// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Native
{
    private const string Dll = "spc_ctrlr_64.dll";
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetDllDirectory(string path);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int scConnect2([MarshalAs(UnmanagedType.I1)] bool alwaysReceive, string applicationName);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scDisconnect();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scGetDevNum(out int count, out int used, out int maxIndex);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scGetSens(int device, out int translation, out int rotation);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scSetSens(int device, ref int translation, ref int rotation);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scGetNullRad(int device, out int value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scSetNullRad(int device, ref int value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int scSetStdZero(int device);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int scSetDsplString(int device, int row, string value, ref int count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int scGetDaemonPar(int parameter, out int value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int scSetDaemonPar(int parameter, ref int value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int scFetchStdData(int device, out short x, out short y, out short z,
        out short a, out short b, out short c, out int translationLevel, out int rotationLevel,
        out int eventCode, out int seconds, out int microseconds);
}

internal sealed class AdapterClient : IDisposable
{
    private readonly object sendLock = new object();
    internal readonly TcpClient Tcp;
    internal readonly StreamWriter Writer;
    internal string Name = "Adapter";
    internal AdapterContext Context;
    internal long ContextTick;
    internal AdapterClient(TcpClient tcp)
    {
        Tcp = tcp;
        Tcp.NoDelay = true; Tcp.SendTimeout = 300;
        Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false));
        Writer.AutoFlush = true;
    }
    internal bool Send(string json)
    {
        try { lock(sendLock) Writer.WriteLine(json); return true; }
        catch { return false; }
    }
    public void Dispose() { try { Tcp.Close(); } catch { } }
}

internal sealed class ControllerHub : IDisposable
{
    private readonly HubSettings settings;
    private readonly List<AdapterClient> clients = new List<AdapterClient>();
    private readonly object clientsLock = new object();
    private Thread controllerThread, listenerThread;
    private TcpListener listener;
    private volatile bool running;
    private double fx, fy, fz, fa, fb, fc;
    private int lastSeconds = -1, lastMicroseconds = -1, currentButtons;
    private bool sensitivityMode;
    private bool daemonAutoChangeCaptured;
    private int daemonAutoChange;
    private VendorButtonLease buttonLease;
    private volatile bool settingsPending = true;
    private readonly LcdWriter displayWriter=new LcdWriter();
    private long nextDisplayTick;
    private int calibrationRequested,calibrationCancelled;
    internal readonly Calibration Calibration=new Calibration();
    internal int ButtonMask;
    internal readonly LiveFeedback Feedback=new LiveFeedback();
    internal double[] LiveAxes = new double[6];
    internal string LastStatus = "Verbinde Controller …";
    internal event Action<string> StatusChanged;
    internal event Action ClientsChanged;
    private volatile bool deviceConnected;
    internal bool DeviceConnected
    {
        get{return deviceConnected;}
        private set{deviceConnected=value;}
    }
    internal int ClientCount { get { lock (clientsLock) return clients.Count; } }
    internal string ClientNames { get { lock (clientsLock) return clients.Count == 0 ? "Kein CAD-Adapter" : string.Join(", ", clients.Select(c => c.Name).Distinct().ToArray()); } }
    internal bool IsConnected(string app) { lock(clientsLock) return clients.Any(c=>c.Name==app); }
    internal AdapterContext ActiveContext
    {
        get{lock(clientsLock){var client=clients.Where(c=>c.Name==settings.ActiveProfile&&c.Context!=null&&Stopwatch.GetTimestamp()-c.ContextTick<Stopwatch.Frequency*2).OrderByDescending(c=>c.ContextTick).FirstOrDefault();return client==null?null:client.Context;}}
    }
    private object AdapterConfig(){return new{type="config",number_ring=settings.Active.NumberRingEnabled,number_step=settings.Active.NumberRingStep,number_sketch_step_mm=settings.Active.SketchRingStep,number_sketch_angle_step_deg=settings.Active.SketchAngleRingStep};}
    internal string[] DisplayPreview {get{return HubDisplay.Build(settings.Active,ActiveContext,sensitivityMode,Calibration.Info);}}

    internal ControllerHub(HubSettings settings) { this.settings = settings; }
    private static void CalibrationLog(string text)
    {
        try {Directory.CreateDirectory(HubSettings.DirectoryName);File.AppendAllText(Path.Combine(HubSettings.DirectoryName,"calibration.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine);}catch{}
    }
    internal void RequestCalibration()
    {
        if(!running || !DeviceConnected)throw new InvalidOperationException("Kein Controller verbunden.");
        if(Calibration.Info.Busy)throw new InvalidOperationException("Eine Nullstellung läuft bereits.");
        CalibrationLog("Manuelle Nullstellung angefordert.");
        Interlocked.CompareExchange(ref calibrationRequested,1,0);
    }
    internal void CancelCalibration(){CalibrationLog("Abbruch angefordert.");Interlocked.Exchange(ref calibrationCancelled,1);}
    private void ResetMotion()
    {
        fx=fy=fz=fa=fb=fc=0;LiveAxes=new double[6];
        Broadcast(new {type="motion",x=0,y=0,z=0,a=0,b=0,c=0,sec=lastSeconds,usec=lastMicroseconds});
    }
    private int SetHardwareZero()
    {
        int count,used,max;int status=Native.scGetDevNum(out count,out used,out max);
        CalibrationLog("Geräteprüfung: SDK="+status+", aktive Geräte="+used);
        if(status!=0 || used<1)throw new InvalidOperationException("Controller nicht mehr verbunden.");
        status=Native.scSetStdZero(0);CalibrationLog("scSetStdZero: SDK="+status);return status;
    }
    private bool CalibrationPausesInput {get{return Volatile.Read(ref calibrationRequested)!=0 || Calibration.Info.Busy;}}
    internal void Start()
    {
        if (running) return;
        running = true;
        listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "CAD adapter bus" };
        controllerThread = new Thread(ControllerSupervisor) { IsBackground = true, Name = "SpaceController input" };
        listenerThread.Start(); controllerThread.Start();
    }
    private void Say(string value) { LastStatus=value; var h = StatusChanged; if (h != null) h(value); }
    private static void EnsureDaemon()
    {
        if (Process.GetProcessesByName("sc_daemon").Length > 0) return;
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"SpaceControl","sc_daemon.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("SpaceControl-Dienst fehlt.\nTreiberpaket installieren.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (Process.GetProcessesByName("sc_daemon").Length == 0 && DateTime.UtcNow < deadline) Thread.Sleep(200);
        if(Process.GetProcessesByName("sc_daemon").Length==0)throw new InvalidOperationException("SpaceControl-Dienst\nstartet nicht.");
        Thread.Sleep(1000);
    }
    private void ListenLoop()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, settings.Port); listener.Start();
            while (running)
            {
                TcpClient tcp = listener.AcceptTcpClient();
                AdapterClient client = new AdapterClient(tcp);
                lock (clientsLock) clients.Add(client);
                ThreadPool.QueueUserWorkItem(ReadClient, client);
                var changed = ClientsChanged; if (changed != null) changed();
            }
        }
        catch (Exception ex) { if (running) Say("Adapter-Bus: " + ex.Message); }
    }
    private void ReadClient(object state)
    {
        AdapterClient client = (AdapterClient)state;
        try
        {
            StreamReader reader = new StreamReader(client.Tcp.GetStream(), new UTF8Encoding(false));
            string line;
            while (running && (line = reader.ReadLine()) != null)
            {
                Dictionary<string, object> msg = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line);
                if (msg.ContainsKey("type") && Convert.ToString(msg["type"]) == "hello" && msg.ContainsKey("app"))
                {
                    client.Name = Convert.ToString(msg["app"]);
                    client.Send(new JavaScriptSerializer().Serialize(new {
                        type = "ready", protocol = 1, device = DeviceConnected,
                        translation = settings.Active.Translation,
                        rotation = settings.Active.Rotation,
                        deadzone = settings.Active.Deadzone,
                        smoothing = settings.Active.Smoothing,
                        number_ring=settings.Active.NumberRingEnabled,
                        number_step=settings.Active.NumberRingStep,
                        number_sketch_step_mm=settings.Active.SketchRingStep,
                        number_sketch_angle_step_deg=settings.Active.SketchAngleRingStep,
                        features=new[]{"context.v1","ring-source.v1","display.templates.v1"}
                    }));
                    settingsPending=true;
                    var changed = ClientsChanged; if (changed != null) changed();
                }
                else if(msg.ContainsKey("type")&&Convert.ToString(msg["type"])=="context"&&msg.ContainsKey("version")&&Convert.ToInt32(msg["version"])==1)
                {
                    var context=AdapterContext.Parse(msg);
                    lock(clientsLock){if(client.Name==settings.ActiveProfile&&settings.Active.Enabled){client.Context=context;client.ContextTick=Stopwatch.GetTimestamp();}}
                }
            }
        }
        catch { }
        finally
        {
            lock (clientsLock) clients.Remove(client);
            client.Dispose();
            var changed = ClientsChanged; if (changed != null) changed();
        }
    }
    private int Broadcast(object value,bool ring=false)
    {
        string line = new JavaScriptSerializer().Serialize(value);
        int delivered=0;
        lock (clientsLock)
        {
            for (int i = clients.Count - 1; i >= 0; i--)
                if (clients[i].Name == settings.ActiveProfile && settings.Active.Enabled)
                {
                    if(clients[i].Send(line)){delivered++;}
                    else {clients[i].Dispose();clients.RemoveAt(i);}
                }
        }
        return delivered;
    }
    private void ControllerSupervisor()
    {
        while (running)
        {
            ControllerLoop();
            if (running) Thread.Sleep(1500);
        }
    }
    private void ControllerLoop()
    {
        try
        {
            string driverLibrary=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"SpaceControl","libs","win64");
            if(!File.Exists(Path.Combine(driverLibrary,"spc_ctrlr_64.dll")))throw new FileNotFoundException("Treiber/DLL fehlt.\nSpaceControl installieren.");
            EnsureDaemon();
            Native.SetDllDirectory(driverLibrary);
            int status = 1;
            for (int i = 0; i < 30 && status != 0 && running; i++) { status = Native.scConnect2(true, null); if (status != 0) Thread.Sleep(300); }
            if (status != 0) throw new InvalidOperationException("Treiber nicht erreichbar.\nSDK-Verbindung: "+status);
            int count, used, max;
            string presenceError=DevicePresence.Error(Native.scGetDevNum(out count,out used,out max),count,used);
            if(presenceError!=null)throw new InvalidOperationException(presenceError);
            DeviceConnected = true;
            displayWriter.Reset();nextDisplayTick=0;
            // DaemonPar.CFG_AUTO_CHANGE = 3. The vendor daemon otherwise
            // reloads a foreground-application profile after our setup and
            // silently restores WFL. While the Hub owns the controller, CAD
            // profiles are handled by adapters instead.
            if (Native.scGetDaemonPar(3, out daemonAutoChange) == 0)
            {
                daemonAutoChangeCaptured = true;
                int disabled = 0;
                Native.scSetDaemonPar(3, ref disabled);
            }
            buttonLease=new VendorButtonLease();
            buttonLease.Acquire();
            // Keep the wheel directions INACTIVE as set by the lease. Do not
            // trigger WHEEL_TO_SENS: it reactivates the manufacturer's wheel
            // handler and can display its sensitivity UI on each detent.
            ApplySettings(); SetDisplay("CAD Hub"); Say("SpaceController verbunden");
            var presencePoll=new DevicePresence();
            long lastFreshTick=Stopwatch.GetTimestamp();
            while (running)
            {
                if(Interlocked.Exchange(ref calibrationRequested,0)!=0 && Calibration.Begin(Stopwatch.GetTimestamp()))
                {
                    ResetMotion();SetDisplay("Nullstellen: Kappe loslassen");
                    CalibrationLog("Countdown gestartet.");
                }
                if(Interlocked.Exchange(ref calibrationCancelled,0)!=0 && Calibration.Cancel()){ResetMotion();ShowMode();CalibrationLog(Calibration.Info.Text);}
                if(Calibration.Tick(Stopwatch.GetTimestamp(),SetHardwareZero)){ResetMotion();CalibrationLog(Calibration.Info.Text);if(!Calibration.Info.Busy)ShowMode();}
                if(settingsPending) { settingsPending=false; ApplyDeviceSettings(); }
                short x, y, z, a, b, c; int tl, rl, evt, sec, usec;
                status = Native.scFetchStdData(0, out x, out y, out z, out a, out b, out c, out tl, out rl, out evt, out sec, out usec);
                long sampleTick=Stopwatch.GetTimestamp();
                if(status==0 && (sec!=lastSeconds || usec!=lastMicroseconds))lastFreshTick=sampleTick;
                if(sampleTick-lastFreshTick>=Stopwatch.Frequency/5)
                {
                    string error=presencePoll.Check(Stopwatch.GetTimestamp(),()=>DevicePresence.Error(Native.scGetDevNum(out count,out used,out max),count,used));
                    if(error!=null)throw new InvalidOperationException(error);
                    FlushDisplay(sampleTick);
                }
                if (status != 0)
                {
                    // No motion packet is not proof of a disconnect. Query the
                    // daemon's actual device count after 200 ms without a fresh
                    // sample, at most every 750 ms. Brief packet gaps alone do
                    // not add round trips to the active movement stream.
                    if(status!=1)throw new InvalidOperationException(DevicePresence.Error(Native.scGetDevNum(out count,out used,out max),count,used)??("Geräteabfrage fehlerhaft.\nSDK-Status: "+status));
                    if(Calibration.ReadStatus(status)){ResetMotion();ShowMode();CalibrationLog(Calibration.Info.Text);}
                    Thread.Sleep(3);continue;
                }
                if (evt >= 0 && evt < 131072)
                {
                    int mask = evt & 0x1FFFF, pressed = mask & ~currentButtons, released = currentButtons & ~mask;
                    currentButtons = mask;
                    ButtonMask=mask;
                    Feedback.Buttons(mask);
                    for(int key=0;key<17;key++) if((pressed & (1<<key))!=0) Dispatch(settings.Active.Bindings[key]);
                }
                if (sec != lastSeconds || usec != lastMicroseconds)
                {
                    if (evt == 131085 || evt == 131086)
                    {
                        int delta = evt == 131085 ? -1 : 1;
                        Feedback.Ring(delta);
                        RouteRing(delta,CalibrationPausesInput,sensitivityMode,ChangeSensitivity,DispatchRing);
                    }
                    if(!Calibration.Info.Settling)
                    {
                        Filter(ref fx, x); Filter(ref fy, y); Filter(ref fz, z);
                        Filter(ref fa, a); Filter(ref fb, b); Filter(ref fc, c);
                    }
                    lastSeconds = sec; lastMicroseconds = usec;
                    double[] v={fx,fy,fz,fa,fb,fc};
                    LiveAxes=(double[])v.Clone();
                    for(int k=0;k<6;k++) v[k]=settings.Active.AxisEnabled[k] ? v[k]*(settings.Active.AxisInverted[k] ? -1 : 1) : 0;
                    if(!CalibrationPausesInput)Broadcast(new { type = "motion", x = v[0], y = v[1], z = v[2], a = v[3], b = v[4], c = v[5], sec = sec, usec = usec });
                }
                else Thread.Sleep(1);
            }
        }
        catch (Exception ex) { DeviceConnected = false; Say(ex is DllNotFoundException?"Treiber/DLL nicht ladbar.\nSpaceControl prüfen.":ex is BadImageFormatException?"Falsche Treiber-DLL.\nx64-Version erforderlich.":ex.Message); }
        finally
        {
            DeviceConnected=false;
            Calibration.Abort("Controller getrennt · Nullstellung abgebrochen.");Interlocked.Exchange(ref calibrationRequested,0);
            Feedback.Clear();ResetMotion();ButtonMask=0;currentButtons=0;lastSeconds=lastMicroseconds=-1;
            if(buttonLease!=null){try{buttonLease.Dispose();}catch{}buttonLease=null;}
            if (daemonAutoChangeCaptured)
            {
                try { int restore = daemonAutoChange; Native.scSetDaemonPar(3, ref restore); } catch { }
                daemonAutoChangeCaptured = false;
            }
            try { Native.scDisconnect(); } catch { }
        }
    }
    // No native SDK round trips in normal ring routing. Only an explicitly
    // selected Hub sensitivity mode is allowed to change device settings.
    internal static void RouteRing(int delta,bool paused,bool sensitivity,Action<int> changeSensitivity,Action<int> dispatch)
    {
        if(paused)return;
        if(sensitivity)changeSensitivity(delta);else dispatch(delta);
    }
    private void Filter(ref double state, short raw)
    {
        if (Math.Abs((int)raw) <= settings.Active.Deadzone) { state = 0; return; }
        double alpha = Math.Max(.05, Math.Min(1, settings.Active.Smoothing / 100.0));
        state = state * (1 - alpha) + raw * alpha;
        if (Math.Abs(state) < .15) state = 0;
    }
    internal void ApplySettings()
    {
        settings.Save(); settingsPending=true;Broadcast(AdapterConfig());
    }
    private void ApplyDeviceSettings()
    {
        if (!DeviceConnected) return;
        int t = settings.Active.Translation, r = settings.Active.Rotation, n = settings.Active.Deadzone;
        Native.scSetSens(0, ref t, ref r); Native.scSetNullRad(0, ref n); ShowMode();
    }
    private void Dispatch(string action)
    {
        if(CalibrationPausesInput)return;
        if(!settings.Active.Enabled) return;
        if(action=="sensitivity") { sensitivityMode=!sensitivityMode; ShowMode(); }
        else if(action!="none") Broadcast(new {type="action",action=action});
    }
    private void ChangeSensitivity(int delta)
    {
        settings.Active.Translation = Math.Max(0, Math.Min(15, settings.Active.Translation + delta));
        settings.Active.Rotation = Math.Max(0, Math.Min(15, settings.Active.Rotation + delta));
        ApplySettings();
    }
    private void DispatchRing(int delta)
    {
        if(CalibrationPausesInput)return;
        if(!settings.Active.Enabled)return;
        string action=delta<0?settings.Active.RingLeft:settings.Active.RingRight;
        if(action=="sensitivity"){Dispatch(action);return;}
        if(action=="none")return;
        Broadcast(new{type="action",action=action,source="ring",delta=delta},true);
    }
    private void ShowMode()
    {
        if (sensitivityMode) SetDisplay("Sensitivity T" + settings.Active.Translation + " R" + settings.Active.Rotation);
        else SetDisplay(settings.ActiveProfile);
    }
    private void SetDisplay(string value)
    {
        // The complete display frame is composed centrally, not by adapters.
        nextDisplayTick=0;
    }
    private void FlushDisplay(long now)
    {
        if(now<nextDisplayTick)return;nextDisplayTick=now+Stopwatch.Frequency/10;
        string[] frame=HubDisplay.Build(settings.Active,ActiveContext,sensitivityMode,Calibration.Info);
        displayWriter.Tick(now,frame,(row,text)=>{int count=text.Length;int status=Native.scSetDsplString(0,row,text,ref count);return new LcdWriteResult(status,count);},DisplayLog);
    }
    private static void DisplayLog(string message)
    {
        try{Directory.CreateDirectory(HubSettings.DirectoryName);File.AppendAllText(Path.Combine(HubSettings.DirectoryName,"display.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}
    }
    public void Dispose()
    {
        running = false; try { if (listener != null) listener.Stop(); } catch { }
        lock (clientsLock) foreach (var c in clients) c.Dispose();
        // Let the input thread restore CFG_AUTO_CHANGE before disconnecting.
        if(controllerThread!=null && controllerThread.IsAlive) controllerThread.Join(2000);
    }
}

// Device presence is deliberately distinct from motion receive timeouts.
// A stationary controller may send no samples indefinitely and is still connected.
internal sealed class DevicePresence
{
    private long next;
    internal string Check(long now,Func<string> probe)
    {
        if(now<next)return null;
        next=now+Stopwatch.Frequency*3/4;return probe();
    }
    internal static string Error(int status,int count,int used)
    {
        if(status!=0)return "Treiber nicht erreichbar.\nSDK-Geräteprüfung: "+status;
        if(count<1)return "Kein USB-Gerät erkannt.\nKabel/Treiber prüfen.";
        if(used<1)return "USB-Gerät erkannt,\naber im Treiber inaktiv.";
        return null;
    }
}



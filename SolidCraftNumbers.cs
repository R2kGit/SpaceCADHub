// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class NumericField
{
    internal readonly string Literal,Unit,Identity,Label;
    internal readonly decimal Value;
    internal readonly bool IsSketch;
    internal NumericField(string literal,string unit,decimal value,string identity,string label,bool isSketch=false)
    {Literal=literal;Unit=unit;Value=value;Identity=identity;Label=label;IsSketch=isSketch;}
    internal static bool SafeContext(Dictionary<string,object> state)
    {
        var ui=state.ContainsKey("ui")?SolidCraftClient.Dict(state["ui"]):new Dictionary<string,object>();
        return !Flag(ui,"paletteOpen")&&!Flag(state,"file_busy")&&!Flag(state,"file_picker_busy")&&!Flag(state,"file_dialog_open")&&!Flag(state,"quitting");
    }
    internal static NumericField FromFocused(Dictionary<string,object> state,FocusedInput input)
    {
        if(input==null||string.IsNullOrEmpty(input.Identity)||!SafeContext(state))return null;
        // egui expression editors are plain edits. Preserve formulas and exclude
        // known free-text roles even if their content happens to be a number.
        if(Regex.IsMatch(input.Label,@"(?i)name|rename|titel|title|suche|such|search|datei|file|pfad|path|beschreibung|description|code|script|expression|ausdruck|formel|formula"))return null;
        var number=Regex.Match(input.Literal,@"^\s*([+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+-]?\d+)?)\s*(mm|cm|m|in|ft|um|µm|deg|°|rad|%)?\s*$",RegexOptions.CultureInvariant);
        decimal value;
        if(!number.Success||!decimal.TryParse(number.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)||value>1000000000m||value< -1000000000m)return null;
        var ui=state.ContainsKey("ui")?SolidCraftClient.Dict(state["ui"]):new Dictionary<string,object>();object tab;
        bool sketch=ui.TryGetValue("tab",out tab)&&string.Equals(Convert.ToString(tab),"SKETCH",StringComparison.Ordinal);
        return new NumericField(input.Literal,number.Groups[2].Value,value,input.Identity,string.IsNullOrWhiteSpace(input.Label)?"Zahlenfeld":input.Label,sketch);
    }
    internal double EffectiveStep(double body,double sketch,double angle)
    {
        if(!IsSketch)return body;
        switch(Unit)
        {
            case "mm":return sketch;
            case "cm":return sketch/10;
            case "m":return sketch/1000;
            case "in":return sketch/25.4;
            case "ft":return sketch/304.8;
            case "um":case "µm":return sketch*1000;
            case "deg":case "°":return angle;
            case "rad":return angle*Math.PI/180;
            case "":
                if(Regex.IsMatch(Label,@"(?i)\b(angle|winkel)\b"))return angle;
                if(Regex.IsMatch(Label,@"(?i)\b(length|width|height|distance|radius|diameter|offset|länge|laenge|breite|höhe|hoehe|abstand|durchmesser|versatz|x|y)\b"))return sketch;
                break;
        }
        return body;
    }
    private static bool Flag(Dictionary<string,object> state,string name){object value;return state.TryGetValue(name,out value)&&value is bool&&(bool)value;}
    internal string Adjust(int detents,double step)
    {
        decimal result=Value+detents*(decimal)step;
        if(result>1000000000m||result< -1000000000m)throw new InvalidOperationException("Zahlenwert außerhalb des sicheren Bereichs.");
        return result.ToString("0.#########",CultureInfo.InvariantCulture)+(Unit.Length>0?" "+Unit:"");
    }
    internal static bool HasDialog(Dictionary<string,object> state)
    {return !SafeContext(state)||state.ContainsKey("dialog")&&state["dialog"]!=null||state.ContainsKey("tool")&&state["tool"]!=null;}
}

internal sealed class SolidCraftNumbers : IDisposable
{
    private sealed class Turn{internal int Delta;internal long Tick,Generation;internal string Action,Mode,Identity;}
    private readonly object gate=new object();
    private readonly Queue<Turn> turns=new Queue<Turn>();
    private readonly Queue<string> zooms=new Queue<string>();
    private readonly Action<AdapterContext> publish;
    private readonly Func<bool> foreground;
    private readonly Func<FocusedInput> readFocus;
    private readonly int port;
    private readonly string tokenFile;
    private readonly Thread worker;
    private volatile bool running=true,enabled;
    private double step=1,sketchStep=.1,sketchAngleStep=.1;
    private long generation;
    private string captureMode="blocked",captureIdentity;
    private volatile bool writesBlocked;
    private TcpClient connection;
    private StreamReader reader;
    private StreamWriter writer;
    private int requestId;
    private readonly JavaScriptSerializer json=new JavaScriptSerializer();
    internal SolidCraftNumbers(int port,string tokenFile,Action<AdapterContext> publish,Func<bool> foreground=null,Func<FocusedInput> readFocus=null)
    {
        this.port=port;this.tokenFile=tokenFile;this.publish=publish;
        this.foreground=foreground??IsSolidCraftForeground;this.readFocus=readFocus??FocusedInput.ReadSolidCraft;
        worker=new Thread(Run){IsBackground=true,Name="SolidCraft parameter context"};worker.Start();
    }
    internal void Configure(bool enable,double numberStep,double sketchLengthStep=.1,double sketchRotationStep=.1)
    {
        foreach(double v in new[]{numberStep,sketchLengthStep,sketchRotationStep})if(double.IsNaN(v)||double.IsInfinity(v)||v<.001||v>1000)return;
        lock(gate){if(!enabled&&enable)writesBlocked=false;enabled=enable;step=numberStep;sketchStep=sketchLengthStep;sketchAngleStep=sketchRotationStep;generation++;turns.Clear();zooms.Clear();Monitor.PulseAll(gate);}
    }
    private void Publish(AdapterContext context,string identity=null)
    {lock(gate){captureMode=context.Mode;captureIdentity=identity;}try{publish(context);}catch{}}
    internal bool QueueRing(string action)
    {
        if(!enabled)return false;
        lock(gate)
        {
            
            if(captureMode!="blocked"&&turns.Count<24){turns.Enqueue(new Turn{Delta=action=="zoom_in"?1:-1,Action=action,Tick=Stopwatch.GetTimestamp(),Generation=generation,Mode=captureMode,Identity=captureIdentity});}
            Monitor.PulseAll(gate);
        }return true;
    }
    internal string[] TakeZooms(){lock(gate){var result=zooms.ToArray();zooms.Clear();return result;}}
    private Dictionary<string,object> Call(string method,Dictionary<string,object> parameters)
    {
        var message=new Dictionary<string,object>{{"id",++requestId},{"method",method},{"params",parameters}};
        if(requestId==1)message["token"]=File.ReadAllText(tokenFile??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SolidCraft","control",port+".token")).Trim();
        writer.WriteLine(json.Serialize(message));string line=reader.ReadLine();if(line==null)throw new IOException("SolidCraft-Kontextverbindung geschlossen.");
        var reply=json.Deserialize<Dictionary<string,object>>(line);
        if(!reply.ContainsKey("ok")||!Convert.ToBoolean(reply["ok"]))throw new IOException("SolidCraft-Kontextaufruf fehlgeschlagen: "+method);
        object result;return reply.TryGetValue("result",out result)?SolidCraftClient.Dict(result):new Dictionary<string,object>();
    }
    private NumericField CurrentField()
    {return NumericField.FromFocused(Call("ui.inspect",new Dictionary<string,object>()),readFocus());}
    private void Run()
    {
        while(running)
        {
            try
            {
                connection=new TcpClient("127.0.0.1",port);connection.NoDelay=true;connection.ReceiveTimeout=1500;connection.SendTimeout=500;
                reader=new StreamReader(connection.GetStream(),Encoding.UTF8);writer=new StreamWriter(connection.GetStream(),new UTF8Encoding(false)){AutoFlush=true};requestId=0;
                while(running)
                {
                    var state=Call("ui.inspect",new Dictionary<string,object>());
                    bool active=foreground();var input=active&&enabled?readFocus():null;
                    var field=NumericField.FromFocused(state,input);
                    bool navigation=!NumericField.HasDialog(state)&&input==null;
                    if(navigation)writesBlocked=false;
                    double currentStep;long currentGeneration;lock(gate){currentStep=field==null?step:field.EffectiveStep(step,sketchStep,sketchAngleStep);currentGeneration=generation;}
                    Publish(!enabled?new AdapterContext("navigation","Ansicht","","Zahlenring ausgeschaltet"):writesBlocked?new AdapterContext("blocked","Feldpruefung fehlgeschlagen"):!active?new AdapterContext("blocked","SolidCraft nicht aktiv"):field!=null?new AdapterContext("numeric",field.Label,field.Literal,"",currentStep):!navigation?new AdapterContext("blocked",input!=null?"Kein einfacher Zahlenwert":"Zahlenfeld anklicken"):new AdapterContext("navigation","Ansicht"),field==null?null:field.Identity);
                    Turn[] pending;lock(gate){pending=turns.ToArray();turns.Clear();}
                    int delta=0;
                    foreach(var turn in pending)
                    {
                        long age=Stopwatch.GetTimestamp()-turn.Tick;
                        if(!enabled){continue;}
                        if(turn.Generation!=currentGeneration){continue;}
                        if(!active){continue;}
                        if(writesBlocked){continue;}
                        if(age>Stopwatch.Frequency/2){continue;}
                        if(turn.Mode=="numeric"&&field!=null&&turn.Identity==field.Identity){delta+=turn.Delta;}
                        else if(turn.Mode=="navigation"&&navigation)
                        {
                            lock(gate)if(zooms.Count<24)zooms.Enqueue(turn.Action);
                        }
                    }
                    
                    
                    if(delta!=0&&field!=null)try{Adjust(field,Math.Max(-16,Math.Min(16,delta)),currentStep,currentGeneration);}catch{writesBlocked=true;throw;}
                    lock(gate){if(running&&turns.Count==0)Monitor.Wait(gate,100);}
                }
            }
            catch{if(running)Publish(new AdapterContext("blocked","CAD-Kontext nicht erreichbar"));lock(gate){turns.Clear();zooms.Clear();if(running)Monitor.Wait(gate,750);}}
            finally{if(connection!=null){connection.Close();connection=null;}}
        }
    }
    private void Adjust(NumericField expected,int delta,double currentStep,long currentGeneration)
    {
        lock(gate)if(currentGeneration!=generation)return;
        if(!enabled||!foreground()){return;}
        // Keep user focus: no handle lookup, click or guessed field.
        var latest=CurrentField();
        if(latest==null||latest.Identity!=expected.Identity||latest.Literal!=expected.Literal||latest.IsSketch!=expected.IsSketch){return;}
        string next=latest.Adjust(delta,currentStep);
        if(!enabled||!foreground()){return;}
        Call("ui.key",new Dictionary<string,object>{{"key","A"},{"cmd",true}});Thread.Sleep(30);
        var beforeText=CurrentField();
        lock(gate)if(currentGeneration!=generation)return;
        if(!enabled||!foreground()||beforeText==null||beforeText.Identity!=expected.Identity||beforeText.Literal!=expected.Literal){return;}
        Call("ui.text",new Dictionary<string,object>{{"text",next}});
        
        // Accessibility changes arrive next frame. Never resend failed input.
        for(int attempt=0;attempt<5&&running;attempt++)
        {
            Thread.Sleep(35);var after=CurrentField();
            if(after==null||after.Identity!=expected.Identity){return;}
            if(after.Literal==next){Publish(new AdapterContext("numeric",after.Label,after.Literal,"",currentStep),after.Identity);return;}
        }
        throw new IOException("Zahlenwert konnte nicht verifiziert werden.");
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    private static bool IsSolidCraftForeground(){try{uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);using(var p=Process.GetProcessById((int)pid))return p.ProcessName.Equals("SolidCraft",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    public void Dispose(){running=false;lock(gate)Monitor.PulseAll(gate);var socket=connection;if(socket!=null)socket.Close();worker.Join(2000);}
}

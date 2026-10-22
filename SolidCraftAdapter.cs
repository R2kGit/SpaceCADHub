// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class SolidCraftAdapterProgram
{
    private static readonly object Gate=new object();
    private static Dictionary<string,object> motion;
    private static long motionTick;
    private sealed class InputAction{internal string Action;internal bool Ring;internal long Tick;}
    private static readonly Queue<InputAction> actions=new Queue<InputAction>();
    private static void Log(string text)
    {
        try { string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SpaceControllerCadHub"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"solidcraft-adapter.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine); } catch { }
    }
    private static double Value(Dictionary<string,object> m,string key){return m.ContainsKey(key) ? Convert.ToDouble(m[key]) : 0;}
    public static void Main(string[] args)
    {
        int hubPort=17861,cadPort=7878;string tokenFile=null;
        for(int i=0;i+1<args.Length;i+=2)
        {
            if(args[i]=="--hub-port")hubPort=int.Parse(args[i+1]);
            else if(args[i]=="--cad-port")cadPort=int.Parse(args[i+1]);
            else if(args[i]=="--token-file")tokenFile=args[i+1];
        }
        bool first;
        using(var instance=new Mutex(true,"Local\\CADHubSolidCraftAdapter"+hubPort,out first))
        {
            if(!first)return;
            while(true)
            {
                try
                {
                    using(var cad=new SolidCraftClient(cadPort,tokenFile))
                    using(var bus=new TcpClient("127.0.0.1",hubPort))
                    {
                        bus.NoDelay=true;
                        var writer=new StreamWriter(bus.GetStream(),new UTF8Encoding(false)){AutoFlush=true};
                        writer.WriteLine("{\"type\":\"hello\",\"app\":\"SolidCraft\",\"version\":\"1.0.2\"}");
                        var sendGate=new object();var serializer=new JavaScriptSerializer();
                        using(var numbers=new SolidCraftNumbers(cadPort,tokenFile,context=>{lock(sendGate)writer.WriteLine(serializer.Serialize(context.Message()));},null,null))
                        {
                        bool receiving=true;
                        lock(Gate){motion=null;actions.Clear();}
                        var readerThread=new Thread(delegate()
                        {
                            try
                            {
                                var reader=new StreamReader(bus.GetStream());var json=new JavaScriptSerializer();string line;
                                while((line=reader.ReadLine())!=null)
                                {
                                    var msg=json.Deserialize<Dictionary<string,object>>(line);
                                    lock(Gate)
                                    {
                                        string type=Convert.ToString(msg["type"]);
                                        if(type=="motion"){motion=msg;motionTick=Stopwatch.GetTimestamp();}
                                        else if(type=="action")
                                        {
                                            bool ring=msg.ContainsKey("source")&&Convert.ToString(msg["source"])=="ring";
                                            if(actions.Count<64)actions.Enqueue(new InputAction{Action=Convert.ToString(msg["action"]),Ring=ring,Tick=Stopwatch.GetTimestamp()});
                                        }
                                        else if((type=="ready"||type=="config")&&msg.ContainsKey("number_ring"))numbers.Configure(Convert.ToBoolean(msg["number_ring"]),msg.ContainsKey("number_step")?Convert.ToDouble(msg["number_step"]):1,msg.ContainsKey("number_sketch_step_mm")?Convert.ToDouble(msg["number_sketch_step_mm"]):.1,msg.ContainsKey("number_sketch_angle_step_deg")?Convert.ToDouble(msg["number_sketch_angle_step_deg"]):.1);
                                    }
                                }
                            }
                            catch(Exception e){Log(e.Message);}
                            finally { Volatile.Write(ref receiving,false); }
                        }){IsBackground=true};readerThread.Start();Log("SolidCraft und Hub verbunden");
                        long next=0;
                        while(Volatile.Read(ref receiving) && cad.IsConnected)
                        {
                            long now=Stopwatch.GetTimestamp();
                            if(now<next){Thread.Sleep(1);continue;}
                            next=now+Stopwatch.Frequency*16/1000;
                            Dictionary<string,object> m;InputAction[] pending;long sampleTime;
                            lock(Gate){m=motion;sampleTime=motionTick;pending=actions.ToArray();actions.Clear();}
                            int ringZoom=0;
                            foreach(var action in pending)
                            {
                                if(action.Ring&&(action.Action=="zoom_in"||action.Action=="zoom_out"))
                                {
                                    if(!numbers.QueueRing(action.Action)){ringZoom+=action.Action=="zoom_in"?1:-1;}
                                    continue;
                                }
                                cad.ExecuteAction(action.Action);
                            }
                            foreach(string zoom in numbers.TakeZooms()){ringZoom+=zoom=="zoom_in"?1:-1;}
                            // Coalesce bursts/opposite detents within one 16 ms
                            // frame instead of queuing many synthetic events.
                            cad.ExecuteRingZoom(ringZoom);
                            cad.TickRingZoom(now);
                            if(m==null || now-sampleTime>Stopwatch.Frequency/8 || !cad.NavigationReady)continue;
                            double x=Value(m,"x"),y=Value(m,"y"),z=Value(m,"z"),a=Value(m,"a"),b=Value(m,"b"),c=Value(m,"c");
                            if(Math.Max(Math.Abs(x),Math.Max(Math.Abs(y),Math.Max(Math.Abs(z),Math.Max(Math.Abs(a),Math.Max(Math.Abs(b),Math.Abs(c))))))<0.01)continue;
                            double mx=-x*.13,my=-z*.13,mz=y*.13;
                            if(Math.Max(Math.Abs(a),Math.Max(Math.Abs(b),Math.Abs(c)))>=10)mx=my=mz=0;
                            cad.NavigateCamera(mx,my,mz,0,b*.012,-a*.012,c);
                        }
                        }
                    }
                }
                catch(Exception e){Log(e.Message);}
                Thread.Sleep(1500);
            }
        }
    }
}

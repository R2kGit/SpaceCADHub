// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class SolidCraftClient : IDisposable
{
    private readonly TcpClient client;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private int nextId = 1;
    private Thread replyThread;
    private volatile bool readingReplies;
    private int outstandingReplies;
    private readonly object replyStateLock = new object();
    private readonly HashSet<int> viewReplyIds = new HashSet<int>();
    private sealed class CameraRead {internal long Generation,DistanceEpoch;}
    private readonly Dictionary<int,CameraRead> distanceReplyIds = new Dictionary<int,CameraRead>();
    private readonly object cameraStateLock = new object();
    private long cameraGeneration,distanceEpoch;
    private long wheelReadUntil, nextWheelRead;
    private bool wheelZoomReversed;
    private long navigationResumeTick;
    internal Dictionary<string, object> Camera;
    private object[] orbitPivot = new object[] { 0.0, 0.0, 0.0 };
    internal double ViewportLeft;
    internal double ViewportTop;
    internal double ViewportWidth = 1200;
    internal double ViewportHeight = 800;
    internal bool IsConnected { get { return readingReplies; } }
    private double pointerX;
    private double pointerY;
    private bool pointerNavigating;

    internal SolidCraftClient(int port, string tokenFile = null)
    {
        string tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolidCraft", "control", port.ToString(CultureInfo.InvariantCulture) + ".token");
        string token = File.ReadAllText(tokenFile ?? tokenPath).Trim();

        client = new TcpClient("127.0.0.1", port);
        client.NoDelay = true; client.ReceiveTimeout = 5000; client.SendTimeout = 1000;
        NetworkStream stream = client.GetStream();
        reader = new StreamReader(stream, new UTF8Encoding(false));
        writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.AutoFlush = true;

        Dictionary<string, object> result = Request("ui.inspect", new Dictionary<string, object>(), token);
        Camera = Dict(result["camera"]);
        Dictionary<string, object> originalCamera = new Dictionary<string, object>(Camera);
        Dictionary<string, object> fitArgs = new Dictionary<string, object>();
        fitArgs["view"] = "fit";
        fitArgs["animate"] = false;
        Dictionary<string, object> fitCamera = Request("ui.view", fitArgs, null);
        if (fitCamera.ContainsKey("camera")) fitCamera = Dict(fitCamera["camera"]);
        if (fitCamera.ContainsKey("target")) orbitPivot = Array(fitCamera["target"]);
        Dictionary<string, object> restore = new Dictionary<string, object>();
        foreach (KeyValuePair<string, object> item in originalCamera) restore[item.Key] = item.Value;
        Request("ui.camera", restore, null);
        Camera = originalCamera;
        object[] viewport = Array(result["viewport"]);
        if (viewport.Length == 4)
        {
            ViewportLeft = Number(viewport[0]);
            ViewportTop = Number(viewport[1]);
            // ui.inspect publishes [left, top, width, height], not corners.
            ViewportWidth = Math.Max(1, Number(viewport[2]));
            ViewportHeight = Math.Max(1, Number(viewport[3]));
        }
        pointerX = ViewportLeft + ViewportWidth * 0.5;
        pointerY = ViewportTop + ViewportHeight * 0.5;

        Dictionary<string, object> cursor = new Dictionary<string, object>();
        cursor["show"] = false;
        cursor["speed"] = "instant";
        Request("ui.agent_cursor", cursor, null);

        Dictionary<string, object> navigationPrefs = new Dictionary<string, object>();
        navigationPrefs["nav"] = "fusion";
        navigationPrefs["smooth_wheel_zoom"] = true;
        // Keeps SolidCraft's viewport event loop awake so control-channel
        // camera updates are consumed every frame instead of in ~320 ms bursts.
        navigationPrefs["show_frame_rate"] = false;
        var appliedPrefs=Request("ui.prefs", navigationPrefs, null);
        object reversed;
        wheelZoomReversed=appliedPrefs.TryGetValue("zoom_reverse",out reversed)&&Convert.ToBoolean(reversed);

        client.ReceiveTimeout = 0;
        StartReplyReader();
    }

    private void StartReplyReader()
    {
        readingReplies = true;
        replyThread = new Thread(delegate()
        {
            JavaScriptSerializer replyJson = new JavaScriptSerializer();
            try
            {
                while (readingReplies)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    Interlocked.Decrement(ref outstandingReplies);
                    Dictionary<string, object> reply = Dict(replyJson.DeserializeObject(line));
                    bool refreshCamera = false;
                    CameraRead distanceRead=null;
                    if (reply.ContainsKey("id"))
                    {
                        int replyId = Convert.ToInt32(reply["id"], CultureInfo.InvariantCulture);
                        lock (replyStateLock)
                        {
                            refreshCamera = viewReplyIds.Remove(replyId);
                            if(distanceReplyIds.TryGetValue(replyId,out distanceRead))distanceReplyIds.Remove(replyId);
                        }
                    }
                    if (reply.ContainsKey("ok") && !(bool)reply["ok"])
                        Console.Error.WriteLine("Async camera update rejected: " +
                            (reply.ContainsKey("error") ? Convert.ToString(reply["error"]) : line));
                    else if (refreshCamera && reply.ContainsKey("result"))
                    {
                        Dictionary<string, object> result = Dict(reply["result"]);
                        lock(cameraStateLock)
                        {
                            if (result.ContainsKey("camera"))Camera = Dict(result["camera"]);
                            else if (result.ContainsKey("target") && result.ContainsKey("distance"))Camera = result;
                        }
                    }
                    else if(distanceRead!=null&&reply.ContainsKey("result"))
                    {
                        var result=Dict(reply["result"]);
                        var camera=result.ContainsKey("camera")?Dict(result["camera"]):result;
                        object value;
                        if(camera.TryGetValue("distance",out value))
                        {
                            double distance=Number(value);
                            if(distance>0&&!double.IsNaN(distance)&&!double.IsInfinity(distance))
                                lock(cameraStateLock)
                                {
                                    // Ignore an inspection predating a fixed
                                    // view or explicit zoom-button command.
                                    if(distanceRead.DistanceEpoch==distanceEpoch)
                                    {
                                        // With no newer local head command,
                                        // also adopt native zoom-anchor target
                                        // changes. Otherwise never rewind the
                                        // newer local target/orientation.
                                        if(distanceRead.Generation==cameraGeneration&&camera.ContainsKey("target")&&camera.ContainsKey("yaw"))Camera=camera;
                                        else Camera["distance"]=distance;
                                    }
                                }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (readingReplies) Console.Error.WriteLine("Camera reply reader stopped: " + ex.Message);
            }
            finally
            {
                readingReplies = false;
            }
        });
        replyThread.IsBackground = true;
        replyThread.Name = "SolidCraft camera replies";
        replyThread.Start();
    }

    private void SendWithoutWaiting(string method, Dictionary<string, object> parameters)
    {
        // Avoid unlimited growth if SolidCraft is genuinely unavailable.
        if (Volatile.Read(ref outstandingReplies) >= 120)return;
        Dictionary<string, object> request = new Dictionary<string, object>();
        int requestId = nextId++;
        request["id"] = requestId;
        request["method"] = method;
        request["params"] = parameters;
        request["animate"] = false;
        if (method == "ui.view")
        {
            lock (replyStateLock) viewReplyIds.Add(requestId);
        }
        if(method=="ui.inspect")
        {
            CameraRead capture;
            lock(cameraStateLock)capture=new CameraRead{Generation=cameraGeneration,DistanceEpoch=distanceEpoch};
            lock(replyStateLock)distanceReplyIds.Add(requestId,capture);
        }
        Interlocked.Increment(ref outstandingReplies);
        writer.WriteLine(json.Serialize(request));
    }

    internal void HandleButtons(int pressedMask)
    {
        if ((pressedMask & 1) != 0) SendKey("Z", true);       // 1: Undo
        if ((pressedMask & 2) != 0) SendKey("Y", true);       // 2: Redo
        if ((pressedMask & 4) != 0) SendView("iso");          // 3: ISO
        if ((pressedMask & 512) != 0) SendKey("Escape", false);
        if ((pressedMask & 1024) != 0) SendView("front");
        if ((pressedMask & 2048) != 0) SendView("right");
        if ((pressedMask & 4096) != 0) SendView("top");
        if ((pressedMask & 8192) != 0) SendView("fit");
    }

    internal void SendKey(string key, bool control)
    {
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["key"] = key;
        if (control) args["ctrl"] = true;
        SendWithoutWaiting("ui.key", args);
    }
    internal void ExecuteAction(string action)
    {
        if(action=="none") return;
        if(action=="undo") SendKey("Z",true);
        else if(action=="redo") SendKey("Y",true);
        else if(action=="escape") SendKey("Escape",false);
        else if(action=="zoom_in" || action=="zoom_out") NavigateCamera(0,0,0,action=="zoom_in" ? 40 : -40,0,0,0);
        else if(action.StartsWith("key:"))
        {
            string[] keys=action.Substring(4).Split('+');
            var args=new Dictionary<string,object>();
            args["key"]=keys[keys.Length-1].Trim();
            args["ctrl"]=System.Array.Exists(keys,k=>k.Trim().Equals("Ctrl",StringComparison.OrdinalIgnoreCase));
            args["alt"]=System.Array.Exists(keys,k=>k.Trim().Equals("Alt",StringComparison.OrdinalIgnoreCase));
            args["shift"]=System.Array.Exists(keys,k=>k.Trim().Equals("Shift",StringComparison.OrdinalIgnoreCase));
            SendWithoutWaiting("ui.key",args);
        }
        else SendView(action);
    }

    internal void SendView(string view)
    {
        lock(cameraStateLock){cameraGeneration++;distanceEpoch++;}
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["view"] = view;
        args["animate"] = false;
        // The reply contains SolidCraft's new fixed-view camera.  Until that
        // arrives, do not send one more movement using our previous 3D camera.
        navigationResumeTick = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 4;
        SendWithoutWaiting("ui.view", args);
    }

    internal bool NavigationReady
    {
        get { return Stopwatch.GetTimestamp() >= Interlocked.Read(ref navigationResumeTick); }
    }

    // Ring-only path. Buttons and numeric fields keep their previous behavior.
    // Use egui's native wheel smoothing instead of an absolute camera jump.
    internal void ExecuteRingZoom(int detents)
    {
        if(detents==0)return;
        if(!NavigationReady)return;
        var args=new Dictionary<string,object>();
        args["x"]=ViewportLeft+ViewportWidth*.5;
        args["y"]=ViewportTop+ViewportHeight*.5;
        // Honor Hub binding direction independently of the mouse preference.
        args["delta"]=Clamp(detents,-8,8)*40*(wheelZoomReversed?-1:1);
        SendWithoutWaiting("ui.scroll",args);
        long now=Stopwatch.GetTimestamp();
        if(now>wheelReadUntil)nextWheelRead=0;
        wheelReadUntil=now+Stopwatch.Frequency*2;
    }
    internal void TickRingZoom(long now)
    {
        if(now>wheelReadUntil||now<nextWheelRead||!NavigationReady)return;
        lock(replyStateLock)if(distanceReplyIds.Count!=0)return;
        nextWheelRead=now+Stopwatch.Frequency/25;
        // At most one read outstanding, async: no waiting in head navigation.
        SendWithoutWaiting("ui.inspect",new Dictionary<string,object>());
    }

    internal void NavigateCamera(double moveX, double moveY, double moveZ, double zoom,
        double orbitX, double orbitY, double rollDelta)
    {
        lock(cameraStateLock)
        {
        double yaw = Camera.ContainsKey("yaw") ? Number(Camera["yaw"]) : 0;
        double pitch = Camera.ContainsKey("pitch") ? Number(Camera["pitch"]) : 0;
        double distance = Camera.ContainsKey("distance") ? Number(Camera["distance"]) : 100;
        double roll = Camera.ContainsKey("roll") ? Number(Camera["roll"]) : 0;
        object[] targetValues = Camera.ContainsKey("target") ? Array(Camera["target"]) : new object[0];
        double tx = targetValues.Length > 0 ? Number(targetValues[0]) : 0;
        double ty = targetValues.Length > 1 ? Number(targetValues[1]) : 0;
        double tz = targetValues.Length > 2 ? Number(targetValues[2]) : 0;

        if (moveX != 0 || moveY != 0 || moveZ != 0)
        {
            double unitsPerPixel = 0.8 * distance / ViewportHeight;
            // Keep the controller's three translations tied to SolidCraft's
            // model axes.  Screen-relative panning swaps Y/Z in front/right
            // views, which makes the physical cap directions feel incorrect.
            bool sideView = Math.Abs(pitch) < 0.02 &&
                Math.Abs(Math.Abs(yaw) - Math.PI / 2) < 0.02;
            tx += (sideView ? moveY : moveX) * unitsPerPixel;
            ty += (sideView ? moveX : moveY) * unitsPerPixel;
            tz += moveZ * unitsPerPixel;
        }

        if (orbitX != 0 || orbitY != 0)
        {
            double pivotX = orbitPivot.Length > 0 ? Number(orbitPivot[0]) : 0;
            double pivotY = orbitPivot.Length > 1 ? Number(orbitPivot[1]) : 0;
            double pivotZ = orbitPivot.Length > 2 ? Number(orbitPivot[2]) : 0;
            double vx = tx - pivotX, vy = ty - pivotY, vz = tz - pivotZ;
            double yawDelta = Clamp(orbitX, -20, 20) * 0.008;
            double pitchDelta = Clamp(orbitY, -20, 20) * 0.008;

            // SolidCraft's native pivot orbit rotates the camera target around
            // model Z in the opposite direction to the yaw change.
            if (yawDelta != 0)
            {
                double cosine = Math.Cos(-yawDelta), sine = Math.Sin(-yawDelta);
                double rotatedX = vx * cosine - vy * sine;
                double rotatedY = vx * sine + vy * cosine;
                vx = rotatedX; vy = rotatedY;
                yaw += yawDelta;
            }

            // Vertical orbit rotates that vector around the camera's right axis.
            if (pitchDelta != 0)
            {
                double axisX = Math.Cos(yaw), axisY = -Math.Sin(yaw);
                double angle = -pitchDelta;
                double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                double dot = axisX * vx + axisY * vy;
                double crossX = axisY * vz;
                double crossY = -axisX * vz;
                double crossZ = axisX * vy - axisY * vx;
                vx = vx * cosine + crossX * sine + axisX * dot * (1 - cosine);
                vy = vy * cosine + crossY * sine + axisY * dot * (1 - cosine);
                vz = vz * cosine + crossZ * sine;
                pitch = Clamp(pitch + pitchDelta, -1.55, 1.55);
            }
            tx = pivotX + vx;
            ty = pivotY + vy;
            tz = pivotZ + vz;
        }
        distance *= Clamp(Math.Exp(-zoom * 0.0016), 0.85, 1.15);
        roll += Clamp(rollDelta * 0.00018, -0.03, 0.03);

        Dictionary<string, object> args = new Dictionary<string, object>();
        args["target"] = new object[] { tx, ty, tz };
        args["yaw"] = yaw;
        args["pitch"] = pitch;
        // Head motion must not overwrite native wheel smoothing (or mouse zoom)
        // with a cached distance. Only explicit legacy zoom buttons set it.
        if(zoom!=0)args["distance"] = distance;
        args["roll"] = roll;
        cameraGeneration++;
        if(zoom!=0)distanceEpoch++;
        SendWithoutWaiting("ui.camera", args);
        Camera["target"] = args["target"];
        Camera["yaw"] = yaw;
        Camera["pitch"] = pitch;
        Camera["distance"] = distance;
        Camera["roll"] = roll;
        }
    }

    internal void Drag(bool orbit, double dx, double dy)
    {
        // One bad HID sample must never throw the view across the model.
        dx = Clamp(dx, -20, 20);
        dy = Clamp(dy, -20, 20);
        double minX = ViewportLeft + 40, maxX = ViewportLeft + ViewportWidth - 40;
        double minY = ViewportTop + 40, maxY = ViewportTop + ViewportHeight - 40;
        double nextX = pointerX + dx, nextY = pointerY + dy;
        if (nextX < minX || nextX > maxX || nextY < minY || nextY > maxY)
        {
            pointerX = ViewportLeft + ViewportWidth * 0.5;
            pointerY = ViewportTop + ViewportHeight * 0.5;
            Dictionary<string, object> reset = new Dictionary<string, object>();
            reset["x"] = pointerX;
            reset["y"] = pointerY;
            Request("ui.move", reset, null);
            nextX = pointerX + dx;
            nextY = pointerY + dy;
        }
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["x"] = nextX;
        args["y"] = nextY;
        if (orbit) args["alt"] = true;
        else args["shift"] = true;
        Request("ui.move", args, null);
        pointerX = nextX;
        pointerY = nextY;
        pointerNavigating = true;
    }

    internal void ReleasePointer()
    {
        if (!pointerNavigating) return;
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["x"] = pointerX;
        args["y"] = pointerY;
        Request("ui.move", args, null);
        pointerNavigating = false;
    }

    internal void Scroll(double delta)
    {
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["x"] = ViewportLeft + ViewportWidth * 0.5;
        args["y"] = ViewportTop + ViewportHeight * 0.5;
        args["delta"] = Clamp(delta, -25, 25);
        Request("ui.scroll", args, null);
    }

    internal void Navigate(string motion, double first, double second)
    {
        Dictionary<string, object> navigation = new Dictionary<string, object>();
        navigation["camera"] = Camera;
        navigation["motion"] = motion;
        if (motion == "zoom")
        {
            navigation["factor"] = first;
        }
        else
        {
            navigation["delta"] = new object[] { first, second };
            if (motion == "pan")
                navigation["viewport"] = new object[] { ViewportWidth, ViewportHeight };
        }

        Dictionary<string, object> args = new Dictionary<string, object>();
        args["command"] = "view.navigate";
        args["params"] = navigation;
        Dictionary<string, object> result = Request("engine.execute", args, null);
        if (result.ContainsKey("camera")) Camera = Dict(result["camera"]);
    }

    internal void Roll(double delta)
    {
        double roll = Camera.ContainsKey("roll") ? Number(Camera["roll"]) : 0;
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["roll"] = roll + delta;
        Dictionary<string, object> result = Request("ui.camera", args, null);
        Camera = result.ContainsKey("camera") ? Dict(result["camera"]) : result;
    }

    private Dictionary<string, object> Request(string method, Dictionary<string, object> parameters, string token)
    {
        Stopwatch requestTimer = Stopwatch.StartNew();
        Dictionary<string, object> request = new Dictionary<string, object>();
        request["id"] = nextId++;
        request["method"] = method;
        request["params"] = parameters;
        request["animate"] = false;
        if (token != null) request["token"] = token;
        writer.WriteLine(json.Serialize(request));

        string line = reader.ReadLine();
        requestTimer.Stop();
        if (requestTimer.ElapsedMilliseconds >= 35)
            Console.Error.WriteLine("SLOW SolidCraft " + method + ": " + requestTimer.ElapsedMilliseconds + " ms");
        if (line == null) throw new IOException("SolidCraft closed the control connection.");
        Dictionary<string, object> reply = Dict(json.DeserializeObject(line));
        if (!reply.ContainsKey("ok") || !(bool)reply["ok"])
            throw new InvalidOperationException(reply.ContainsKey("error") ? Convert.ToString(reply["error"]) : line);
        return Dict(reply["result"]);
    }

    internal static Dictionary<string, object> Dict(object value)
    {
        Dictionary<string, object> direct = value as Dictionary<string, object>;
        if (direct != null) return direct;
        IDictionary dictionary = value as IDictionary;
        Dictionary<string, object> result = new Dictionary<string, object>();
        if (dictionary != null)
            foreach (DictionaryEntry item in dictionary) result[Convert.ToString(item.Key)] = item.Value;
        return result;
    }

    private static object[] Array(object value)
    {
        object[] direct = value as object[];
        if (direct != null) return direct;
        ArrayList list = value as ArrayList;
        return list == null ? new object[0] : list.ToArray();
    }

    private static double Number(object value)
    {
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    public void Dispose()
    {
        readingReplies = false;
        client.Close();
        if (replyThread != null && replyThread.IsAlive) replyThread.Join(250);
        writer.Dispose();
        reader.Dispose();
    }
}



// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß
// Private stdio companion; no listener, clipboard or focus changes.
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Automation;

internal static class FusionNumbers
{
    internal sealed class Snapshot
    {
        public string mode="blocked",id="",label="Feld nicht lesbar",value="",automation_id="";
        public bool verified=false;
        public double? step=null;
        public string verify_reason="";
    }
    private static readonly Regex Literal=new Regex(@"^\s*([+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+-]?\d+)?)\s*(mm|cm|m|in|ft|um|µm|deg|°|rad|%)?\s*$",RegexOptions.CultureInvariant);
    private static readonly Regex NumericId=new Regex(@"QTLineEdit_[^._]*(Distance|Angle|Radius|Diameter|Offset|Length|Width|Height|Depth|Thickness|Taper|Scale|Count|Number|Quantity|Pitch|Extent|Tolerance|Gap|Size)[^._]*_",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex HudId=new Regex(@"^QTApplication\.HUD:QTView:\([0-9]+\)[^.\r\n]+\.QTLineEdit_",RegexOptions.CultureInvariant);
    // Observed sketch-dimension editor. Do not allow arbitrary *_Dim_* fields.
    private static readonly Regex SketchId=new Regex(@"^QTApplication\.HUD:QTView:\([0-9]+\)InferenceDimension[0-9]+\.QTLineEdit_Dim_[^.\r\n]+$",RegexOptions.CultureInvariant);
    private static int targetPid;
    private static double configuredBodyStep=1,configuredSketchStep=.1,configuredSketchAngleStep=.1;

    internal static bool SupportedEditor(string className,string auto)
    {
        if(className!="ToolbarFramework::QTLineEdit"||auto==null)return false;
        if(SketchId.IsMatch(auto))return true;
        if(!NumericId.IsMatch(auto))return false;
        return (auto.Contains("QTCommandDialogContentPanel")&&auto.Contains("CmdDg:"))||HudId.IsMatch(auto);
    }

    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    { public ushort vk,scan;public uint flags,time;public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int x,y;public uint data,flags,time;public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct HardwareInput
    { public uint message;public ushort low,high; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputData
    {
        [FieldOffset(0)]public KeyboardInput keyboard;
        [FieldOffset(0)]public MouseInput mouse;
        [FieldOffset(0)]public HardwareInput hardware;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint type;public InputData data; }
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,[In]Input[] input,int size);

    private static Input Key(ushort vk,ushort scan,uint flags)
    {return new Input{type=1,data=new InputData{keyboard=new KeyboardInput{vk=vk,scan=scan,flags=flags}}};}
    internal static Input[] TextInputs(string text)
    {
        if(text==null||text.Length>200||text.Any(char.IsControl))throw new ArgumentException("Invalid input text");
        // One contiguous Windows input batch: select this field's text, then
        // real Unicode key events. Never Enter/Tab, never clipboard/pointer.
        var list=new System.Collections.Generic.List<Input>();
        list.Add(Key(0x11,0,0));list.Add(Key(0x41,0,0));
        list.Add(Key(0x41,0,2));list.Add(Key(0x11,0,2));
        foreach(char c in text){list.Add(Key(0,c,4));list.Add(Key(0,c,6));}
        return list.ToArray();
    }
    private static bool CanType()
    {
        uint pid;IntPtr window=GetForegroundWindow();GetWindowThreadProcessId(window,out pid);
        return window!=IntPtr.Zero&&pid==(uint)targetPid&&
            !new[]{0x10,0x11,0x12,0x5B,0x5C,0x41}.Any(key=>(GetAsyncKeyState(key)&0x8000)!=0);
    }
    private static bool TypeText(string text)
    {
        var inputs=TextInputs(text);
        if(!CanType())return false;
        uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)));
        if(sent==(uint)inputs.Length)return true;
        // Release only keys pressed by the inserted prefix, never retry text.
        var releases=new System.Collections.Generic.List<Input>();
        if(sent>=2&&sent<3)releases.Add(Key(0x41,0,2));
        if(sent>=1&&sent<4)releases.Add(Key(0x11,0,2));
        if(sent>4&&(inputs[sent-1].data.keyboard.flags&2)==0)
        {var last=inputs[sent-1].data.keyboard;releases.Add(Key(last.vk,last.scan,last.flags|2));}
        if(releases.Count>0)SendInput((uint)releases.Count,releases.ToArray(),Marshal.SizeOf(typeof(Input)));
        throw new InvalidOperationException("Partial text input");
    }

    internal static bool Parse(string text,out double number,out string unit,out bool comma)
    {
        number=0;unit="";comma=false;
        if(text==null||text.Length>200)return false;
        var match=Literal.Match(text);if(!match.Success)return false;
        comma=match.Groups[1].Value.Contains(",");unit=match.Groups[2].Value;
        return double.TryParse(match.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out number)
            && !double.IsInfinity(number)&&!double.IsNaN(number)&&Math.Abs(number)<=1e9;
    }
    internal static string Adjust(string text,double step,int delta)
    { if(step>1000)throw new ArgumentException("Invalid profile step");return AdjustInternal(text,step,delta,.001); }

    internal static double FieldStep(string auto,string text,double bodyStep,double sketchStep,double angleStep)
    {
        if(!SketchId.IsMatch(auto??""))return bodyStep;
        double value;string unit;bool comma;
        if(!Parse(text,out value,out unit,out comma))throw new ArgumentException("Invalid sketch literal");
        // Configured physical sketch precision, independent of body step.
        // Never assume a unit for an empty/unitless field.
        double perMm;
        switch(unit)
        {
            case "mm":perMm=1;break;
            case "cm":perMm=.1;break;
            case "m":perMm=.001;break;
            case "in":perMm=1/25.4;break;
            case "ft":perMm=1/304.8;break;
            case "um":case "µm":perMm=1000;break;
            case "deg":case "°":return angleStep;
            case "rad":return angleStep*Math.PI/180;
            default:throw new ArgumentException("Unknown sketch unit");
        }
        return sketchStep*perMm;
    }
    internal static string AdjustField(string auto,string text,double step,int delta,double sketchStep=.01,double angleStep=.1)
    {
        return AdjustInternal(text,FieldStep(auto,text,step,sketchStep,angleStep),delta,1e-9);
    }

    private static string AdjustInternal(string text,double step,int delta,double minimumStep)
    {
        double value;string unit;bool comma;
        if(!Parse(text,out value,out unit,out comma)||double.IsNaN(step)||double.IsInfinity(step)||step<minimumStep||step>1000000||Math.Abs(delta)!=1)
            throw new ArgumentException("Invalid numeric input");
        double next=Math.Round(value+delta*step,9,MidpointRounding.AwayFromZero);
        if(Math.Abs(next)>1e9)throw new ArgumentException("Numeric limit");
        string result=next.ToString("0.#########",CultureInfo.InvariantCulture);
        if(comma)result=result.Replace('.',',');
        return result+(unit.Length==0?"":" "+unit);
    }

    private static Snapshot Read(out AutomationElement element)
    {
        element=null;
        try
        {
            var focused=AutomationElement.FocusedElement;
            if(focused==null)return new Snapshot();
            var info=focused.Current;
            if(info.ProcessId!=targetPid)return new Snapshot{label="Fusion nicht im Fokus"};
            if(info.ControlType!=ControlType.Edit)
                return new Snapshot{mode="navigation",label="3D-Navigation"};
            if(!info.HasKeyboardFocus||!info.IsEnabled||info.IsOffscreen||info.IsPassword)return new Snapshot();
            string auto=info.AutomationId??"";
            // Observed native dialog and canvas HUD families only, never
            // search, filenames, parameter tables or arbitrary text edits.
            if(!SupportedEditor(info.ClassName,auto))
                return new Snapshot{label="Kein freigegebenes Zahlenfeld"};
            object pattern;if(!focused.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))return new Snapshot();
            var current=((ValuePattern)pattern).Current;if(current.IsReadOnly)return new Snapshot();
            double value;string unit;bool comma;
            if(!Parse(current.Value,out value,out unit,out comma))return new Snapshot{label="Formel oder ungueltiger Wert"};
            string label=info.Name;
            if(string.IsNullOrWhiteSpace(label)&&info.LabeledBy!=null)label=info.LabeledBy.Current.Name;
            if(string.IsNullOrWhiteSpace(label))label=auto.Substring(auto.LastIndexOf('_')+1);
            if(label.Length>80)label=label.Substring(0,80);
            element=focused;
            return new Snapshot{mode="numeric",id=targetPid+":"+string.Join(".",focused.GetRuntimeId().Select(x=>x.ToString()).ToArray()),
                automation_id=auto,label=label,value=current.Value,step=FieldStep(auto,current.Value,configuredBodyStep,configuredSketchStep,configuredSketchAngleStep)};
        }
        catch{return new Snapshot();}
    }

    private static Snapshot Route(System.Collections.Generic.Dictionary<string,object> request)
    {
        AutomationElement element;Snapshot before=Read(out element);
        string expectedMode=Convert.ToString(request["mode"]);
        if(before.mode!=expectedMode)return new Snapshot{label="Fokuswechsel - Ring verworfen"};
        if(before.mode=="navigation")return before;
        if(before.mode!="numeric"||before.id!=Convert.ToString(request["id"])||
           before.automation_id!=Convert.ToString(request["automation_id"])||before.value!=Convert.ToString(request["value"]))
            return new Snapshot{label="Feldwechsel - Ring verworfen"};
        string next=AdjustField(before.automation_id,before.value,configuredBodyStep,Convert.ToInt32(request["delta"]),configuredSketchStep,configuredSketchAngleStep);
        // Fresh focus/value verification immediately before writing, against
        // the EXACT UIA element. Do not refocus an editor that lost focus.
        AutomationElement check;Snapshot again=Read(out check);
        if(again.mode!="numeric"||again.id!=before.id||again.value!=before.value||again.automation_id!=before.automation_id)
            return new Snapshot{label="Fokuswechsel - Ring verworfen"};
        if(!TypeText(next))return new Snapshot{label="Tastatur oder Fokus belegt"};
        Snapshot lastAfter=null;
        for(int attempt=0;attempt<10;attempt++)
        {
            AutomationElement afterElement;Snapshot after=Read(out afterElement);
            lastAfter=after;
            double wanted,actual;string wu,au;bool wc,ac;
            after.verified=after.mode=="numeric"&&after.automation_id==before.automation_id&&Parse(next,out wanted,out wu,out wc)
                &&Parse(after.value,out actual,out au,out ac)&&wu==au&&Math.Abs(wanted-actual)<1e-8;
            if(after.verified)return after;
            // Fusion may rebuild the same editor while updating its preview.
            // A new RuntimeId after the write is not itself a failed write:
            // verify the exact semantic field and value, without writing again.
            if(after.mode!="numeric"||after.automation_id!=before.automation_id)
                return new Snapshot{label="Fokuswechsel nach Schreiben"};
            if(after.value!=before.value)break;
            System.Threading.Thread.Sleep(15);
        }
        return new Snapshot{label="Schreiben nicht bestaetigt",id=before.id,automation_id=before.automation_id,
            verify_reason=lastAfter!=null&&lastAfter.value==before.value?"value_unchanged":"value_mismatch"};
    }

    [MTAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=2||args[0]!="--pid"||!int.TryParse(args[1],out targetPid))return 2;
        try{using(var p=Process.GetProcessById(targetPid))if(!p.ProcessName.Equals("Fusion360",StringComparison.OrdinalIgnoreCase))return 3;}catch{return 3;}
        Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
        var json=new JavaScriptSerializer();string line;
        while((line=Console.ReadLine())!=null)
        {
            Snapshot result;
            try
            {
                if(line.Length>4096)throw new ArgumentException("Frame too large");
                var request=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(line);
                configuredBodyStep=ConfigStep(request,"body_step",1);
                configuredSketchStep=ConfigStep(request,"sketch_step_mm",.1);
                configuredSketchAngleStep=ConfigStep(request,"sketch_angle_step_deg",.1);
                string type=Convert.ToString(request["type"]);
                if(type=="snapshot"){AutomationElement unused;result=Read(out unused);}
                else if(type=="route")result=Route(request);
                else result=new Snapshot();
            }
            catch{result=new Snapshot{label="Zahlenring gesperrt"};}
            Console.WriteLine(json.Serialize(result));Console.Out.Flush();
        }
        return 0;
    }
    private static double ConfigStep(System.Collections.Generic.Dictionary<string,object> request,string name,double fallback)
    {
        object raw;if(!request.TryGetValue(name,out raw))return fallback;
        if(raw is bool||raw is string||raw==null)throw new ArgumentException("Invalid step type");
        double value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);
        if(double.IsNaN(value)||double.IsInfinity(value)||value<.001||value>1000)throw new ArgumentException("Invalid step range");
        return value;
    }
}

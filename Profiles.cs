// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

internal sealed class CadProfile
{
    public string Id = "SolidCraft";
    public string Executable = "";
    public string AdapterExecutable = "";
    public bool Enabled = true;
    public int Translation = 7, Rotation = 7, Deadzone = 3, Smoothing = 45;
    public bool[] AxisEnabled = {true,true,true,true,true,true};
    public bool[] AxisInverted = {false,false,false,false,false,false};
    public string[] Bindings = {"undo","redo","iso","none","none","sensitivity","none","none","none","escape","front","right","top","fit","none","none","none"};
    public string RingLeft = "zoom_out", RingRight = "zoom_in";
    public bool NumberRingEnabled=false;
    public double NumberRingStep=1;
    public double SketchRingStep=.1, SketchAngleRingStep=.1;
    public string[] DisplayLines={"{cad}","Ring: {ring}","{context}","{value}","Schritt: {step}","T1 {key1}","T6 {key6}"};
    public CadProfile Copy() { var j=new JavaScriptSerializer(); return j.Deserialize<CadProfile>(j.Serialize(this)); }
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Id) || !Regex.IsMatch(Id,@"^[\p{L}\p{N}][\p{L}\p{N}_. -]{0,63}$")) throw new InvalidDataException("Bitte einen CAD-/Plugin-Namen mit maximal 64 Zeichen eingeben.");
        if(AxisEnabled==null || AxisEnabled.Length!=6 || AxisInverted==null || AxisInverted.Length!=6 || Bindings==null || Bindings.Length!=17) throw new InvalidDataException("Profil enthält unvollständige Achsen oder Tasten.");
        if(Bindings.Any(x=>!Actions.IsValid(x)) || !Actions.IsValid(RingLeft) || !Actions.IsValid(RingRight)) throw new InvalidDataException("Profil enthält eine unbekannte Aktion.");
        if(double.IsNaN(NumberRingStep)||double.IsInfinity(NumberRingStep)||NumberRingStep<.001||NumberRingStep>1000)throw new InvalidDataException("Die Ring-Schrittweite muss zwischen 0,001 und 1000 liegen.");
        foreach(double sketchStep in new[]{SketchRingStep,SketchAngleRingStep})
            if(double.IsNaN(sketchStep)||double.IsInfinity(sketchStep)||sketchStep<.001||sketchStep>1000)throw new InvalidDataException("Die Skizzen-Schrittweiten müssen zwischen 0,001 und 1000 liegen.");
        if(DisplayLines==null||DisplayLines.Length!=7||DisplayLines.Any(x=>x==null||x.Length>100||x.Any(char.IsControl)))throw new InvalidDataException("Das Display benötigt sieben einzeilige Vorlagen mit maximal 100 Zeichen.");
        Translation=Math.Max(0,Math.Min(15,Translation)); Rotation=Math.Max(0,Math.Min(15,Rotation));
        Deadzone=Math.Max(0,Math.Min(15,Deadzone)); Smoothing=Math.Max(5,Math.Min(100,Smoothing));
    }
}
internal sealed class HubSettings
{
    public int Version=2, Port=17861;
    public string ActiveProfile="SolidCraft";
    public List<CadProfile> Profiles=new List<CadProfile>();
    public bool StartMinimized = false;
    private static readonly object Gate=new object();
    public static string DirectoryName { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SpaceControllerCadHub"); } }
    public static string PathName { get { return Path.Combine(DirectoryName,"profiles.json"); } }
    public CadProfile Active { get { return Profiles.First(p=>p.Id==ActiveProfile); } }
    public static HubSettings Load()
    {
        HubSettings s=new HubSettings();
        if(File.Exists(PathName))
        {
            // Never silently overwrite a damaged user configuration.
            s=new JavaScriptSerializer().Deserialize<HubSettings>(File.ReadAllText(PathName,Encoding.UTF8));
            if(s==null || s.Profiles==null) throw new InvalidDataException("Profile konnten nicht geladen werden.");
        }
        else
        {
            string legacy=Path.Combine(DirectoryName,"settings.json");
            var p=new CadProfile();
            s.Profiles.Add(p);
            var f=new CadProfile {Id="FreeCAD",Enabled=false};
            if(File.Exists(legacy))
            {
                var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(legacy));
                if(d.ContainsKey("FreeCadPath")) f.Executable=Convert.ToString(d["FreeCadPath"]);
                if(d.ContainsKey("TranslationSensitivity")) p.Translation=f.Translation=Convert.ToInt32(d["TranslationSensitivity"]);
                if(d.ContainsKey("RotationSensitivity")) p.Rotation=f.Rotation=Convert.ToInt32(d["RotationSensitivity"]);
                if(d.ContainsKey("Deadzone")) p.Deadzone=f.Deadzone=Convert.ToInt32(d["Deadzone"]);
                if(d.ContainsKey("Smoothing")) p.Smoothing=f.Smoothing=Convert.ToInt32(d["Smoothing"]);
            }
            s.Profiles.Add(f); s.Profiles.Add(new CadProfile {Id="Fusion",Enabled=false});
        }
        foreach(string id in new[]{"SolidCraft","FreeCAD","Fusion"}) if(!s.Profiles.Any(p=>p.Id==id)) s.Profiles.Add(new CadProfile{Id=id,Enabled=false});
        foreach(var p in s.Profiles) p.Validate();
        if(s.Profiles.Select(p=>p.Id).Distinct().Count()!=s.Profiles.Count) throw new InvalidDataException("Doppelte CAD-Profile.");
        if(!s.Profiles.Any(p=>p.Id==s.ActiveProfile)) s.ActiveProfile="SolidCraft";
        s.Port=17861; return s;
    }
    public void Save()
    {
        lock(Gate)
        {
            Directory.CreateDirectory(DirectoryName);
            string temp=PathName+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
            if(File.Exists(PathName)) File.Replace(temp,PathName,PathName+".bak"); else File.Move(temp,PathName);
        }
    }
}
internal sealed class ActionChoice
{
    public string Id, Label;
    public ActionChoice(string id,string label) {Id=id;Label=label;}
    public override string ToString(){return Label;}
}
internal static class Actions
{
    public static readonly ActionChoice[] All={
        new ActionChoice("none","Nicht belegt"),new ActionChoice("undo","Bearbeiten · Rückgängig"),new ActionChoice("redo","Bearbeiten · Wiederholen"),
        new ActionChoice("escape","Bearbeiten · Abbrechen"),new ActionChoice("fit","Ansicht · Alles einpassen"),new ActionChoice("iso","Ansicht · Isometrisch"),
        new ActionChoice("front","Ansicht · Vorne"),new ActionChoice("back","Ansicht · Hinten"),new ActionChoice("right","Ansicht · Rechts"),
        new ActionChoice("left","Ansicht · Links"),new ActionChoice("top","Ansicht · Oben"),new ActionChoice("bottom","Ansicht · Unten"),
        new ActionChoice("zoom_in","Ansicht · Hineinzoomen"),new ActionChoice("zoom_out","Ansicht · Herauszoomen"),
        new ActionChoice("sensitivity","Hub · Ring-Empfindlichkeit umschalten")};
    public static bool IsValid(string id){return All.Any(a=>a.Id==id) || (id!=null && id.StartsWith("key:") && id.Length>4 && id.Length<100);}
    public static string Label(string id){var a=All.FirstOrDefault(x=>x.Id==id);return a==null ? (id??"Nicht belegt").Replace("key:","Tastenkürzel · ") : a.Label;}
    public static readonly string[] Keys={"1","2","3","4","5","6","Ctrl","Alt","Shift","Esc","Front","Right","Top","Fit","2D/3D","Panel","Menu"};
}

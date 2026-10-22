// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal static class HubDisplay
{
    internal static string LcdText(string text)
    {
        text=(text??"").Replace("ä","ae").Replace("ö","oe").Replace("ü","ue").Replace("Ä","Ae").Replace("Ö","Oe").Replace("Ü","Ue").Replace("ß","ss").Replace("−","-").Replace("·","-").Replace("…","...");
        var output=new StringBuilder();foreach(char c in text)if(!char.IsControl(c))output.Append(c>=32&&c<=126?c:'?');
        return output.Length>24?output.ToString(0,21)+"...":output.ToString();
    }
    internal static string[] Build(CadProfile profile,AdapterContext context,bool sensitivity,CalibrationInfo calibration)
    {
        bool zoomBindings=(profile.RingLeft=="zoom_in"||profile.RingLeft=="zoom_out")&&(profile.RingRight=="zoom_in"||profile.RingRight=="zoom_out");
        string navigation=zoomBindings?"Zoom":"Belegung";
        string mode=sensitivity?"Empfindlichkeit":context==null?(profile.Id=="SolidCraft"&&profile.NumberRingEnabled?"Wartet":navigation):context.Mode=="numeric"?"Zahlenwert":context.Mode=="blocked"?"Gesperrt":navigation;
        double effectiveStep=context!=null&&context.Mode=="numeric"&&context.Step.HasValue?context.Step.Value:profile.NumberRingStep;
        var values=new Dictionary<string,string>{{"cad",profile.Id},{"ring",mode},{"context",context==null?"Warte auf CAD-Kontext":context.Label},{"value",context==null?"":context.Value},{"step",effectiveStep.ToString("0.#########",CultureInfo.InvariantCulture)}};
        for(int i=0;i<17;i++)
        {
            string label=Actions.Label(profile.Bindings[i]);int separator=label.IndexOf('·');
            values["key"+(i+1)]=profile.Bindings[i]=="sensitivity"?"Empfindlichkeit":separator<0?label:label.Substring(separator+1).Trim();
        }
        var result=new string[7];for(int row=0;row<7;row++){string line=profile.DisplayLines[row];foreach(var entry in values)line=line.Replace("{"+entry.Key+"}",entry.Value);result[row]=LcdText(line);}
        if(sensitivity){result[2]="Translation "+profile.Translation;result[3]="Rotation "+profile.Rotation;}
        if(calibration!=null&&calibration.Busy){result[0]="Nullstellung";result[1]="Kappe loslassen";result[2]=calibration.CanCancel?"Countdown: "+System.Text.RegularExpressions.Regex.Match(calibration.Text,@"\b\d+ s\b").Value:"Nullpunkt uebernehmen";result[3]="CAD-Eingabe pausiert";}
        return result;
    }
}

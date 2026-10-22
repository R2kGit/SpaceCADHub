// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

// Read-only accessibility bridge: no clipboard, global hooks or focus changes.
internal sealed class FocusedInput
{
    internal readonly string Identity,Label,Literal;
    internal FocusedInput(string identity,string label,string literal)
    {Identity=identity;Label=label??"";Literal=literal??"";}
    internal static FocusedInput ReadSolidCraft()
    {
        try
        {
            var element=AutomationElement.FocusedElement;if(element==null)return Unavailable();
            var info=element.Current;
            using(var process=Process.GetProcessById(info.ProcessId))
                if(!process.ProcessName.Equals("SolidCraft",StringComparison.OrdinalIgnoreCase))return null;
            if(info.ControlType!=ControlType.Edit)return null;
            if(!info.IsEnabled||info.IsOffscreen||info.IsPassword||!info.HasKeyboardFocus)return Unavailable();
            object pattern;if(!element.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))return Unavailable();
            var value=((ValuePattern)pattern).Current;if(value.IsReadOnly)return Unavailable();
            string label=info.Name;
            if(string.IsNullOrWhiteSpace(label)&&info.LabeledBy!=null)label=info.LabeledBy.Current.Name;
            string id=info.ProcessId+":"+string.Join(".",element.GetRuntimeId().Select(n=>n.ToString()).ToArray());
            return new FocusedInput(id,label,value.Value);
        }
        catch{return Unavailable();}
    }
    // Distinguish an unreadable editor/provider from no editor: never fall
    // through to camera zoom merely because reading a text field failed.
    private static FocusedInput Unavailable(){return new FocusedInput("unavailable","Feld nicht lesbar","");}
}

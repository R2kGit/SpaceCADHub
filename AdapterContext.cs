// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal sealed class AdapterContext
{
    internal readonly string Mode,Label,Value,Detail;
    internal readonly double? Step;
    internal AdapterContext(string mode,string label="",string value="",string detail="",double? step=null){Mode=mode;Label=Clean(label);Value=Clean(value);Detail=Clean(detail);Step=step;}
    internal static string Clean(string text)
    {
        if(text==null)return "";var result=new StringBuilder();
        foreach(char c in text){if(!char.IsControl(c))result.Append(c);if(result.Length>=80)break;}return result.ToString();
    }
    internal static AdapterContext Parse(Dictionary<string,object> message)
    {
        string mode=Get(message,"mode");if(mode!="navigation"&&mode!="numeric"&&mode!="blocked")throw new FormatException("Unbekannter Adapterkontext.");
        object raw;double? step=null;
        if(message.TryGetValue("step",out raw)&&raw!=null)
        {
            if(!(raw is double||raw is int||raw is decimal||raw is long))throw new FormatException("Ungültige Kontext-Schrittweite.");
            double n=Convert.ToDouble(raw,CultureInfo.InvariantCulture);
            if(double.IsNaN(n)||double.IsInfinity(n)||n<=0||n>1000000)throw new FormatException("Ungültige Kontext-Schrittweite.");
            step=n;
        }
        return new AdapterContext(mode,Get(message,"label"),Get(message,"value"),Get(message,"detail"),step);
    }
    private static string Get(Dictionary<string,object> data,string name){object value;return data.TryGetValue(name,out value)?Convert.ToString(value,CultureInfo.InvariantCulture):"";}
    internal object Message(){return new{type="context",version=1,mode=Mode,label=Label,value=Value,detail=Detail,step=Step};}
}

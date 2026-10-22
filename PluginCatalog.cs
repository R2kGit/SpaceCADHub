// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß
using System;
using System.IO;

internal static class PluginCatalog
{
    internal const string Version="1.0";
    internal static bool Builtin(string id){return id=="SolidCraft"||id=="FreeCAD"||id=="Fusion";}
    internal static string Name(CadProfile profile)
    {
        if(!string.IsNullOrEmpty(profile.AdapterExecutable))return Path.GetFileName(profile.AdapterExecutable);
        switch(profile.Id)
        {
            case "SolidCraft":return "SolidCraft-Adapter "+Version;
            case "FreeCAD":return "FreeCAD-Add-on "+Version;
            case "Fusion":return "Fusion-Add-in "+Version;
            default:return "Nicht eingerichtet";
        }
    }
    internal static string Status(CadProfile profile,bool active,bool connected,bool legacyBridge)
    {
        if(!profile.Enabled)return "Deaktiviert";
        if(!string.IsNullOrEmpty(profile.AdapterExecutable)&&!File.Exists(profile.AdapterExecutable))return "Adapterdatei fehlt";
        if(!Builtin(profile.Id)&&string.IsNullOrEmpty(profile.AdapterExecutable))return "Kein Adapter eingerichtet";
        if(active&&profile.Id=="SolidCraft"&&legacyBridge)return "Alte SolidCraft-Bridge aktiv · bitte schließen";
        if(connected)return active?"Verbunden · bereit für Eingaben":"Verbunden · Profil derzeit nicht ausgewählt";
        if(!active)return "Bereit · Profil derzeit nicht ausgewählt";
        return profile.Id=="Fusion"?"Wartet auf Fusion-Add-in":"Wartet auf CAD-Verbindung";
    }
}

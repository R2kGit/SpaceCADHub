// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
internal sealed partial class MainForm
{
    internal static string MotionHelp(string title)
    {
        switch(title)
        {
            case "Translation":return "Empfindlichkeit beim Verschieben mit der Kappe.\nHöher: dieselbe Auslenkung verschiebt stärker.\nNiedriger: feinere Verschiebung, mehr Auslenkung nötig.\nBetrifft nicht die Schrittweite des Zahlenrings.";
            case "Rotation":return "Empfindlichkeit beim Drehen, Kippen und Rollen mit der Kappe.\nHöher: dieselbe Auslenkung dreht stärker.\nNiedriger: feinere Drehung, mehr Auslenkung nötig.\nBetrifft nicht den Drehring-Zoom.";
            case "Totzone":return "Bereich um die Ruhestellung, in dem kleine Ausschläge ignoriert werden.\nGrößer: weniger unbeabsichtigte Bewegung, späterer Bewegungsbeginn.\nKleiner: reagiert auf feinere Auslenkung.\nDas ersetzt keine Nullstellung/Kalibrierung.";
            case "Direktheit":return "Wie direkt die Kappenbewegung durch den Hub weitergegeben wird.\nHöher: weniger Glättung, schnellere Reaktion auf Änderungen.\nNiedriger: weichere Bewegung, kann verzögert wirken oder nachlaufen.\nDer Zahlenwert ist kein Zeitwert in Millisekunden.";
            default:return "";
        }
    }
}

# SpaceCADHub Fusion Add-in 1.0.3

Eigenständiges Windows-Add-in für Autodesk Fusion. Benötigt den laufenden
SpaceCADHub und das aktive Fusion-Profil. Keine separate Adapter-EXE im Hub eintragen.

## Installation

Das vollständige Add-in liegt im Windows-Paket unter SpaceCADHubFusion/.
Bei geschlossenem Fusion-Befehl Install-FusionAddin.ps1 starten. Es überschreibt
keine vorhandene Installation. Danach in Fusion unter Skripte und Zusatzmodule
starten und automatischen Start aktivieren.

Installationsziel:
`%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\SpaceCADHubFusion`

## Funktionen

Navigation, Tasten, Ringzoom, fokussierte native numerische Dialog-/HUD-Felder
und die bestätigte Skizzen-Bemaßungsfeldfamilie InferenceDimension/Dim.
Hub 1.0.10 oder neuer liefert getrennte Rastweiten: allgemeiner Schritt in
Feldeinheiten, Skizzenlänge in mm, Skizzenwinkel in Grad. Skizzen-Defaults 0,1/0,1.
Das LCD erhält den tatsächlich verwendeten Schritt.

## Schutzmaßnahmen

Nur freigegebene Edit-Familien, explizite numerische Literale und überprüfter Fokus.
Keine Formeln, Suche, Dateinamen oder beliebigen Texte. UI-Automation wird nur
gelesen. Schreiben erfolgt nach erneuter Feld-/Wertprüfung durch einen einzigen
nativen Unicode-Eingabeblock, ohne Enter, Tab, Klick oder Clipboard.
Physisch gehaltene Modifikatoren sperren die Eingabe. Während einer Rastung nicht
Anwendung/Feld wechseln oder gleichzeitig tippen. Windows-Fokus und Eingabe sind
nicht atomar; unbestätigte Änderungen werden nicht wiederholt.
Zustandsabfragen laufen im privaten Helferprozess, Fusion-API-Aufrufe im UI-Thread.

## Quellcode

`build.ps1` baut FusionNumbers.exe. Der Quellcode ist FusionNumbers.cs.
Das Veröffentlichungspaket enthält keine Testdateien oder Testskripte.
Kein Selbsttest-Schalter und keine Fokusdiagnose sind in der Laufzeit-EXE enthalten.
Betriebs-/Fehlerlogs enthalten keine Zahlenfeldwerte.

GPL-3.0-only. LICENSE und NOTICE.md beachten.

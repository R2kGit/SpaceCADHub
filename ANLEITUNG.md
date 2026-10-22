<p align="center"><img src="Assets/hub-logo.png" width="160" alt="SpaceCADHub"></p>

# SpaceCADHub

Ein SpaceControl SpaceController Blackline. Mehrere CAD-Programme. Eine Oberfläche.

Version **1.0.11** · Entwickler **Benjamin Preuß** · **GPL-3.0-only**

[GitHub](https://github.com/R2kGit/SpaceCADHub) · [Probleme melden](https://github.com/R2kGit/SpaceCADHub/issues) · [Kontakt](mailto:R2kGit@Borgcollective.de)

## Was funktioniert?

- SolidCraft: 6-Achsen-Navigation, Hardwaretasten, Ringzoom und Zahlenring.
- Autodesk Fusion: eigenständiges Add-in, Navigation, Tasten, Ringzoom und
  Zahlenring für unterstützte native Dialog-/HUD-Felder und Skizzenbemaßungen.
- FreeCAD: Add-on für Navigation, Hardwaretasten und Ringzoom; kein Zahlenring.
- Pro CAD-Profil: Empfindlichkeit, Achsen, Tasten, Ringbelegung und LCD-Vorlagen.
- Live-Anzeige der Kappe, Gerätestatus und manuelle Nullstellung.
- Getrennte Rastweiten für allgemeine Felder, Skizzenlängen und Skizzenwinkel.

Die Anwendung ist für **SpaceControl**, nicht für 3Dconnexion. Es handelt sich
um ein unabhängiges Projekt, nicht um Hersteller-Software.

## Voraussetzungen

- Windows x64 mit .NET Framework 4.x.
- Installierte originale SpaceControl-Software samt 64-Bit-SDK-DLL und USB-Treiber.
  Getestet mit Version 2.9.3 und SpaceController Blackline.
- Das jeweilige CAD-Programm ist separat zu installieren.
- Der Hub erwartet SpaceControl im Windows-Standardordner
  `%ProgramFiles(x86)%\SpaceControl`. Andere Hersteller-Installationspfade
  sind derzeit nicht über die Oberfläche konfigurierbar.

Treiber, Hersteller-DLLs und CAD-Anwendungen werden **nicht mitgeliefert**.
Ein angeschlossener Controller allein reicht ohne SpaceControl-Installation nicht.

## Installation der fertigen Version

1. Das Windows-Paket vollständig in einen eigenen, beschreibbaren Ordner entpacken.
   Die Unterordner nicht auflösen und nicht nur die Hub-EXE kopieren.
2. `SpaceControllerCadHub.exe` starten.
3. Unter **Plugins & Profile** die gewünschte CAD-Programmdatei auswählen und
   das entsprechende Profil aktivieren.
4. Oben das passende Profil auswählen und **CAD starten** drücken.

Der Hub startet den SpaceControl-Hintergrunddienst bei Bedarf automatisch.
Für SolidCraft wird das Programm mit `--control 7878` gestartet und der
mitgelieferte Adapter verwendet. Bei bereits geöffnetem SolidCraft muss dessen
Kontrollschnittstelle aktiv sein; andernfalls SolidCraft beenden und über den Hub starten.
Eine alte separate SpaceController-SolidCraft-Bridge darf nicht parallel laufen.

**Wichtig:** Das aktive Hub-Profil muss zum gerade bedienten CAD passen.
**CAD starten** kann den Adapter auch nachträglich an ein bereits laufendes
SolidCraft anschließen. Fensterschließen versteckt den Hub im Tray.
Zum vollständigen Beenden das Tray-Menü **Beenden** verwenden.

### Paketinhalt

```text
SpaceControllerCadHub.exe
SolidCraftHubAdapter.exe
Assets/
FreeCADAddon/
FusionAdapter/
README.md
LICENSE
NOTICE.md
```

Der Quellcode wird separat als Source-Paket bereitgestellt. Die zum Windows-Paket
gehörende Quellversion ist 1.0.11. Für eigene Weitergabe Lizenz und korrespondierenden
Quellcode beachten; offene Punkte vor Veröffentlichung stehen in NOTICE.md.

### Fusion einrichten

Fusion vor der Einrichtung starten und offene Befehle beenden.
`FusionAdapter/Install-FusionAddin.ps1` installiert ein bisher nicht vorhandenes
Add-in. Alternativ den Ordner `FusionAdapter/SpaceCADHubFusion` samt LICENSE
und NOTICE.md nach folgendem Verzeichnis kopieren:

```text
%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\SpaceCADHubFusion
```

In Fusion unter **Dienstprogramme → Skripte und Zusatzmodule → Zusatzmodule**
SpaceCADHubFusion ausführen und den automatischen Start aktivieren.
Im Hub das Fusion-Profil aktivieren/auswählen; **Adapter-EXE leer lassen**.
Ein vorhandenes Add-in wird vom Installationsskript absichtlich nicht überschrieben.
Für Updates das Add-in zuerst stoppen und den bisherigen Stand sichern.
Das mitgelieferte Add-in hat Version **1.0.3**; die Plugin-Übersicht verwendet
weiterhin die Bezeichnung der stabilen Pluginfamilie **1.0**.

### FreeCAD einrichten

Unter **Plugins & Profile** das FreeCAD-Profil wählen und **Einrichten** verwenden.
Danach FreeCAD neu starten. Das Python-Add-on wird im Benutzerbereich installiert,
typischerweise unter `%APPDATA%\FreeCAD\Mod\SpaceControllerCADHub`
(ggf. zusätzlich im vorhandenen versionsbezogenen FreeCAD-Benutzerordner).

## Einstellungen und Sicherung

Alle Hub-Profile werden benutzerbezogen außerhalb des Programmordners gespeichert:

```text
%APPDATA%\SpaceControllerCadHub\profiles.json
%APPDATA%\SpaceControllerCadHub\profiles.json.bak
```

`profiles.json` enthält Programm-/Adapterpfade, aktive Profile, Tastenbelegungen,
Empfindlichkeiten, Achsen, LCD-Vorlagen und Rastweiten. Die .bak-Datei ist der
Stand vor der letzten Speicherung, kein unbegrenztes Archiv.

Für eine Sicherung den Hub vollständig über das Tray beenden und den Ordner kopieren.
Zum Wiederherstellen bei beendetem Hub die gesicherte profiles.json zurückkopieren.
Beim Umzug auf einen anderen PC anschließend CAD-/Adapterpfade kontrollieren.
Die Ablage ist **nicht vollständig portable**: Ein anderer Programmordner auf
demselben Windows-Benutzer verwendet dieselben Profile. Keine zwei Hubs parallel starten.

Im selben Benutzerordner können Betriebs-/Fehlerlogs liegen. Diese sind nicht
zum Betrieb erforderlich und gehören ebenso wenig wie persönliche Profile,
Zugriffstoken oder CAD-Dateien in das öffentliche Repository. Fusion- und
FreeCAD-Add-ins liegen separat an den oben genannten Installationsorten.
Die ursprünglichen SpaceControl-Einstellungen verwaltet der Hersteller-Treiber selbst.

## Bedienung

### Zahlenring

Unter **Display & Ring** den Zahlenring einschalten und **Übernehmen** drücken.
Das unterstützte Zahlenfeld muss den Fokus besitzen. Ohne Zahlenfeld bleibt
die Ringbelegung/Zoomfunktion erhalten. Formeln und nicht sicher lesbare Felder
werden nicht verändert. Bei einer gesperrten Eingabe gibt es keinen Zoom-Fallback.

Rastweiten pro CAD-Profil:

- **Körper / übrige Felder:** in der Einheit des Felds, bisheriger Standard 1.
- **Skizzenlängen:** in mm, Standard 0,1 mm pro Rastung.
- **Skizzenwinkel:** in Grad, Standard 0,1° pro Rastung.

Vorauswahl oder freie Eingabe (Punkt/Komma), jeweils 0,001 bis 1000.
Explizite Einheiten werden für Skizzen umgerechnet. In SolidCraft wird der
SKETCH-Kontext verwendet; einheitenlose Werte benötigen eine passende
Feldbezeichnung. Unklare Felder und Anzahl/Prozent behalten den allgemeinen Schritt.
Fusion erkennt freigegebene native Zahlenfeldfamilien, keine beliebigen Textfelder.
Die Zahlenfelder können für die Vorschau aufwendig sein; der Ring ist kein
stufenloser analoger Regler. Nicht gleichzeitig tippen oder während einer Rastung
den Fokus in ein anderes Programm wechseln.

### Tasten und LCD

Tasten können CAD-Aktionen oder Tastenkombinationen erhalten, etwa
`key:Enter`, `key:Escape`, `key:Ctrl+Z` oder `key:Ctrl+Shift+S`.
Eine allein gedrückte Modifikatortaste als dauerhaft gehaltene Taste ist keine
normale Tastenkombination; nicht unterstützte Kombinationen werden abgewiesen.

LCD-Platzhalter: `{cad}`, `{ring}`, `{context}`, `{value}`, `{step}`
und `{key1}` bis `{key17}`. `{step}` zeigt bei unterstütztem Adapter den
wirksamen Schritt in der Feldeinheit. Sieben Zeilen, maximal 24 Zeichen je Zeile;
lange Texte werden mit ... gekürzt. Änderungen mit **Übernehmen** speichern.

## Aus dem Quellcode bauen

Windows PowerShell und der 64-Bit-C#-Compiler des .NET Framework werden verwendet.
Ein zusätzliches NuGet-Paket ist nicht nötig. Der lokale SpaceControl-Treiber ist
zum Kompilieren nicht erforderlich, wohl aber zum Betrieb des Controllers.

```powershell
.\build.ps1
```

Ausgaben liegen unter `bin/`. Der Fusion-Zahlenhelfer wird mitgebaut.
`bin/` ist ein Build-Ordner, kein fertig bereinigtes Release-ZIP.

Mit `.\package.ps1` entstehen die getrennten Windows- und GitHub-Source-ZIPs
unter `dist/`. Ein vorhandenes gleichnamiges Paket wird nicht überschrieben.

## Sicherheit und Grenzen

Hub-Bus: `127.0.0.1:17861`, JSON-Zeilen, nur für vertrauenswürdige lokale Programme,
ohne Authentifizierung. SolidCraft verwendet seine lokale Kontrollschnittstelle
mit Zugriffstoken. Der Hub ist kein Netzwerkdienst zur Freigabe ins LAN.
Keine Garantie für alle CAD-Versionen, Dialoge oder Controller-Modelle.
Die Eingabe-/Fokus-/Alters-/Wertprüfungen bleiben auch ohne Entwicklungsdiagnose erhalten.

## Lizenz und Veröffentlichung

Eigener Projektcode: **GNU GPL Version 3 only**, siehe [LICENSE](LICENSE).
Drittanbieter, Controllerbild und Hersteller-DLL: siehe [NOTICE.md](NOTICE.md).

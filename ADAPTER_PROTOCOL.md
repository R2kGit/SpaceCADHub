# SpaceCADHub Adapterprotokoll

Lokaler TCP-Bus auf 127.0.0.1:17861, UTF-8 JSON Lines, Protokoll 1.
Nur vertrauenswürdige lokale Programme; keine Authentifizierung.

## Anmeldung

Adapter sendet beispielsweise:
`{"type":"hello","app":"Fusion","version":"1.0.3"}`

app entspricht exakt der Profil-ID. Der Hub antwortet ready mit protocol:1,
Gerätestatus, Translation/Rotation/Totzone/Glättung, number_ring und Rastweiten.
Aktualisierte Optionen kommen als config.

`{"type":"config","number_ring":true,"number_step":1,"number_sketch_step_mm":0.1,"number_sketch_angle_step_deg":0.1}`

Die Schritte sind endliche Zahlen von 0,001 bis 1000. number_step bezeichnet
die allgemeine Feldeinheit, die Skizzenoptionen mm bzw. Grad. Alte Adapter
dürfen unbekannte Felder ignorieren.

## Eingaben und Kontext

motion enthält x,y,z,a,b,c sowie sec,usec; nur das aktive, aktivierte Profil
erhält Eingaben. action enthält eine konfigurierte CAD-Aktion oder key:-Kombination.
Ringaktionen besitzen zusätzlich source:"ring" und delta. Die konfigurierte
Aktion, nicht das physische Vorzeichen allein, bestimmt die Richtung.
Normale Zoom-Tasten besitzen keine Ringquelle und sind kein Zahlenring.

Adapter melden context mit version:1, mode (navigation/numeric/blocked),
label, value, detail und optional step. step ist der wirksame positive,
endliche Schritt in der Feldeinheit (bis 1000000 nach Einheitenumrechnung).
Kontext ist nur für das aktive Profil gültig und läuft nach zwei Sekunden ab.
Der Adapter muss vor jeder Eingabe Fokus, Feldidentität, Wert und Alter prüfen.
Unlesbare oder unbestätigte Zahlenfelder erhalten keinen Zoom-Fallback.
Kontext ist eine Anzeige, keine Berechtigung zum beliebigen Schreiben.

Die Release-Fassung enthält keine Ringmesszähler, Diagnose-Sitzungen oder
ring_diagnostics-Nachrichten. Betriebs- und Sicherheitsprüfungen bleiben erhalten.



# SpaceCADHub – Lizenz- und Drittanbieterhinweise

Copyright (C) 2026 Benjamin Preuß

Der eigene C#- und Python-Projektcode, Build- und Testskripte sowie die eigene
Dokumentation dieses Quellpakets stehen unter GNU GPL Version 3 only
(SPDX: GPL-3.0-only), soweit eine Datei keinen abweichenden Hinweis enthält.

This program is free software: you can redistribute it and/or modify it
under the terms of the GNU General Public License as published by the Free
Software Foundation, version 3 of the License.

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
FITNESS FOR A PARTICULAR PURPOSE. See LICENSE for the complete terms.

Projekt und Kontakt: https://github.com/R2kGit/SpaceCADHub

## Drittkomponenten und Medien

SpaceController ist eine eingetragene Marke der SpaceControl GmbH & Co. KG.
Die installierte SpaceControl-Dokumentation nennt Copyright © 2009–2018
SpaceControl GmbH & Co. KG. Dies ist kein Copyright am SpaceCADHub-Projektcode.
Hersteller: https://spacecontrol.de/
Markenhinweis: https://spacecontrol.de/impressum/

- SpaceControl-Treiber, sc_daemon.exe und spc_ctrlr_64.dll sind externe
  Herstellerkomponenten. Sie sind nicht Bestandteil dieses Pakets und werden
  nicht durch die Projektlizenz lizenziert. Es gelten ihre jeweiligen Bedingungen.
- `Assets/controller.png` ist die vom Projektinhaber bereitgestellte bearbeitete
  Controllerabbildung. Etwaige Rechte an der zugrunde liegenden Geräteabbildung
  bleiben bei deren jeweiligen Rechteinhabern; die GPL-Freigabe für den eigenen
  Projektcode ist keine pauschale Lizenz an dieser Produktabbildung. Die
  Weiterverteilungsrechte am Bild sind vor öffentlicher Veröffentlichung zu prüfen.
- Windows, .NET Framework und CAD-Anwendungen werden nicht mitgeliefert.
  Ihre jeweiligen Lizenzen gelten unabhängig von diesem Projekt.
- Das KI-generierte Projektlogo und daraus abgeleitete Icons sind eigene
  Projektassets. Soweit Rechte daran bestehen und vom Projektinhaber eingeräumt
  werden können, werden sie unter derselben GPLv3 bereitgestellt. Rechte Dritter
  und Markenrechte werden dadurch nicht eingeräumt.

## Offener Punkt vor öffentlicher Binärverteilung

Der Hub bindet spc_ctrlr_64.dll per P/Invoke ein. Die GPL-Verträglichkeit dieser
Verknüpfung ist noch nicht abschließend geprüft. Das bloße Nicht-Mitliefern der
DLL klärt die Lizenzfrage nicht. Vor öffentlicher Verteilung einer solchen
Binärfassung sind die Herstellerbedingungen und gegebenenfalls eine eng
begrenzte zusätzliche Verknüpfungserlaubnis durch den Rechteinhaber zu klären.
Dieses Paket erteilt keine solche Ausnahme und keine Rechte an Herstellercode.

Referenz: https://www.gnu.org/licenses/gpl-faq.en.html#GPLIncompatibleLibs

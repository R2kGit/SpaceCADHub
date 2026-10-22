# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
import FreeCADGui as Gui
import SpaceControllerHub


class SpaceControllerCADHubWorkbench(Gui.Workbench):
    MenuText = "SpaceController CAD Hub"
    ToolTip = "SpaceController-Anbindung über den CAD Hub"

    def Initialize(self):
        SpaceControllerHub.start()

    def Activated(self):
        SpaceControllerHub.start()

    def Deactivated(self):
        pass

    def GetClassName(self):
        return "Gui::PythonWorkbench"


Gui.addWorkbench(SpaceControllerCADHubWorkbench())
SpaceControllerHub.start()

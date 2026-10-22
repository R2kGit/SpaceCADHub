# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
"""FreeCAD adapter for SpaceController CAD Hub.

All FreeCAD/Coin calls run on the Qt GUI thread.  The socket worker only
receives JSON and stores the newest state, so old motion packets never queue.
"""
import json
import math
import socket
import threading
import time

import FreeCAD as App
import FreeCADGui as Gui
from PySide import QtCore
from pivy import coin

HOST = "127.0.0.1"
PORT = 17861
_adapter = None


class Adapter:
    def __init__(self):
        self._lock = threading.Lock()
        self._latest_motion = None
        self._events = []
        self._stop = threading.Event()
        self._connected = False
        self._timer = QtCore.QTimer()
        self._timer.setInterval(16)
        self._timer.timeout.connect(self._tick)
        self._timer.start()
        self._thread = threading.Thread(target=self._socket_loop, name="SpaceController CAD Hub", daemon=True)
        self._thread.start()
        App.Console.PrintMessage("SpaceController CAD Hub: Adapter gestartet\n")

    def _socket_loop(self):
        while not self._stop.is_set():
            try:
                sock = socket.create_connection((HOST, PORT), timeout=2.0)
                sock.settimeout(None)
                stream = sock.makefile("rwb")
                stream.write((json.dumps({"type": "hello", "app": "FreeCAD", "version": "1.0.0"}) + "\n").encode("utf-8"))
                stream.flush()
                self._connected = True
                App.Console.PrintMessage("SpaceController CAD Hub: verbunden\n")
                while not self._stop.is_set():
                    line = stream.readline()
                    if not line:
                        break
                    message = json.loads(line.decode("utf-8"))
                    with self._lock:
                        if message.get("type") == "motion":
                            self._latest_motion = message
                        else:
                            self._events.append(message)
                            if len(self._events) > 32:
                                self._events = self._events[-32:]
            except Exception:
                pass
            finally:
                if self._connected:
                    App.Console.PrintMessage("SpaceController CAD Hub: Verbindung getrennt, neuer Versuch läuft\n")
                self._connected = False
                try:
                    sock.close()
                except Exception:
                    pass
            self._stop.wait(1.0)

    def _tick(self):
        with self._lock:
            motion = self._latest_motion
            self._latest_motion = None
            events = self._events
            self._events = []
        for event in events:
            if event.get("type") == "action":
                self._action(event.get("action", "none"))
            elif event.get("type") == "buttons":
                self._buttons(int(event.get("pressed", 0)))
            elif event.get("type") == "ring":
                self._zoom(int(event.get("delta", 0)))
        if motion:
            self._motion(motion)

    def _action(self, action):
        if action == "none":
            return
        if action == "undo":
            Gui.runCommand("Std_Undo")
        elif action == "redo":
            Gui.runCommand("Std_Redo")
        elif action == "escape":
            Gui.Control.closeDialog()
        elif action == "zoom_in":
            self._zoom(1)
        elif action == "zoom_out":
            self._zoom(-1)
        elif action.startswith("key:"):
            from PySide import QtGui
            sequence = QtGui.QKeySequence(action[4:])
            if sequence.isEmpty():
                return
            # Send a shortcut inside FreeCAD, never to an unrelated foreground app.
            encoded = int(sequence[0])
            modifiers = QtCore.Qt.KeyboardModifiers(encoded & 0xFE000000)
            key = encoded & 0x01FFFFFF
            target = Gui.getMainWindow().focusWidget() or Gui.getMainWindow()
            QtGui.QApplication.sendEvent(target, QtGui.QKeyEvent(QtCore.QEvent.KeyPress, key, modifiers))
            QtGui.QApplication.sendEvent(target, QtGui.QKeyEvent(QtCore.QEvent.KeyRelease, key, modifiers))
        else:
            view = self._view()
            methods = {"iso": "viewIsometric", "front": "viewFront", "back": "viewRear",
                       "right": "viewRight", "left": "viewLeft", "top": "viewTop",
                       "bottom": "viewBottom", "fit": "fitAll"}
            if view and action in methods:
                getattr(view, methods[action])()

    @staticmethod
    def _view():
        doc = Gui.activeDocument()
        return doc.activeView() if doc else None

    def _buttons(self, pressed):
        view = self._view()
        if pressed & 1:
            Gui.runCommand("Std_Undo")
        if pressed & 2:
            Gui.runCommand("Std_Redo")
        if view and pressed & 4:
            view.viewIsometric()
        if pressed & 512:
            try:
                Gui.Control.closeDialog()
            except Exception:
                pass
        if view and pressed & 1024:
            view.viewFront()
        if view and pressed & 2048:
            view.viewRight()
        if view and pressed & 4096:
            view.viewTop()
        if view and pressed & 8192:
            view.fitAll()

    def _zoom(self, delta):
        view = self._view()
        if not view or not delta:
            return
        cam = view.getCameraNode()
        factor = math.pow(1.08, -delta)
        if cam.getTypeId().isDerivedFrom(coin.SoOrthographicCamera.getClassTypeId()):
            cam.height = max(1e-5, float(cam.height.getValue()) * factor)
        else:
            focal = max(1e-5, float(cam.focalDistance.getValue()))
            forward = cam.orientation.getValue().multVec(coin.SbVec3f(0, 0, -1))
            cam.position = cam.position.getValue() + forward * (focal * (1.0 - factor))
            cam.focalDistance = focal * factor
        view.redraw()

    @staticmethod
    def _pivot():
        doc = App.ActiveDocument
        if not doc:
            return coin.SbVec3f(0, 0, 0)
        boxes = []
        for obj in doc.Objects:
            try:
                if obj.ViewObject.Visibility and hasattr(obj, "Shape") and not obj.Shape.isNull():
                    boxes.append(obj.Shape.BoundBox)
            except Exception:
                pass
        if not boxes:
            return coin.SbVec3f(0, 0, 0)
        xmin=min(b.XMin for b in boxes); xmax=max(b.XMax for b in boxes)
        ymin=min(b.YMin for b in boxes); ymax=max(b.YMax for b in boxes)
        zmin=min(b.ZMin for b in boxes); zmax=max(b.ZMax for b in boxes)
        return coin.SbVec3f((xmin+xmax)/2.0,(ymin+ymax)/2.0,(zmin+zmax)/2.0)

    def _motion(self, m):
        view = self._view()
        if not view:
            return
        x,y,z = float(m.get("x",0)),float(m.get("y",0)),float(m.get("z",0))
        a,b,c = float(m.get("a",0)),float(m.get("b",0)),float(m.get("c",0))
        if max(abs(x),abs(y),abs(z),abs(a),abs(b),abs(c)) < 0.01:
            return
        cam = view.getCameraNode()
        orient = cam.orientation.getValue()
        focal = max(1e-5,float(cam.focalDistance.getValue()))
        scale = focal * 0.00012
        rotation_active = max(abs(a),abs(b),abs(c)) >= 10
        if not rotation_active:
            # Model-fixed X/Y/Z, matching the SolidCraft profile.
            cam.position = cam.position.getValue() + coin.SbVec3f(-x*scale,z*scale,-y*scale)
        else:
            pivot=self._pivot(); pos=cam.position.getValue()
            yaw=coin.SbRotation(coin.SbVec3f(0,0,1), b*0.00010)
            right=orient.multVec(coin.SbVec3f(1,0,0))
            pitch=coin.SbRotation(right, -a*0.00010)
            roll=coin.SbRotation(orient.multVec(coin.SbVec3f(0,0,-1)), c*0.00006)
            rotation=roll * pitch * yaw
            new_orientation = rotation * orient
            new_position = pivot + rotation.multVec(pos-pivot)
            cam.position = new_position
            cam.orientation = new_orientation
            # Panning can leave the model away from the optical axis. Keep
            # every orbit centred on the visible geometry, independent of zoom.
            up = new_orientation.multVec(coin.SbVec3f(0,1,0))
            cam.pointAt(pivot, up)
            cam.focalDistance = max(1e-5, (pivot-new_position).length())
        view.redraw()

    def stop(self):
        self._stop.set()
        self._timer.stop()


def start():
    global _adapter
    if _adapter is None:
        _adapter = Adapter()


def stop():
    global _adapter
    if _adapter is not None:
        _adapter.stop()
        _adapter = None

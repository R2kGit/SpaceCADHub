# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
"""SpaceCADHub Fusion add-in. No vendor DLL, no model edits at startup."""
import logging
from logging.handlers import RotatingFileHandler
import math
import os
import time

import adsk.core

from . import hub_core
from .hub_transport import Client
from .windows_keys import ForegroundKeys
from .fusion_numbers import Numbers

EVENT_ID = 'de.borgcollective.spacecadhub.fusion.frame.v1'
_adapter = None
_logger = None


def log(message):
    global _logger
    if _logger is None:
        folder = os.path.join(os.environ.get('APPDATA', os.path.expanduser('~')),
                              'SpaceControllerCadHub')
        os.makedirs(folder, exist_ok=True)
        _logger = logging.getLogger('SpaceCADHubFusion')
        _logger.setLevel(logging.INFO)
        _logger.propagate = False
        if not _logger.handlers:
            handler = RotatingFileHandler(os.path.join(folder, 'fusion-adapter.log'),
                                          maxBytes=512*1024, backupCount=2, encoding='utf-8')
            handler.setFormatter(logging.Formatter('%(asctime)s %(message)s'))
            _logger.addHandler(handler)
    _logger.info(message)


def xyz(value):
    return (value.x, value.y, value.z)


class FrameHandler(adsk.core.CustomEventHandler):
    def __init__(self, adapter):
        super().__init__()
        self.adapter = adapter

    def notify(self, args):
        adapter = self.adapter
        try:
            if not adapter.client.stop_event.is_set():
                adapter.tick()
        except Exception:
            adapter.client.box.clear()
            # Avoid unbounded per-frame error logs if Fusion rejects a state.
            now = time.monotonic()
            if now-adapter.last_error > 2:
                adapter.last_error = now
                _logger.exception('Fusion frame failed')
        finally:
            adapter.client.event_done()


class Adapter:
    def __init__(self, app):
        self.app = app
        self.keys = ForegroundKeys()
        self.last_frame = time.monotonic()
        self.last_error = -10.0
        self.event = app.registerCustomEvent(EVENT_ID)
        if not self.event:
            raise RuntimeError('SpaceCADHub event is already registered')
        self.client = Client(lambda: app.fireCustomEvent(EVENT_ID), self.keys.foreground, log)
        self.numbers = Numbers(self.keys.foreground,
                               lambda action: self.client.box.put(dict(type='action', action=action)), log)
        self.client.configure = self.numbers.configure
        self.client.intercept = self.numbers.intercept
        self.client.disconnected = self.numbers.disconnected
        self.handler = FrameHandler(self)
        self.event.add(self.handler)
        try:
            self.numbers.start()
            self.client.start()
        except Exception:
            self.client.stop()
            self.numbers.stop()
            self.event.remove(self.handler)
            app.unregisterCustomEvent(EVENT_ID)
            raise

    def tick(self):
        now = time.monotonic()
        dt = max(0.0, min(1.0/30, now-self.last_frame))
        self.last_frame = now
        if not self.keys.foreground():
            self.client.box.clear()
            self.client.set_context('blocked', 'Fusion im Hintergrund')
            return
        viewport = self.app.activeViewport
        if not self.app.activeDocument or not viewport:
            self.client.box.clear()
            self.client.set_context('blocked', 'Kein Modellfenster')
            return
        if hasattr(self, 'numbers') and self.app.userInterface.activeCommand == 'SelectCommand':
            self.numbers.dialog_closed()
        context = self.numbers.context() if hasattr(self, 'numbers') else {}
        self.client.set_context(context.get('mode', 'navigation'), context.get('label', '3D-Navigation'),
                                value=context.get('value', ''), step=context.get('step'))
        axes, actions = self.client.box.take()
        for action in actions:
            self.action(action, viewport)
        if actions:
            # Never apply a pre-view-change cap sample on top of a view key.
            axes = (0.0,) * 6
        if any(axes):
            camera = viewport.camera
            height = self.height(camera)
            eye, target, up = hub_core.pose_step(xyz(camera.eye), xyz(camera.target),
                                                xyz(camera.upVector), height, axes, dt)
            camera.eye = adsk.core.Point3D.create(*eye)
            camera.target = adsk.core.Point3D.create(*target)
            camera.upVector = adsk.core.Vector3D.create(*up)
            self.apply(viewport, camera)

    @staticmethod
    def extents(camera):
        # The installed Fusion raises RuntimeError for perspective rather than
        # returning (False, ...). Hybrid orthographic faces are tried as well.
        try:
            ok, width, height = camera.getExtents()
            if ok and width > 0 and height > 0:
                return width, height
        except RuntimeError:
            pass
        return None

    @staticmethod
    def height(camera):
        extents = Adapter.extents(camera)
        if extents:
            width, height = extents
            return max(1e-5, height)
        distance = math.sqrt(hub_core.dot(hub_core.sub(xyz(camera.eye), xyz(camera.target)),
                                         hub_core.sub(xyz(camera.eye), xyz(camera.target))))
        return max(1e-5, 2 * distance * math.tan(camera.perspectiveAngle/2))

    @staticmethod
    def apply(viewport, camera):
        camera.isSmoothTransition = False
        viewport.camera = camera
        viewport.refresh()

    def zoom(self, viewport, steps):
        camera = viewport.camera
        factor = hub_core.zoom_factor(steps)
        extents = self.extents(camera)
        if extents:
            width, height = extents
            # Keep aspect ratio even at the numerical limits.
            factor = max(1e-5, min(1e9, height*factor))/height
            if not camera.setExtents(width*factor, height*factor):
                raise RuntimeError('Fusion rejected orthographic zoom')
        else:
            offset = hub_core.sub(xyz(camera.eye), xyz(camera.target))
            distance = math.sqrt(hub_core.dot(offset, offset))
            factor = max(1e-5, min(1e9, distance*factor))/distance
            camera.eye = adsk.core.Point3D.create(*hub_core.add(xyz(camera.target),
                                                              hub_core.mul(offset, factor)))
        self.apply(viewport, camera)

    def action(self, action, viewport):
        if not self.keys.foreground():
            return
        if action == 'none':
            return
        if action in ('zoom_in', 'zoom_out'):
            self.zoom(viewport, 1 if action == 'zoom_in' else -1)
            return
        if action == 'fit':
            viewport.fit()
            return
        orientations = {'iso': 'IsoTopRightViewOrientation', 'front': 'FrontViewOrientation',
                        'back': 'BackViewOrientation', 'right': 'RightViewOrientation',
                        'left': 'LeftViewOrientation', 'top': 'TopViewOrientation',
                        'bottom': 'BottomViewOrientation'}
        if action in orientations:
            camera = viewport.camera
            camera.viewOrientation = getattr(adsk.core.ViewOrientations, orientations[action])
            self.apply(viewport, camera)
        elif action == 'escape':
            self.app.userInterface.terminateActiveCommand()
        elif action in ('undo', 'redo'):
            # A keyboard chord respects Fusion's current editor/focus, unlike
            # globally undoing the document while the user edits a text field.
            self.keys.send('Ctrl+Z' if action == 'undo' else 'Ctrl+Y')
        elif action.startswith('key:'):
            try:
                if not self.keys.send(action[4:]):
                    log('Shortcut skipped: foreground or physical modifier guard')
            except ValueError as exc:
                log('Shortcut rejected: ' + str(exc))
        else:
            log('Unsupported Hub action ignored: ' + action[:99])

    def stop(self):
        self.client.stop()
        self.numbers.stop()
        self.event.remove(self.handler)
        self.app.unregisterCustomEvent(EVENT_ID)


def run(context):
    global _adapter
    if _adapter is not None:
        return
    log('Starting Fusion adapter 1.0.3')
    app = adsk.core.Application.get()
    try:
        _adapter = Adapter(app)
    except Exception:
        _logger.exception('Add-in start failed')
        raise


def stop(context):
    global _adapter
    if _adapter:
        adapter, _adapter = _adapter, None
        adapter.stop()
        log('Fusion adapter stopped')

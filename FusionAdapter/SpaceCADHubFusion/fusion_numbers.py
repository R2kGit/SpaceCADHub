# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
"""Isolated UIA worker: no waiting for accessibility on Fusion's UI thread."""
import collections
import json
import math
import os
import subprocess
import threading
import time


class Numbers:
    def __init__(self, foreground, zoom, log):
        self.foreground, self.zoom, self.log = foreground, zoom, log
        self.lock = threading.Lock()
        self.enabled = False
        self.step = 1.0
        self.sketch_step = .1
        self.sketch_angle_step = .1
        self.epoch = 0
        self.queue = collections.deque()
        self.snapshot = dict(mode='blocked', label='Zahlenring startet', value='')
        self.at = 0.0
        self.fault = False
        self.error_label = 'Zahlenring bitte aus/ein'
        self.failed_id = ''
        self.recovery_candidate = None
        self.recovery_at = 0.0
        self.stopped = threading.Event()
        self.wake = threading.Event()
        self.process = None
        self.thread = threading.Thread(target=self._worker, name='SpaceCADHub Fusion numbers', daemon=True)

    def start(self):
        self.thread.start()

    def configure(self, message):
        enabled = message.get('number_ring') is True
        step = message.get('number_step', 1)
        sketch_step = message.get('number_sketch_step_mm', .1)
        sketch_angle_step = message.get('number_sketch_angle_step_deg', .1)
        for candidate in (sketch_step, sketch_angle_step):
            if isinstance(candidate, bool) or not isinstance(candidate, (int, float)) or not math.isfinite(candidate) or not .001 <= candidate <= 1000:
                enabled = False
                sketch_step, sketch_angle_step = .1, .1
        if isinstance(step, bool) or not isinstance(step, (int, float)) or not math.isfinite(step) or not .001 <= step <= 1000:
            enabled, step = False, 1
        with self.lock:
            if not enabled or not self.enabled:
                self.fault = False
                self.failed_id = ''
                self.recovery_candidate = None
            self.enabled, self.step = enabled, float(step)
            self.sketch_step, self.sketch_angle_step = float(sketch_step), float(sketch_angle_step)
            self.epoch += 1
            self.queue.clear()
        self.wake.set()

    def disconnected(self):
        self.configure({})

    def dialog_closed(self):
        # Called only when Fusion's main thread confirms SelectCommand.
        # Closing a command clears its verification latch, never a dead helper.
        with self.lock:
            if self.fault and self.error_label == 'Zahlenring bitte aus/ein':
                self.fault = False
                self.failed_id = ''
                self.recovery_candidate = None
                self.epoch += 1
                self.queue.clear()
                self.at = 0.0
                self.log('Numeric verification guard reset after command closed')
                self.wake.set()

    def observe(self, snapshot):
        """Recovery requires a fresh, stable focus change, not a background hop.

        Do not retry the failed write. Only future physical detents are eligible.
        A dead companion is never reset through field focus.
        """
        now = time.monotonic()
        with self.lock:
            if not self.fault or not self.failed_id or self.error_label == 'Add-in neu starten':
                return
            mode, identity = snapshot.get('mode'), snapshot.get('id', '')
            candidate = (mode, identity)
            if not (mode == 'navigation' or (mode == 'numeric' and identity and identity != self.failed_id)):
                self.recovery_candidate = None
                return
            if candidate != self.recovery_candidate:
                self.recovery_candidate, self.recovery_at = candidate, now
                return
            if now-self.recovery_at < .150:
                return
            self.fault = False
            self.failed_id = ''
            self.recovery_candidate = None
            self.epoch += 1
            self.queue.clear()
            self.log('Numeric field guard cleared after stable focus change; no input replay')

    def context(self):
        with self.lock:
            if not self.enabled:
                return dict(mode='navigation', label='3D-Navigation', value='')
            if self.fault:
                return dict(mode='blocked', label=self.error_label, value='')
            if time.monotonic()-self.at > .5:
                return dict(mode='blocked', label='Feldpruefung laeuft', value='')
            return self.snapshot.copy()

    def intercept(self, message):
        if message.get('type') != 'action' or message.get('source') != 'ring' or message.get('action') not in ('zoom_in', 'zoom_out'):
            return False
        with self.lock:
            if not self.enabled:
                return False
            if self.fault or time.monotonic()-self.at > .5 or not self.foreground():
                return True
            snap = self.snapshot.copy()
            if snap.get('mode') not in ('numeric', 'navigation'):
                return True
            if len(self.queue) < 16:
                self.queue.append((time.monotonic(), self.epoch, snap.get('mode'), snap.get('id', ''),
                                   1 if message['action'] == 'zoom_in' else -1))
        self.wake.set()
        return True

    def _request(self, message):
        with self.lock:
            message = dict(message, body_step=self.step, sketch_step_mm=self.sketch_step,
                           sketch_angle_step_deg=self.sketch_angle_step)
        data = (json.dumps(message, ensure_ascii=True)+'\n').encode('utf-8')
        self.process.stdin.write(data)
        self.process.stdin.flush()
        line = self.process.stdout.readline(8192)
        if not line or not line.endswith(b'\n'):
            raise OSError('Numeric companion stopped')
        result = json.loads(line.decode('utf-8-sig'))
        if not isinstance(result, dict) or result.get('mode') not in ('navigation', 'numeric', 'blocked'):
            raise ValueError('Invalid numeric response')
        return result

    def _worker(self):
        try:
            executable = os.path.join(os.path.dirname(__file__), 'FusionNumbers.exe')
            self.process = subprocess.Popen([executable, '--pid', str(os.getpid())],
                                            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                            stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
            while not self.stopped.is_set():
                self.wake.wait(.075)
                self.wake.clear()
                with self.lock:
                    enabled, epoch, step = self.enabled, self.epoch, self.step
                    item = self.queue.popleft() if self.queue else None
                if not enabled:
                    continue
                if not self.foreground():
                    with self.lock:
                        self.queue.clear()
                        self.recovery_candidate = None
                        self.snapshot = dict(mode='blocked', label='Fusion im Hintergrund', value='')
                        self.at = time.monotonic()
                    continue
                before = self._request(dict(type='snapshot'))
                self.observe(before)
                with self.lock:
                    if epoch != self.epoch:
                        continue
                    self.snapshot, self.at = before, time.monotonic()
                if not item or time.monotonic()-item[0] > .250 or item[1] != epoch:
                    continue
                if item[2] != before.get('mode') or item[3] != before.get('id', ''):
                    continue
                reply = self._request(dict(type='route', mode=item[2], id=item[3],
                                            automation_id=before.get('automation_id', ''),
                                            value=before.get('value', ''), step=step, delta=item[4]))
                with self.lock:
                    if epoch != self.epoch:
                        continue
                    self.snapshot, self.at = reply, time.monotonic()
                    if reply.get('label') == 'Schreiben nicht bestaetigt':
                        self.fault = True
                        self.failed_id = before.get('id', '')
                        self.recovery_candidate = None
                        self.queue.clear()
                        self.log('Numeric field guard: ' + reply.get('verify_reason', 'unknown'))
                    if self.queue:
                        self.wake.set()
                if reply.get('mode') == 'navigation' and self.foreground() and time.monotonic()-item[0] <= .250:
                    self.zoom('zoom_in' if item[4] > 0 else 'zoom_out')
        except Exception as exc:
            if not self.stopped.is_set():
                self.log('Numeric companion unavailable: ' + type(exc).__name__)
            with self.lock:
                self.fault = True
                self.error_label = 'Add-in neu starten'
                self.queue.clear()
        finally:
            if self.process:
                if self.process.poll() is None:
                    self.process.kill()
                self.process.wait(timeout=2)

    def stop(self):
        self.stopped.set()
        self.wake.set()
        if self.process and self.process.poll() is None:
            self.process.kill()  # Only our private child, never Fusion or the Hub.
        if self.thread.is_alive():
            self.thread.join(timeout=2)

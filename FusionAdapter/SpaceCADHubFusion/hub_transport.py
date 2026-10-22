# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
"""Local JSON-lines client. Only fireCustomEvent may call Fusion off-thread."""
import json
import socket
import threading
import time

from .hub_core import Mailbox


class Client:
    def __init__(self, fire, foreground, log, host='127.0.0.1', port=17861):
        if host != '127.0.0.1':
            raise ValueError('Only the local Hub is allowed')
        self.fire, self.foreground, self.log = fire, foreground, log
        self.host, self.port = host, port
        self.box = Mailbox()
        self.stop_event = threading.Event()
        self.lock = threading.Lock()
        self.connected = False
        self.pending = False
        self.socket = None
        self.context = None
        self.context_at = 0.0
        self.last_pulse = 0.0
        self.threads = []
        self.configure = lambda message: None
        self.intercept = lambda message: False
        self.disconnected = lambda: None

    def start(self):
        for fn, name in ((self._receive, 'SpaceCADHub Fusion socket'),
                         (self._schedule, 'SpaceCADHub Fusion timer')):
            thread = threading.Thread(target=fn, name=name, daemon=True)
            self.threads.append(thread)
            thread.start()

    def set_context(self, mode, label, detail='', value='', step=None):
        with self.lock:
            self.context = dict(type='context', version=1, mode=mode,
                                label=label[:80], value=value[:80], detail=detail[:80], step=step)
            self.context_at = time.monotonic()

    def _schedule(self):
        was_foreground = False
        while not self.stop_event.wait(1.0/60.0):
            foreground = self.foreground()
            if not foreground or not was_foreground:
                self.box.clear()
            was_foreground = foreground
            now = time.monotonic()
            work = foreground and self.box.has_work()
            with self.lock:
                pulse = self.connected and now-self.last_pulse >= 0.5
                fire = self.connected and not self.pending and (work or pulse)
                if fire:
                    self.pending = True
                    self.last_pulse = now
            if fire:
                try:
                    if self.fire() is False:
                        self.event_done()
                except Exception as exc:
                    self.log('Custom event failed: ' + str(exc))
                    self.event_done()

    def event_done(self):
        with self.lock:
            self.pending = False

    def _send_context(self, sock):
        with self.lock:
            message = self.context if time.monotonic()-self.context_at < 1.5 else None
            ready = self.connected
        if ready and message:
            sock.sendall((json.dumps(message, ensure_ascii=True)+'\n').encode('utf-8'))

    def _receive(self):
        while not self.stop_event.is_set():
            sock = None
            try:
                sock = socket.create_connection((self.host, self.port), timeout=1.0)
                sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                sock.settimeout(0.25)
                with self.lock:
                    self.socket = sock
                sock.sendall(b'{"type":"hello","app":"Fusion","version":"1.0.3"}\n')
                buffer = b''
                next_context = 0.0
                while not self.stop_event.is_set():
                    if time.monotonic() >= next_context:
                        self._send_context(sock)
                        next_context = time.monotonic() + 0.5
                    try:
                        chunk = sock.recv(8192)
                    except socket.timeout:
                        continue
                    if not chunk:
                        break
                    buffer += chunk
                    if len(buffer) > 65536:
                        raise ValueError('Hub frame too large')
                    while b'\n' in buffer:
                        line, buffer = buffer.split(b'\n', 1)
                        message = json.loads(line.decode('utf-8'))
                        if not isinstance(message, dict):
                            raise ValueError('Expected JSON object')
                        if message.get('type') == 'ready':
                            if message.get('protocol') != 1:
                                raise ValueError('Unsupported Hub protocol')
                            with self.lock:
                                self.connected = True
                            self.configure(message)
                            self.log('Hub connected (protocol 1)')
                        elif message.get('type') == 'config':
                            self.configure(message)
                        elif self.connected and self.foreground():
                            if not self.intercept(message):
                                self.box.put(message)
                        else:
                            self.box.clear()
            except (OSError, ValueError, UnicodeError) as exc:
                self.log('Hub connection: ' + str(exc))
            finally:
                with self.lock:
                    was_connected = self.connected
                    self.connected = False
                    self.socket = None
                self.box.clear()
                self.disconnected()
                if sock:
                    sock.close()
                if was_connected:
                    self.log('Hub disconnected')
            self.stop_event.wait(1.0)

    def stop(self):
        self.stop_event.set()
        with self.lock:
            sock = self.socket
        if sock:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            sock.close()
        for thread in self.threads:
            thread.join(timeout=1.25)
        self.box.clear()

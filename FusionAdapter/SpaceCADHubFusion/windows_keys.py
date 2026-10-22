# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
"""Foreground-only shortcut output. No global hooks or pointer injection."""
import ctypes
import os
from ctypes import wintypes

MODIFIERS = {'ctrl': 0x11, 'control': 0x11, 'alt': 0x12, 'shift': 0x10}
KEYS = {'enter': 0x0D, 'return': 0x0D, 'escape': 0x1B, 'esc': 0x1B,
        'tab': 0x09, 'space': 0x20, 'delete': 0x2E, 'del': 0x2E,
        'backspace': 0x08, 'insert': 0x2D, 'home': 0x24, 'end': 0x23,
        'pageup': 0x21, 'pagedown': 0x22, 'left': 0x25, 'up': 0x26,
        'right': 0x27, 'down': 0x28}


def parse_shortcut(text):
    parts = [part.strip().lower() for part in text.split('+')]
    if not parts or any(not part for part in parts):
        raise ValueError('Empty shortcut')
    modifiers = []
    for part in parts[:-1]:
        if part not in MODIFIERS:
            raise ValueError('Unsupported shortcut modifier')
        code = MODIFIERS[part]
        if code not in modifiers:
            modifiers.append(code)
    last = parts[-1]
    if last in MODIFIERS:
        raise ValueError('Held modifier keys are not supported')
    if last in KEYS:
        key = KEYS[last]
    elif len(last) == 1 and last.isascii() and last.isalnum():
        key = ord(last.upper())
    elif last.startswith('f') and last[1:].isdigit() and 1 <= int(last[1:]) <= 24:
        key = 0x70 + int(last[1:])-1
    else:
        raise ValueError('Unsupported shortcut key')
    return modifiers, key


class KeyboardInput(ctypes.Structure):
    _fields_ = [('wVk', wintypes.WORD), ('wScan', wintypes.WORD),
                ('dwFlags', wintypes.DWORD), ('time', wintypes.DWORD),
                ('dwExtraInfo', ctypes.c_size_t)]


class MouseInput(ctypes.Structure):
    _fields_ = [('dx', wintypes.LONG), ('dy', wintypes.LONG),
                ('mouseData', wintypes.DWORD), ('dwFlags', wintypes.DWORD),
                ('time', wintypes.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class HardwareInput(ctypes.Structure):
    _fields_ = [('uMsg', wintypes.DWORD), ('wParamL', wintypes.WORD), ('wParamH', wintypes.WORD)]


class InputUnion(ctypes.Union):
    _fields_ = [('ki', KeyboardInput), ('mi', MouseInput), ('hi', HardwareInput)]


class Input(ctypes.Structure):
    _fields_ = [('type', wintypes.DWORD), ('data', InputUnion)]


class ForegroundKeys:
    def __init__(self):
        self.pid = os.getpid()
        self.user32 = ctypes.WinDLL('user32', use_last_error=True)
        self.user32.GetForegroundWindow.restype = wintypes.HWND
        self.user32.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))
        self.user32.IsWindowEnabled.argtypes = (wintypes.HWND,)
        self.user32.GetAsyncKeyState.argtypes = (ctypes.c_int,)
        self.user32.GetAsyncKeyState.restype = wintypes.SHORT
        self.user32.SendInput.argtypes = (wintypes.UINT, ctypes.POINTER(Input), ctypes.c_int)
        self.user32.SendInput.restype = wintypes.UINT

    def foreground(self):
        hwnd = self.user32.GetForegroundWindow()
        pid = wintypes.DWORD()
        self.user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        return bool(hwnd and pid.value == self.pid and self.user32.IsWindowEnabled(hwnd))

    def send(self, text):
        modifiers, key = parse_shortcut(text)
        # Do not release physical modifiers held by the user, or reinterpret
        # the shortcut while the user's keyboard chord is in progress.
        if any(self.user32.GetAsyncKeyState(vk) & 0x8000
               for vk in (0x10, 0x11, 0x12, 0x5B, 0x5C, key)):
            return False
        sequence = [(vk, 0) for vk in modifiers] + [(key, 0), (key, 2)]
        sequence += [(vk, 2) for vk in reversed(modifiers)]
        entries = (Input * len(sequence))()
        extended = {0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E}
        for entry, (vk, flags) in zip(entries, sequence):
            entry.type = 1
            entry.data.ki = KeyboardInput(vk, 0, flags | (1 if vk in extended else 0), 0, 0)
        if not self.foreground():
            return False
        sent = self.user32.SendInput(len(entries), entries, ctypes.sizeof(Input))
        if sent != len(entries):
            # Release only keys whose down-events were actually inserted.
            held = set()
            for vk, flags in sequence[:sent]:
                if flags & 2:
                    held.discard(vk)
                else:
                    held.add(vk)
            releases = (Input * len(held))()
            for entry, vk in zip(releases, held):
                entry.type = 1
                entry.data.ki = KeyboardInput(vk, 0, 2 | (1 if vk in extended else 0), 0, 0)
            if held:
                self.user32.SendInput(len(releases), releases, ctypes.sizeof(Input))
            raise OSError('Shortcut injection incomplete (possibly different privilege levels)')
        return True

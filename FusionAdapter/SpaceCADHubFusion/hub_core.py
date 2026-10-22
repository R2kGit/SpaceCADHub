# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
"""Fusion-independent camera mathematics and bounded input mailbox."""
import math
import threading
import time
from collections import deque

AXES = ('x', 'y', 'z', 'a', 'b', 'c')
MOTION_TTL = 0.150
ACTION_TTL = 0.250


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def mul(a, factor):
    return tuple(x * factor for x in a)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def unit(a):
    length = math.sqrt(dot(a, a))
    if not math.isfinite(length) or length < 1e-12:
        raise ValueError('Degenerate camera vector')
    return mul(a, 1.0 / length)


def rotate(vector, axis, angle):
    axis = unit(axis)
    cosine, sine = math.cos(angle), math.sin(angle)
    return add(add(mul(vector, cosine), mul(cross(axis, vector), sine)),
               mul(axis, dot(axis, vector) * (1.0-cosine)))


def pose_step(eye, target, up, height, axes, dt):
    """Model-fixed XYZ translation; orbit around the current native target.

    Always consumes the NEW camera read on Fusion's UI thread, never a cached
    pose. Orthographic/perspective projection and extents remain unchanged.
    """
    if not all(math.isfinite(v) for v in (*eye, *target, *up, height, dt)):
        raise ValueError('Non-finite camera')
    if height <= 0:
        raise ValueError('Invalid view height')
    dt = max(0.0, min(1.0/30.0, dt))  # Never catch up after a blocked UI.
    x, y, z, a, b, c = axes
    offset = sub(eye, target)
    forward = unit(mul(offset, -1))
    right = unit(cross(forward, up))
    up = unit(cross(right, forward))
    # Values already include the Hub's vendor sensitivity, deadzone, filtering
    # and inversion. This is only a fixed conversion to world units/radians.
    # Fusion's model axes differ from the physical cap axes: swap the Y/Z
    # input channels, retaining the existing world-axis signs and all rotation.
    translation = mul((-x, -z, y), height * 0.0008 * dt)
    target = add(target, translation)
    angular = 0.0006 * dt
    for axis, angle in (((0, 0, 1), b*angular),
                        (right, -a*angular), (forward, c*angular)):
        if angle:
            offset = rotate(offset, axis, angle)
            up = rotate(up, axis, angle)
    eye = add(target, offset)
    forward = unit(sub(target, eye))
    up = unit(sub(up, mul(forward, dot(up, forward))))
    return eye, target, up


def zoom_factor(steps):
    return math.exp(-0.08 * max(-8, min(8, steps)))


def motion_values(message):
    result = []
    for key in AXES:
        value = message.get(key, 0)
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise ValueError('Invalid axis')
        if not math.isfinite(value):
            raise ValueError('Non-finite axis')
        result.append(max(-10000.0, min(10000.0, float(value))))
    return tuple(result)


class Mailbox:
    """One latest motion state, FIFO actions, no historical motion replay."""
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.lock = threading.Lock()
        self.motion = None
        self.actions = deque()

    def clear(self):
        with self.lock:
            self.motion = None
            self.actions.clear()

    def put(self, message):
        now = self.clock()
        kind = message.get('type')
        if kind == 'motion':
            values = motion_values(message)
            with self.lock:
                self.motion = (now, values)
        elif kind == 'action':
            action = message.get('action')
            if not isinstance(action, str) or len(action) > 99:
                return
            with self.lock:
                if len(self.actions) < 32:
                    self.actions.append((now, action))

    def has_work(self):
        now = self.clock()
        with self.lock:
            return bool(self.actions or (self.motion and now-self.motion[0] <= MOTION_TTL
                                        and any(self.motion[1])))

    def take(self):
        now = self.clock()
        with self.lock:
            axes = (0.0,) * 6
            if self.motion and now-self.motion[0] <= MOTION_TTL:
                axes = self.motion[1]
            actions = [action for at, action in self.actions if now-at <= ACTION_TTL]
            self.actions.clear()
        return axes, actions

# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import ctypes
import ctypes.util
import json
import os
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import gi

gi.require_version("Gio", "2.0")
gi.require_version("GLib", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

from logger import log  # noqa: E402

GNOME_EXT_UUID = "antigravity-boot-animation@wynneceles.github.io"
GNOME_DBUS_NAME = "io.github.wynneceles.AntigravityBootAnimation.GnomeHelper"
GNOME_DBUS_PATH = "/io/github/wynneceles/AntigravityBootAnimation/GnomeHelper"
KWIN_DBUS_NAME = "io.github.wynneceles.AntigravityBootAnimation.KWinBridge"
KWIN_DBUS_PATH = "/io/github/wynneceles/AntigravityBootAnimation/KWinBridge"
OVERLAY_TITLE = "AntigravityBootAnimationOverlay"


@dataclass
class TargetWindow:
    window_id: str
    pid: int
    x: int
    y: int
    width: int
    height: int
    maximized: bool = False
    visible: bool = True
    covered_by_other: bool = False
    fully_covered: bool = False


def is_wayland_session() -> bool:
    return bool(os.environ.get("WAYLAND_DISPLAY")) or os.environ.get("XDG_SESSION_TYPE", "").lower() == "wayland"


def desktop_environment() -> str:
    return (os.environ.get("XDG_CURRENT_DESKTOP") or os.environ.get("DESKTOP_SESSION") or "").lower()


def _read_proc_cmdline(pid: int) -> list[str]:
    try:
        raw = Path(f"/proc/{pid}/cmdline").read_bytes()
    except OSError:
        return []
    if not raw:
        return []
    return [part.decode("utf-8", "replace") for part in raw.split(b"\x00") if part]


def _read_proc_exe(pid: int) -> str:
    try:
        return os.readlink(f"/proc/{pid}/exe")
    except OSError:
        return ""


def _read_proc_comm(pid: int) -> str:
    try:
        return Path(f"/proc/{pid}/comm").read_text(encoding="utf-8", errors="replace").strip()
    except OSError:
        return ""


def is_antigravity_main_process(pid: int, own_pid: int | None = None) -> bool:
    if own_pid is None:
        own_pid = os.getpid()
    if pid <= 1 or pid == own_pid:
        return False
    argv = _read_proc_cmdline(pid)
    if not argv:
        return False
    # Chromium/Electron zygote & helper processes rewrite argv[0] as one space-separated string.
    full_cmd = " ".join(argv)
    tokens = full_cmd.split()
    arg0_base = os.path.basename(tokens[0]).lower() if tokens else ""
    exe_base = os.path.basename(_read_proc_exe(pid)).lower()
    comm = _read_proc_comm(pid).lower()

    # Must actually be the antigravity binary (not bash/python/language_server mentioning antigravity in args).
    if exe_base:
        if exe_base != "antigravity" and not (exe_base.startswith("electron") and arg0_base == "antigravity"):
            return False
    elif arg0_base != "antigravity" and comm != "antigravity":
        return False

    for token in tokens[1:]:
        if token.startswith("--type=") or token.startswith("--import=") or token.startswith("--utility-sub-type="):
            return False
    return True


def find_antigravity_pids() -> list[int]:
    own = os.getpid()
    pids: list[int] = []
    try:
        entries = os.listdir("/proc")
    except OSError:
        return pids
    for entry in entries:
        if not entry.isdigit():
            continue
        pid = int(entry)
        if is_antigravity_main_process(pid, own_pid=own):
            pids.append(pid)
    pids.sort()
    return pids


class ProcessWatcher:
    """Polls /proc to detect Antigravity launches and exits without polling X11/D-Bus needlessly."""

    def __init__(self, ignore_existing: bool = True) -> None:
        self.seen_pids: set[int] = set(find_antigravity_pids()) if ignore_existing else set()
        self.active_pids: set[int] = set(find_antigravity_pids())

    def poll(self) -> tuple[list[int], bool]:
        current = set(find_antigravity_pids())
        launched = sorted(pid for pid in current if pid not in self.seen_pids)
        self.seen_pids.update(launched)
        was_running = bool(self.active_pids)
        self.active_pids = current
        all_exited = was_running and not current
        return launched, all_exited

    def mark_seen(self, pid: int) -> None:
        if pid > 0:
            self.seen_pids.add(pid)


# ───────────────────────────────── Base Tracker ─────────────────────────────────

class BaseTracker:
    name = "base"
    status_note = ""
    compositor_manages_geometry = False

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        return None

    def attach_overlay(self, gtk_window, target: TargetWindow, title_hint: str | None = None) -> None:
        pass

    def sync_overlay(self, gtk_window, target: TargetWindow, visible: bool, title_hint: str | None = None) -> None:
        pass

    def set_click_through(self, gtk_window, enabled: bool) -> None:
        pass

    def detach_overlay(self, gtk_window) -> None:
        pass

    def close(self) -> None:
        pass


# ───────────────────────────────── X11 Backend ─────────────────────────────────

class _XWindowAttributes(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_int),
        ("y", ctypes.c_int),
        ("width", ctypes.c_int),
        ("height", ctypes.c_int),
        ("border_width", ctypes.c_int),
        ("depth", ctypes.c_int),
        ("visual", ctypes.c_void_p),
        ("root", ctypes.c_ulong),
        ("c_class", ctypes.c_int),
        ("bit_gravity", ctypes.c_int),
        ("win_gravity", ctypes.c_int),
        ("backing_store", ctypes.c_int),
        ("backing_planes", ctypes.c_ulong),
        ("backing_pixel", ctypes.c_ulong),
        ("save_under", ctypes.c_int),
        ("colormap", ctypes.c_ulong),
        ("map_installed", ctypes.c_int),
        ("map_state", ctypes.c_int),
        ("all_event_masks", ctypes.c_long),
        ("your_event_mask", ctypes.c_long),
        ("do_not_propagate_mask", ctypes.c_long),
        ("override_redirect", ctypes.c_int),
        ("screen", ctypes.c_void_p),
    ]


class _XWindowChanges(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_int),
        ("y", ctypes.c_int),
        ("width", ctypes.c_int),
        ("height", ctypes.c_int),
        ("border_width", ctypes.c_int),
        ("sibling", ctypes.c_ulong),
        ("stack_mode", ctypes.c_int),
    ]


class _XRectangle(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_short),
        ("y", ctypes.c_short),
        ("width", ctypes.c_ushort),
        ("height", ctypes.c_ushort),
    ]


class _XClientMessageData(ctypes.Union):
    _fields_ = [("b", ctypes.c_char * 20), ("s", ctypes.c_short * 10), ("l", ctypes.c_long * 5)]


class _XClientMessageEvent(ctypes.Structure):
    _fields_ = [
        ("type", ctypes.c_int),
        ("serial", ctypes.c_ulong),
        ("send_event", ctypes.c_int),
        ("display", ctypes.c_void_p),
        ("window", ctypes.c_ulong),
        ("message_type", ctypes.c_ulong),
        ("format", ctypes.c_int),
        ("data", _XClientMessageData),
    ]


class X11Tracker(BaseTracker):
    name = "X11"
    status_note = "X11 原生窗口追踪"

    _IS_VIEWABLE = 2
    _CW_SIBLING = 1 << 5
    _CW_STACK_MODE = 1 << 6
    _STACK_ABOVE = 0
    _SHAPE_SET = 0
    _SHAPE_INPUT = 2
    _NET_WM_STATE_REMOVE = 0
    _NET_WM_STATE_ADD = 1

    def __init__(self) -> None:
        lib_x11 = ctypes.util.find_library("X11")
        lib_xext = ctypes.util.find_library("Xext")
        if not lib_x11:
            raise RuntimeError("libX11 不可用")
        self.x11 = ctypes.CDLL(lib_x11)
        self.xext = ctypes.CDLL(lib_xext) if lib_xext else None

        self.x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        self.x11.XOpenDisplay.restype = ctypes.c_void_p
        self.x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
        self.x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
        self.x11.XDefaultRootWindow.restype = ctypes.c_ulong
        self.x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
        self.x11.XInternAtom.restype = ctypes.c_ulong
        self.x11.XGetWindowProperty.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_ulong,
            ctypes.c_long,
            ctypes.c_long,
            ctypes.c_int,
            ctypes.c_ulong,
            ctypes.POINTER(ctypes.c_ulong),
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_ulong),
            ctypes.POINTER(ctypes.c_ulong),
            ctypes.POINTER(ctypes.c_void_p),
        ]
        self.x11.XGetWindowProperty.restype = ctypes.c_int
        self.x11.XFree.argtypes = [ctypes.c_void_p]
        self.x11.XGetWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(_XWindowAttributes)]
        self.x11.XGetWindowAttributes.restype = ctypes.c_int
        self.x11.XTranslateCoordinates.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_ulong),
        ]
        self.x11.XTranslateCoordinates.restype = ctypes.c_int
        self.x11.XMoveResizeWindow.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_uint,
            ctypes.c_uint,
        ]
        self.x11.XConfigureWindow.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_uint,
            ctypes.POINTER(_XWindowChanges),
        ]
        self.x11.XSendEvent.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_long,
            ctypes.c_void_p,
        ]
        self.x11.XFlush.argtypes = [ctypes.c_void_p]

        if self.xext:
            self.xext.XShapeCombineRectangles.argtypes = [
                ctypes.c_void_p,
                ctypes.c_ulong,
                ctypes.c_int,
                ctypes.c_int,
                ctypes.c_int,
                ctypes.POINTER(_XRectangle),
                ctypes.c_int,
                ctypes.c_int,
                ctypes.c_int,
            ]

        self.dpy = self.x11.XOpenDisplay(None)
        if not self.dpy:
            raise RuntimeError("XOpenDisplay 失败")
        self.root = self.x11.XDefaultRootWindow(self.dpy)
        self._atoms: dict[str, int] = {}
        self._above_state: dict[int, bool] = {}

    def _atom(self, name: str) -> int:
        if name not in self._atoms:
            self._atoms[name] = int(self.x11.XInternAtom(self.dpy, name.encode("ascii"), 0))
        return self._atoms[name]

    def _get_prop_ulongs(self, win: int, prop_name: str) -> list[int]:
        atom = self._atom(prop_name)
        actual_type = ctypes.c_ulong()
        actual_format = ctypes.c_int()
        nitems = ctypes.c_ulong()
        bytes_after = ctypes.c_ulong()
        data = ctypes.c_void_p()
        rc = self.x11.XGetWindowProperty(
            self.dpy,
            win,
            atom,
            0,
            4096,
            0,
            0,  # AnyPropertyType
            ctypes.byref(actual_type),
            ctypes.byref(actual_format),
            ctypes.byref(nitems),
            ctypes.byref(bytes_after),
            ctypes.byref(data),
        )
        if rc != 0 or not data.value:
            return []
        try:
            count = nitems.value
            if actual_format.value == 32:
                arr = ctypes.cast(data.value, ctypes.POINTER(ctypes.c_ulong))
                return [int(arr[i]) for i in range(count)]
            return []
        finally:
            self.x11.XFree(data)

    def _get_prop_str(self, win: int, prop_name: str) -> str:
        atom = self._atom(prop_name)
        actual_type = ctypes.c_ulong()
        actual_format = ctypes.c_int()
        nitems = ctypes.c_ulong()
        bytes_after = ctypes.c_ulong()
        data = ctypes.c_void_p()
        rc = self.x11.XGetWindowProperty(
            self.dpy,
            win,
            atom,
            0,
            4096,
            0,
            0,
            ctypes.byref(actual_type),
            ctypes.byref(actual_format),
            ctypes.byref(nitems),
            ctypes.byref(bytes_after),
            ctypes.byref(data),
        )
        if rc != 0 or not data.value:
            return ""
        try:
            raw = ctypes.string_at(data.value, nitems.value)
            return raw.replace(b"\x00", b" ").decode("utf-8", "replace").strip()
        finally:
            self.x11.XFree(data)

    def _window_rect(self, win: int) -> tuple[int, int, int, int, bool] | None:
        attrs = _XWindowAttributes()
        if not self.x11.XGetWindowAttributes(self.dpy, win, ctypes.byref(attrs)):
            return None
        if attrs.map_state != self._IS_VIEWABLE or attrs.width <= 0 or attrs.height <= 0:
            return None
        rx = ctypes.c_int()
        ry = ctypes.c_int()
        child = ctypes.c_ulong()
        if not self.x11.XTranslateCoordinates(
            self.dpy, win, self.root, 0, 0, ctypes.byref(rx), ctypes.byref(ry), ctypes.byref(child)
        ):
            return None
        return (int(rx.value), int(ry.value), int(attrs.width), int(attrs.height), True)

    def _window_title(self, win: int) -> str:
        return self._get_prop_str(win, "_NET_WM_NAME") or self._get_prop_str(win, "WM_NAME")

    def _window_states(self, win: int) -> set[int]:
        return set(self._get_prop_ulongs(win, "_NET_WM_STATE"))

    def _overlay_xid(self, gtk_window) -> int | None:
        if gtk_window is None:
            return None
        surface = gtk_window.get_surface()
        if surface is None:
            return None
        try:
            return int(surface.get_xid())
        except Exception:
            return None

    def _send_wm_state(self, win: int, action: int, state_atom_name: str, second_atom_name: str | None = None) -> None:
        ev = _XClientMessageEvent()
        ev.type = 33  # ClientMessage
        ev.window = win
        ev.message_type = self._atom("_NET_WM_STATE")
        ev.format = 32
        ev.data.l[0] = action
        ev.data.l[1] = self._atom(state_atom_name)
        ev.data.l[2] = self._atom(second_atom_name) if second_atom_name else 0
        ev.data.l[3] = 1  # normal application
        self.x11.XSendEvent(self.dpy, self.root, 0, (1 << 20) | (1 << 19), ctypes.byref(ev))

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        stack = self._get_prop_ulongs(self.root, "_NET_CLIENT_LIST_STACKING")
        if not stack:
            stack = self._get_prop_ulongs(self.root, "_NET_CLIENT_LIST")
        if not stack:
            return None

        cur_desktop_vals = self._get_prop_ulongs(self.root, "_NET_CURRENT_DESKTOP")
        cur_desktop = cur_desktop_vals[0] if cur_desktop_vals else None
        hidden_atom = self._atom("_NET_WM_STATE_HIDDEN")
        max_v = self._atom("_NET_WM_STATE_MAXIMIZED_VERT")
        max_h = self._atom("_NET_WM_STATE_MAXIMIZED_HORZ")
        fs_atom = self._atom("_NET_WM_STATE_FULLSCREEN")
        own_pid = os.getpid()

        best: tuple[int, TargetWindow, int] | None = None
        for idx, wid in enumerate(stack):
            title = self._window_title(wid)
            if title == OVERLAY_TITLE:
                continue
            pids = self._get_prop_ulongs(wid, "_NET_WM_PID")
            wpid = pids[0] if pids else 0
            if wpid == own_pid:
                continue
            states = self._window_states(wid)
            if hidden_atom in states:
                continue
            if cur_desktop is not None:
                desk_vals = self._get_prop_ulongs(wid, "_NET_WM_DESKTOP")
                if desk_vals and desk_vals[0] not in (cur_desktop, 0xFFFFFFFF):
                    continue

            if title_hint:
                if title_hint.lower() not in title.lower():
                    continue
            else:
                if target_pid is not None:
                    if wpid != target_pid:
                        continue
                else:
                    wm_class = self._get_prop_str(wid, "WM_CLASS").lower()
                    if "antigravity" not in wm_class and not is_antigravity_main_process(wpid, own_pid=own_pid):
                        continue

            rect = self._window_rect(wid)
            if not rect:
                continue
            x, y, w, h, _ = rect
            if w < 300 or h < 200:
                continue
            area = w * h
            maximized = (max_v in states and max_h in states) or (fs_atom in states)
            tw = TargetWindow(
                window_id=str(wid),
                pid=wpid,
                x=x,
                y=y,
                width=w,
                height=h,
                maximized=maximized,
                visible=True,
            )
            if best is None or area > best[0]:
                best = (area, tw, idx)

        if best is None:
            return None

        _, tw, target_idx = best
        covered = False
        fully_covered = False
        tx1, ty1, tx2, ty2 = tw.x, tw.y, tw.x + tw.width, tw.y + tw.height
        for wid in stack[target_idx + 1 :]:
            if self._window_title(wid) == OVERLAY_TITLE:
                continue
            pids = self._get_prop_ulongs(wid, "_NET_WM_PID")
            wpid = pids[0] if pids else 0
            if wpid == own_pid or (tw.pid and wpid == tw.pid):
                continue
            states = self._window_states(wid)
            if hidden_atom in states:
                continue
            rect = self._window_rect(wid)
            if not rect:
                continue
            ox, oy, ow, oh, _ = rect
            if ox < tx2 and (ox + ow) > tx1 and oy < ty2 and (oy + oh) > ty1:
                covered = True
                if ox <= tx1 and oy <= ty1 and (ox + ow) >= tx2 and (oy + oh) >= ty2:
                    fully_covered = True
        tw.covered_by_other = covered
        tw.fully_covered = fully_covered
        return tw

    def attach_overlay(self, gtk_window, target: TargetWindow, title_hint: str | None = None) -> None:
        xid = self._overlay_xid(gtk_window)
        if not xid:
            return
        self._send_wm_state(xid, self._NET_WM_STATE_ADD, "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER")
        self.sync_overlay(gtk_window, target, True, title_hint=title_hint)

    def sync_overlay(self, gtk_window, target: TargetWindow, visible: bool, title_hint: str | None = None) -> None:
        xid = self._overlay_xid(gtk_window)
        if not xid or not visible:
            return
        self.x11.XMoveResizeWindow(self.dpy, xid, target.x, target.y, max(1, target.width), max(1, target.height))
        want_above = not target.covered_by_other
        if self._above_state.get(xid) != want_above:
            self._send_wm_state(
                xid,
                self._NET_WM_STATE_ADD if want_above else self._NET_WM_STATE_REMOVE,
                "_NET_WM_STATE_ABOVE",
            )
            self._above_state[xid] = want_above
        if not want_above:
            try:
                target_xid = int(target.window_id)
                chg = _XWindowChanges()
                chg.sibling = target_xid
                chg.stack_mode = self._STACK_ABOVE
                self.x11.XConfigureWindow(
                    self.dpy, xid, self._CW_SIBLING | self._CW_STACK_MODE, ctypes.byref(chg)
                )
            except Exception:
                pass
        self.x11.XFlush(self.dpy)

    def set_click_through(self, gtk_window, enabled: bool) -> None:
        xid = self._overlay_xid(gtk_window)
        if not xid or not self.xext:
            return
        if enabled:
            rect = _XRectangle(0, 0, 0, 0)
            self.xext.XShapeCombineRectangles(
                self.dpy, xid, self._SHAPE_INPUT, 0, 0, ctypes.byref(rect), 0, self._SHAPE_SET, 0
            )
        else:
            w = max(1, gtk_window.get_width() or 1920)
            h = max(1, gtk_window.get_height() or 1080)
            rect = _XRectangle(0, 0, w, h)
            self.xext.XShapeCombineRectangles(
                self.dpy, xid, self._SHAPE_INPUT, 0, 0, ctypes.byref(rect), 1, self._SHAPE_SET, 0
            )
        self.x11.XFlush(self.dpy)

    def close(self) -> None:
        if getattr(self, "dpy", None):
            self.x11.XCloseDisplay(self.dpy)
            self.dpy = None


# ───────────────────────────── GNOME Wayland Backend ─────────────────────────────

class GnomeWaylandTracker(BaseTracker):
    name = "GNOME Wayland Extension"
    status_note = "GNOME Wayland 扩展已连接（精确跟随窗口与层级）"
    compositor_manages_geometry = True

    def __init__(self) -> None:
        self.proxy = Gio.DBusProxy.new_for_bus_sync(
            Gio.BusType.SESSION,
            Gio.DBusProxyFlags.NONE,
            None,
            GNOME_DBUS_NAME,
            GNOME_DBUS_PATH,
            GNOME_DBUS_NAME,
            None,
        )
        # Verify the extension is actually exported on the bus right now.
        self.proxy.call_sync(
            "Ping",
            None,
            Gio.DBusCallFlags.NONE,
            1000,
            None,
        )
        self._signal_hid = 0
        self._target_appeared_cb: Callable[[TargetWindow], None] | None = None

    @staticmethod
    def _parse_target_json(payload: str) -> TargetWindow | None:
        if not payload:
            return None
        d = json.loads(payload)
        if not d.get("found"):
            return None
        return TargetWindow(
            window_id=str(d["id"]),
            pid=int(d.get("pid", 0)),
            x=int(d["x"]),
            y=int(d["y"]),
            width=int(d["width"]),
            height=int(d["height"]),
            maximized=bool(d.get("maximized", False)),
            visible=bool(d.get("visible", True)),
            covered_by_other=bool(d.get("coveredByOther", False)),
            fully_covered=bool(d.get("fullyCovered", False)),
        )

    def subscribe_target_appeared(self, callback: Callable[[TargetWindow], None]) -> None:
        self._target_appeared_cb = callback
        if not self._signal_hid:
            self._signal_hid = self.proxy.connect("g-signal", self._on_proxy_signal)

    def _on_proxy_signal(self, _proxy, _sender, signal_name: str, params) -> None:
        if signal_name != "TargetAppeared" or self._target_appeared_cb is None:
            return
        try:
            payload = params.unpack()[0]
            tw = self._parse_target_json(payload)
            if tw is not None:
                cb = self._target_appeared_cb
                GLib.idle_add(lambda: (cb(tw), GLib.SOURCE_REMOVE)[1])
        except Exception:
            pass

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        try:
            res = self.proxy.call_sync(
                "GetTarget",
                GLib.Variant("(is)", (int(target_pid or 0), title_hint or "")),
                Gio.DBusCallFlags.NONE,
                1000,
                None,
            )
            tw = self._parse_target_json(res.unpack()[0])
            if tw is not None and tw.pid == os.getpid():
                return None
            return tw
        except Exception:
            return None

    def attach_overlay(self, gtk_window, target: TargetWindow, title_hint: str | None = None) -> None:
        try:
            self.proxy.call_sync(
                "AttachOverlay",
                GLib.Variant("(isis)", (os.getpid(), OVERLAY_TITLE, int(target.pid or 0), title_hint or "")),
                Gio.DBusCallFlags.NONE,
                1000,
                None,
            )
        except Exception as exc:
            log(f"GNOME 扩展 AttachOverlay 失败：{exc}")

    def sync_overlay(self, gtk_window, target: TargetWindow, visible: bool, title_hint: str | None = None) -> None:
        try:
            self.proxy.call(
                "SyncOverlay",
                GLib.Variant("(sisb)", (OVERLAY_TITLE, int(target.pid or 0), title_hint or "", bool(visible))),
                Gio.DBusCallFlags.NONE,
                500,
                None,
                None,
            )
        except Exception:
            pass

    def detach_overlay(self, gtk_window) -> None:
        try:
            self.proxy.call_sync(
                "DetachOverlay",
                GLib.Variant("(s)", (OVERLAY_TITLE,)),
                Gio.DBusCallFlags.NONE,
                500,
                None,
            )
        except Exception:
            pass

    def close(self) -> None:
        if self._signal_hid:
            try:
                self.proxy.disconnect(self._signal_hid)
            except Exception:
                pass
            self._signal_hid = 0
        self._target_appeared_cb = None


# ───────────────────────────── KDE KWin Wayland Backend ─────────────────────────────

class KWinWaylandTracker(BaseTracker):
    name = "KDE KWin Wayland"
    status_note = "KDE KWin 脚本已连接（精确跟随窗口）"

    _IFACE_XML = f"""
    <node>
      <interface name="{KWIN_DBUS_NAME}">
        <method name="ReportTarget">
          <arg type="s" name="jsonPayload" direction="in"/>
        </method>
      </interface>
    </node>
    """

    def __init__(self) -> None:
        self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        self.node_info = Gio.DBusNodeInfo.new_for_xml(self._IFACE_XML)
        self._reg_id = self.bus.register_object(
            KWIN_DBUS_PATH,
            self.node_info.interfaces[0],
            self._on_method,
            None,
            None,
        )
        self._name_id = Gio.bus_own_name_on_connection(
            self.bus, KWIN_DBUS_NAME, Gio.BusNameOwnerFlags.NONE, None, None
        )
        self._latest_target: TargetWindow | None = None
        self._script_id: int | None = None
        self._script_path = Path(__file__).resolve().parent.parent / "kwin-script" / "tracker.js"
        if not self._script_path.is_file():
            raise RuntimeError("找不到 KWin tracker.js")

        # Verify KWin Scripting D-Bus exists and load script
        self.kwin = Gio.DBusProxy.new_for_bus_sync(
            Gio.BusType.SESSION,
            Gio.DBusProxyFlags.NONE,
            None,
            "org.kde.KWin",
            "/Scripting",
            "org.kde.kwin.Scripting",
            None,
        )
        plugin_name = f"agba_{os.getpid()}"
        res = self.kwin.call_sync(
            "loadScript",
            GLib.Variant("(ss)", (str(self._script_path), plugin_name)),
            Gio.DBusCallFlags.NONE,
            1500,
            None,
        )
        self._script_id = int(res.unpack()[0])
        if self._script_id < 0:
            raise RuntimeError("KWin loadScript 返回失败")
        self.kwin.call_sync("start", None, Gio.DBusCallFlags.NONE, 1500, None)
        self._plugin_name = plugin_name

    def _on_method(self, _conn, _sender, _path, _iface, method, params, invocation) -> None:
        if method == "ReportTarget":
            raw = params.unpack()[0]
            try:
                d = json.loads(raw)
                if d.get("found"):
                    self._latest_target = TargetWindow(
                        window_id=str(d.get("id", "")),
                        pid=int(d.get("pid", 0)),
                        x=int(d.get("x", 0)),
                        y=int(d.get("y", 0)),
                        width=int(d.get("width", 0)),
                        height=int(d.get("height", 0)),
                        maximized=bool(d.get("maximized", False)),
                        visible=bool(d.get("visible", True)),
                        covered_by_other=bool(d.get("coveredByOther", False)),
                        fully_covered=bool(d.get("fullyCovered", False)),
                    )
                else:
                    self._latest_target = None
            except Exception:
                pass
            invocation.return_value(None)

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        return self._latest_target

    def close(self) -> None:
        try:
            if self._script_id is not None:
                self.kwin.call_sync("unloadScript", GLib.Variant("(s)", (self._plugin_name,)), Gio.DBusCallFlags.NONE, 500, None)
        except Exception:
            pass
        if self._reg_id:
            self.bus.unregister_object(self._reg_id)
        if self._name_id:
            Gio.bus_unown_name(self._name_id)


# ───────────────────────────── Hyprland / Sway IPC ─────────────────────────────

class HyprlandTracker(BaseTracker):
    name = "Hyprland"
    status_note = "Hyprland IPC 已连接"

    def __init__(self) -> None:
        if not os.environ.get("HYPRLAND_INSTANCE_SIGNATURE"):
            raise RuntimeError("非 Hyprland 会话")
        subprocess.check_output(["hyprctl", "version"], timeout=1)

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        try:
            clients = json.loads(subprocess.check_output(["hyprctl", "clients", "-j"], timeout=1, text=True))
        except Exception:
            return None
        own_pid = os.getpid()
        best = None
        for c in clients:
            title = c.get("title", "")
            if title == OVERLAY_TITLE:
                continue
            pid = int(c.get("pid", 0))
            if pid == own_pid:
                continue
            cls_lower = (c.get("class", "") or "").lower()
            if not title_hint and any(
                tag in cls_lower for tag in ("boot-animation", "antigravitybootanimation", "wynneceles")
            ):
                continue
            if title_hint:
                if title_hint.lower() not in title.lower():
                    continue
            elif target_pid:
                if pid != target_pid:
                    continue
            elif "antigravity" not in (cls_lower + " " + title.lower()):
                continue
            at = c.get("at", [0, 0])
            size = c.get("size", [0, 0])
            w, h = int(size[0]), int(size[1])
            if w < 300 or h < 200:
                continue
            area = w * h
            tw = TargetWindow(
                window_id=str(c.get("address", "")),
                pid=pid,
                x=int(at[0]),
                y=int(at[1]),
                width=w,
                height=h,
                maximized=bool(c.get("fullscreen", 0)),
                visible=not bool(c.get("hidden", False)),
            )
            if best is None or area > best[0]:
                best = (area, tw)
        return best[1] if best else None

    def sync_overlay(self, gtk_window, target: TargetWindow, visible: bool, title_hint: str | None = None) -> None:
        if not visible:
            return
        try:
            subprocess.Popen(
                [
                    "hyprctl",
                    "--batch",
                    f"dispatch setfloating title:^{OVERLAY_TITLE}$ ; "
                    f"dispatch resizewindowpixel exact {target.width} {target.height},title:^{OVERLAY_TITLE}$ ; "
                    f"dispatch movewindowpixel exact {target.x} {target.y},title:^{OVERLAY_TITLE}$",
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        except Exception:
            pass


# ───────────────────────────── Fallback Wayland Backend ─────────────────────────────

class FallbackTracker(BaseTracker):
    name = "Wayland Fallback"

    def __init__(self, note: str) -> None:
        self.status_note = note

    def find_target(self, target_pid: int | None = None, title_hint: str | None = None) -> TargetWindow | None:
        pids = [target_pid] if target_pid else find_antigravity_pids()
        if not pids and not title_hint:
            return None
        gi.require_version("Gdk", "4.0")
        from gi.repository import Gdk

        display = Gdk.Display.get_default()
        if display is None:
            return TargetWindow(
                window_id="fallback", pid=pids[0] if pids else 0, x=0, y=0, width=1280, height=720, maximized=True
            )
        mons = display.get_monitors()
        if mons.get_n_items() > 0:
            geom = mons.get_item(0).get_geometry()
            return TargetWindow(
                window_id="fallback",
                pid=pids[0] if pids else 0,
                x=geom.x,
                y=geom.y,
                width=max(800, geom.width),
                height=max(600, geom.height),
                maximized=True,
            )
        return TargetWindow(
            window_id="fallback", pid=pids[0] if pids else 0, x=0, y=0, width=1280, height=720, maximized=True
        )


def try_connect_gnome_tracker() -> GnomeWaylandTracker | None:
    try:
        return GnomeWaylandTracker()
    except Exception:
        return None


def _diagnose_gnome_extension_note() -> str:
    default_note = (
        "GNOME 扩展已安装但尚未载入：请注销并重新登录一次系统桌面"
        "（仅重启 Antigravity 不会加载新扩展），重登后即可自动覆盖启动动画并固定贴合在 Antigravity 窗口上。"
    )
    try:
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        res = bus.call_sync(
            "org.gnome.Shell",
            "/org/gnome/Shell",
            "org.gnome.Shell.Extensions",
            "GetExtensionInfo",
            GLib.Variant("(s)", (GNOME_EXT_UUID,)),
            GLib.VariantType("(a{sv})"),
            Gio.DBusCallFlags.NONE,
            800,
            None,
        )
        info = res.unpack()[0]
        if not info:
            return default_note
        err = info.get("error")
        if err:
            return f"GNOME 扩展加载出错：{err}（当前为通用浮层模式）"
        if info.get("enabled") is False:
            return (
                f"GNOME 扩展已载入但未启用：请运行 gnome-extensions enable {GNOME_EXT_UUID}"
                "（或在扩展管理中开启），开启后将自动热切换。"
            )
    except Exception:
        pass
    return default_note


def create_tracker() -> BaseTracker:
    if not is_wayland_session():
        try:
            return X11Tracker()
        except Exception as exc:
            log(f"X11 追踪初始化失败：{exc}")

    de = desktop_environment()
    if "gnome" in de or "ubuntu" in de:
        try:
            return GnomeWaylandTracker()
        except Exception as exc:
            ext_installed = (Path.home() / ".local" / "share" / "gnome-shell" / "extensions" / GNOME_EXT_UUID).is_dir()
            if ext_installed:
                note = _diagnose_gnome_extension_note()
            else:
                note = "未检测到 GNOME 窗口跟随扩展（当前为通用浮层模式）"
            log(f"GNOME 扩展暂未激活（{exc}），使用兜底模式")
            return FallbackTracker(note)

    if "kde" in de or "plasma" in de:
        try:
            return KWinWaylandTracker()
        except Exception as exc:
            log(f"KWin 追踪初始化失败（{exc}），使用兜底模式")
            return FallbackTracker("KDE KWin 脚本未连接（当前为通用浮层模式）")

    if os.environ.get("HYPRLAND_INSTANCE_SIGNATURE"):
        try:
            return HyprlandTracker()
        except Exception as exc:
            log(f"Hyprland 追踪初始化失败（{exc}）")

    return FallbackTracker("当前合成器不支持外部窗口定位（通用浮层模式）")

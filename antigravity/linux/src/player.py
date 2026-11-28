# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import ctypes
import ctypes.util
import math
import time
from enum import Enum
from pathlib import Path
from typing import Callable

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("Gst", "1.0")
from gi.repository import Gdk, GLib, Gst, Gtk  # noqa: E402

from config import Config  # noqa: E402
from logger import log  # noqa: E402
from media import ResolvedMedia  # noqa: E402
from subtitles import SubtitleOverlay  # noqa: E402
from window_tracker import OVERLAY_TITLE, BaseTracker, TargetWindow  # noqa: E402

_CSS_INSTALLED = False

_OVERLAY_CSS = b"""
window.agba-overlay {
    background-color: transparent;
    background-image: none;
    box-shadow: none;
    border: none;
}
.agba-video-clip {
    border-radius: 12px;
}
.agba-video-clip.maximized {
    border-radius: 0px;
}
.agba-intro-bg {
    background-color: #000000;
}
button.agba-skip {
    min-width: 72px;
    min-height: 28px;
    padding: 0 12px;
    border-radius: 14px;
    background-color: rgba(0, 0, 0, 0.45);
    background-image: none;
    border: 1px solid rgba(255, 255, 255, 0.35);
    color: #ffffff;
    font-size: 13px;
    font-weight: 500;
    box-shadow: none;
}
button.agba-skip:hover {
    background-color: rgba(0, 0, 0, 0.65);
    border-color: rgba(255, 255, 255, 0.55);
}
"""


def _ensure_overlay_css() -> None:
    global _CSS_INSTALLED
    if _CSS_INSTALLED:
        return
    display = Gdk.Display.get_default()
    if display is None:
        return
    provider = Gtk.CssProvider()
    provider.load_from_data(_OVERLAY_CSS)
    Gtk.StyleContext.add_provider_for_display(display, provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
    _CSS_INSTALLED = True


_INPUT_REGION_LIBS = None


class _CairoRectangleInt(ctypes.Structure):
    _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int), ("width", ctypes.c_int), ("height", ctypes.c_int)]


def _set_surface_input_region(surface: Gdk.Surface, click_through: bool, width: int, height: int) -> None:
    """Apply Gdk.Surface input region via ctypes so click-through works even without python3-gi-cairo."""
    global _INPUT_REGION_LIBS
    if _INPUT_REGION_LIBS is None:
        libgtk = ctypes.CDLL(ctypes.util.find_library("gtk-4"))
        libcairo = ctypes.CDLL(ctypes.util.find_library("cairo"))
        ctypes.pythonapi.PyCapsule_GetPointer.argtypes = [ctypes.py_object, ctypes.c_char_p]
        ctypes.pythonapi.PyCapsule_GetPointer.restype = ctypes.c_void_p
        libgtk.gdk_surface_set_input_region.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
        libcairo.cairo_region_create.restype = ctypes.c_void_p
        libcairo.cairo_region_create_rectangle.argtypes = [ctypes.POINTER(_CairoRectangleInt)]
        libcairo.cairo_region_create_rectangle.restype = ctypes.c_void_p
        libcairo.cairo_region_destroy.argtypes = [ctypes.c_void_p]
        _INPUT_REGION_LIBS = (libgtk, libcairo)

    libgtk, libcairo = _INPUT_REGION_LIBS
    surf_ptr = ctypes.pythonapi.PyCapsule_GetPointer(surface.__gpointer__, None)
    if click_through:
        region = libcairo.cairo_region_create()
    else:
        rect = _CairoRectangleInt(0, 0, max(1, int(width)), max(1, int(height)))
        region = libcairo.cairo_region_create_rectangle(ctypes.byref(rect))
    try:
        libgtk.gdk_surface_set_input_region(surf_ptr, region)
    finally:
        libcairo.cairo_region_destroy(region)


def _make_audio_sink() -> Gst.Element:
    for factory in ("pipewiresink", "pulsesink", "autoaudiosink", "alsasink"):
        el = Gst.ElementFactory.make(factory, None)
        if el is not None:
            return el
    return Gst.ElementFactory.make("fakesink", None)


def preroll_intro(intro_path: Path, volume: float) -> tuple[Gst.Element, Gst.Element] | None:
    """Pre-decode frame 0 of the intro video while Antigravity is booting."""
    Gst.init(None)
    sink = Gst.ElementFactory.make("gtk4paintablesink", None)
    if sink is None:
        return None
    pb = Gst.ElementFactory.make("playbin", "intro-preroll-playbin")
    if pb is None:
        return None
    pb.set_property("uri", intro_path.resolve().as_uri())
    pb.set_property("video-sink", sink)
    pb.set_property("audio-sink", _make_audio_sink())
    pb.set_property("volume", float(volume))
    pb.set_state(Gst.State.PAUSED)
    return pb, sink


class Phase(str, Enum):
    INTRO = "intro"
    REVEALED = "revealed"
    ENDING = "ending"
    DONE = "done"


class Show:
    """Manages one overlay playback session anchored over Antigravity's main window."""

    def __init__(
        self,
        app: Gtk.Application,
        tracker: BaseTracker,
        media: ResolvedMedia,
        target: TargetWindow,
        config: Config,
        with_intro: bool,
        on_done: Callable[["Show"], None],
        title_hint: str | None = None,
        prerolled_intro: tuple[Gst.Element, Gst.Element] | None = None,
    ) -> None:
        Gst.init(None)
        _ensure_overlay_css()

        self.app = app
        self.tracker = tracker
        self.media = media
        self.target = target
        self.config = config
        self.with_intro = with_intro
        self.on_done = on_done
        self.title_hint = title_hint

        self.phase = Phase.INTRO if with_intro else Phase.REVEALED
        self.paused = False
        self.in_loop_audio = not with_intro
        self.intro_gone = not with_intro
        self.current_sub = -1
        self._click_through = not with_intro
        self._sleeping = False
        self._last_rect: tuple[int, int, int, int, bool] | None = None
        self._last_visible = True
        self._attached = False
        self._surface_signals_connected = False

        self._follow_timer_id = 0
        self._pos_timer_id = 0
        self._alpha_timer_id = 0
        self._intro_fade_timer_id = 0
        self._vol_timer_id = 0
        self._intro_bus_watch_id = 0
        self._loop_bus_watch_id = 0

        self._loop_uri = media.loop_path.resolve().as_uri()
        self._intro_uri = media.intro_path.resolve().as_uri()
        self._current_alpha = 1.0 if with_intro else 0.0
        self._current_volume = float(config.intro_volume) if with_intro else 0.0
        self._started_at = time.monotonic()
        self._first_frame_logged = False
        self._loop_seg_armed = False

        # Build GTK4 transparent overlay window
        self.window = Gtk.Window(application=app)
        self.window.set_title(OVERLAY_TITLE)
        self.window.set_decorated(False)
        self.window.set_deletable(False)
        self.window.set_resizable(True)
        self.window.set_focusable(False)
        self.window.add_css_class("agba-overlay")
        self.window.set_default_size(max(300, target.width), max(200, target.height))

        self.root_overlay = Gtk.Overlay()
        self.root_overlay.set_can_focus(False)
        self.window.set_child(self.root_overlay)

        self.video_frame = Gtk.Overlay()
        self.video_frame.set_can_target(False)
        self.video_frame.set_can_focus(False)
        self.video_frame.set_overflow(Gtk.Overflow.HIDDEN)
        self.video_frame.add_css_class("agba-video-clip")
        if target.maximized:
            self.video_frame.add_css_class("maximized")
        if with_intro:
            self.video_frame.add_css_class("agba-intro-bg")
        self.video_frame.set_opacity(self._current_alpha)
        self.root_overlay.set_child(self.video_frame)

        # GStreamer pipelines with gtk4paintablesink
        self.loop_sink = Gst.ElementFactory.make("gtk4paintablesink", None)
        if self.loop_sink is None:
            raise RuntimeError("缺少 GStreamer 插件 gstreamer1.0-gtk4 (gtk4paintablesink)")
        self.loop_paintable = self.loop_sink.get_property("paintable")
        self.loop_picture = Gtk.Picture.new_for_paintable(self.loop_paintable)
        self.loop_picture.set_content_fit(Gtk.ContentFit.COVER)
        self.loop_picture.set_can_shrink(True)
        self.loop_picture.set_can_target(False)
        self.loop_picture.set_hexpand(True)
        self.loop_picture.set_vexpand(True)
        self.video_frame.set_child(self.loop_picture)

        self.loop_bin = self._build_loop_bin()

        self.intro_sink = None
        self.intro_bin = None
        self.intro_picture: Gtk.Picture | None = None
        if with_intro:
            if prerolled_intro is not None:
                self.intro_bin, self.intro_sink = prerolled_intro
                self.intro_bin.set_property("volume", self._current_volume)
                bus = self.intro_bin.get_bus()
                bus.add_signal_watch()
                self._intro_bus_watch_id = bus.connect("message", self._on_intro_bus_message)
            else:
                self.intro_sink = Gst.ElementFactory.make("gtk4paintablesink", None)
                self.intro_bin = self._build_intro_bin()
            self.intro_paintable = self.intro_sink.get_property("paintable")
            self.intro_picture = Gtk.Picture.new_for_paintable(self.intro_paintable)
            self.intro_picture.set_content_fit(Gtk.ContentFit.COVER)
            self.intro_picture.set_can_shrink(True)
            self.intro_picture.set_can_target(False)
            self.intro_picture.set_hexpand(True)
            self.intro_picture.set_vexpand(True)
            self.video_frame.add_overlay(self.intro_picture)

            self.intro_paintable.connect("invalidate-contents", self._on_first_frame)

        # Subtitles live in an independent overlay layer so they stay opaque during fade
        self.sub_view = SubtitleOverlay(width_ratio=config.subtitle_width_ratio)
        self.sub_view.set_viewport_size(max(300, target.width), max(200, target.height))
        self.root_overlay.add_overlay(self.sub_view)

        # Skip button in top-right (matching macOS maxX-90, maxY-72)
        self.skip_button = Gtk.Button(label="跳过 ›")
        self.skip_button.add_css_class("agba-skip")
        self.skip_button.set_halign(Gtk.Align.END)
        self.skip_button.set_valign(Gtk.Align.START)
        self.skip_button.set_margin_top(28)
        self.skip_button.set_margin_end(18)
        self.skip_button.set_focus_on_click(False)
        self.skip_button.set_cursor_from_name("pointer")
        self.skip_button.connect("clicked", lambda _b: self.skip_intro())
        self.skip_button.set_visible(with_intro)
        self.root_overlay.add_overlay(self.skip_button)

        self.window.connect("map", self._on_window_mapped)
        self.window.connect("notify::default-width", lambda *_: self._reapply_input_region())
        self.window.connect("notify::default-height", lambda *_: self._reapply_input_region())

        self.window.present()
        self._apply_target_rect(target)

        if with_intro:
            self.loop_bin.set_state(Gst.State.PAUSED)
            self.intro_bin.set_state(Gst.State.PLAYING)
            log(f"开始播放开屏动画，Antigravity 窗口 {target.window_id} ({target.width}x{target.height})")
        else:
            self.loop_bin.set_state(Gst.State.PLAYING)
            log(f"直接开启循环背景，Antigravity 窗口 {target.window_id} ({target.width}x{target.height})")
            self._ramp_video_alpha(config.residual_alpha, config.fade_duration_seconds)

        self._pos_timer_id = GLib.timeout_add(33, self._poll_playback)
        self._follow_timer_id = GLib.timeout_add(66, self._follow_tick)

    def _build_intro_bin(self) -> Gst.Element:
        pb = Gst.ElementFactory.make("playbin", "intro-playbin")
        pb.set_property("uri", self._intro_uri)
        pb.set_property("video-sink", self.intro_sink)
        pb.set_property("audio-sink", _make_audio_sink())
        pb.set_property("volume", self._current_volume)
        bus = pb.get_bus()
        bus.add_signal_watch()
        self._intro_bus_watch_id = bus.connect("message", self._on_intro_bus_message)
        return pb

    def _build_loop_bin(self) -> Gst.Element:
        pb = Gst.ElementFactory.make("playbin", "loop-playbin")
        pb.set_property("uri", self._loop_uri)
        pb.set_property("video-sink", self.loop_sink)
        fakesink = Gst.ElementFactory.make("fakesink", None)
        fakesink.set_property("sync", True)
        pb.set_property("audio-sink", fakesink)
        pb.set_property("mute", True)
        # Strip GST_PLAY_FLAG_AUDIO (0x2) so loop clips with an audio stream stay silent and synced.
        flags = pb.get_property("flags")
        pb.set_property("flags", int(flags) & ~2)
        bus = pb.get_bus()
        bus.add_signal_watch()
        self._loop_bus_watch_id = bus.connect("message", self._on_loop_bus_message)
        return pb

    def _on_first_frame(self, *_args) -> None:
        if not self._first_frame_logged:
            self._first_frame_logged = True
            elapsed_ms = int((time.monotonic() - self._started_at) * 1000)
            log(f"第一帧出画面，用时 {elapsed_ms}ms")

    def _on_window_mapped(self, *_args) -> None:
        if not self._attached:
            self._attached = True
            self.tracker.attach_overlay(self.window, self.target, title_hint=self.title_hint)
            if not getattr(self.tracker, "compositor_manages_geometry", False) and self.target.maximized:
                self.window.maximize()
        surface = self.window.get_surface()
        if surface is not None and not self._surface_signals_connected:
            self._surface_signals_connected = True
            surface.connect("notify::width", lambda *_: self._on_surface_resized())
            surface.connect("notify::height", lambda *_: self._on_surface_resized())
        self._reapply_input_region()

    def _on_surface_resized(self) -> None:
        surface = self.window.get_surface()
        if surface is not None:
            w = max(1, surface.get_width())
            h = max(1, surface.get_height())
            self.sub_view.set_viewport_size(w, h)
        self._reapply_input_region()

    def _reapply_input_region(self) -> None:
        surface = self.window.get_surface()
        if surface is None:
            return
        try:
            w = max(1, self.window.get_width() or self.target.width)
            h = max(1, self.window.get_height() or self.target.height)
            _set_surface_input_region(surface, self._click_through, w, h)
        except Exception as exc:
            log(f"设置点击穿透区域失败：{exc}")
        self.tracker.set_click_through(self.window, self._click_through)

    def _set_click_through(self, enabled: bool) -> None:
        if self._click_through == enabled:
            return
        self._click_through = enabled
        self._reapply_input_region()

    def _apply_target_rect(self, t: TargetWindow) -> None:
        state = (t.x, t.y, t.width, t.height, t.maximized)
        if state != self._last_rect:
            self._last_rect = state
            w = max(200, t.width)
            h = max(160, t.height)
            if not getattr(self.tracker, "compositor_manages_geometry", False):
                self.window.set_default_size(w, h)
            self.sub_view.set_viewport_size(w, h)
            if t.maximized:
                self.video_frame.add_css_class("maximized")
            else:
                self.video_frame.remove_css_class("maximized")
        self.tracker.sync_overlay(self.window, t, self._last_visible, title_hint=self.title_hint)
        if self._click_through:
            self._reapply_input_region()

    def _follow_tick(self) -> bool:
        if self.phase == Phase.DONE:
            self._follow_timer_id = 0
            return GLib.SOURCE_REMOVE
        if self._sleeping:
            return GLib.SOURCE_CONTINUE

        found = self.tracker.find_target(
            target_pid=self.target.pid if self.target.pid > 0 and not self.title_hint else None,
            title_hint=self.title_hint,
        )
        if found is None or not found.visible:
            if self._last_visible:
                self._last_visible = False
                self.window.set_visible(False)
                self.tracker.sync_overlay(self.window, self.target, False, title_hint=self.title_hint)
            return GLib.SOURCE_CONTINUE

        self.target = found
        if not self._last_visible:
            self._last_visible = True
            self.window.set_visible(True)
            self._reapply_input_region()
        self._apply_target_rect(found)
        return GLib.SOURCE_CONTINUE

    def _on_intro_bus_message(self, _bus: Gst.Bus, msg: Gst.Message) -> None:
        mtype = msg.type
        if mtype == Gst.MessageType.EOS:
            GLib.idle_add(self._finish_intro_into_loop)
        elif mtype == Gst.MessageType.ERROR:
            err, dbg = msg.parse_error()
            log(f"开屏播放出错：{err} ({dbg})")
            GLib.idle_add(self._finish_intro_into_loop)

    def _on_loop_bus_message(self, _bus: Gst.Bus, msg: Gst.Message) -> None:
        mtype = msg.type
        if mtype == Gst.MessageType.ASYNC_DONE and not self._loop_seg_armed and self.loop_bin is not None:
            self._loop_seg_armed = True
            self.loop_bin.seek(
                1.0,
                Gst.Format.TIME,
                Gst.SeekFlags.FLUSH | Gst.SeekFlags.SEGMENT,
                Gst.SeekType.SET,
                0,
                Gst.SeekType.NONE,
                0,
            )
        elif mtype == Gst.MessageType.SEGMENT_DONE and self.loop_bin is not None and self.phase != Phase.DONE:
            ok = self.loop_bin.seek(
                1.0,
                Gst.Format.TIME,
                Gst.SeekFlags.SEGMENT,
                Gst.SeekType.SET,
                0,
                Gst.SeekType.NONE,
                0,
            )
            if not ok:
                self.loop_bin.seek_simple(
                    Gst.Format.TIME, Gst.SeekFlags.FLUSH | Gst.SeekFlags.KEY_UNIT, 0
                )
        elif mtype == Gst.MessageType.EOS and self.loop_bin is not None and self.phase != Phase.DONE:
            self.loop_bin.seek_simple(
                Gst.Format.TIME,
                Gst.SeekFlags.FLUSH | Gst.SeekFlags.KEY_UNIT | Gst.SeekFlags.SEGMENT,
                0,
            )
        elif mtype == Gst.MessageType.ERROR:
            err, dbg = msg.parse_error()
            log(f"循环背景播放出错：{err} ({dbg})")

    def _poll_playback(self) -> bool:
        if self.phase == Phase.DONE:
            self._pos_timer_id = 0
            return GLib.SOURCE_REMOVE
        if self.paused or self.intro_gone or self.intro_bin is None:
            return GLib.SOURCE_CONTINUE

        ok, pos_ns = self.intro_bin.query_position(Gst.Format.TIME)
        if not ok or pos_ns < 0:
            return GLib.SOURCE_CONTINUE
        t = pos_ns / float(Gst.SECOND)

        if self.phase == Phase.INTRO and t >= self.config.fade_start_seconds:
            self.reveal(quick=False)
        if self.config.show_subtitles and self.config.subtitles:
            self._update_subtitle(t)
        return GLib.SOURCE_CONTINUE

    def _update_subtitle(self, t: float) -> None:
        if self.phase not in (Phase.INTRO, Phase.REVEALED) or not self.config.show_subtitles:
            return
        fade = max(0.01, self.config.subtitle_fade_seconds)
        idx = -1
        for i, sub in enumerate(self.config.subtitles):
            if sub.start <= t <= sub.end:
                idx = i
                break
        if idx < 0:
            if self.current_sub >= 0 and self.sub_view.alpha > 0:
                log(f"字幕 {self.current_sub + 1} 消失，视频时间 {t:.3f}s")
            self.sub_view.set_alpha(0.0)
            return
        sub = self.config.subtitles[idx]
        if idx != self.current_sub:
            self.current_sub = idx
            self.sub_view.width_ratio = self.config.subtitle_width_ratio
            self.sub_view.set_text(sub.text)
            log(f"字幕 {idx + 1} 出现，视频时间 {t:.3f}s")
        alpha = min(1.0, (t - sub.start) / fade, (sub.end - t) / fade)
        self.sub_view.set_alpha(max(0.0, alpha))

    def _hide_subtitles(self) -> None:
        if self.sub_view.alpha <= 0.001:
            return
        start_a = self.sub_view.alpha
        t0 = time.monotonic()

        def step() -> bool:
            if self.phase == Phase.DONE:
                return GLib.SOURCE_REMOVE
            k = min(1.0, (time.monotonic() - t0) / 0.4)
            self.sub_view.set_alpha(start_a * (1.0 - k))
            return GLib.SOURCE_CONTINUE if k < 1.0 else GLib.SOURCE_REMOVE

        GLib.timeout_add(16, step)

    def _finish_intro_into_loop(self) -> bool:
        if self.phase == Phase.DONE or self.intro_gone:
            return False
        self.intro_gone = True
        self.in_loop_audio = True
        self._hide_subtitles()
        if self.phase == Phase.INTRO:
            self.reveal(quick=False)

        # Ensure loop playback is rolling underneath before crossfading intro out
        if not self.paused:
            self.loop_bin.set_state(Gst.State.PLAYING)
        self._ramp_volume(0.0, 0.3)
        self._fade_out_intro_picture(0.25)
        if self._pos_timer_id:
            GLib.source_remove(self._pos_timer_id)
            self._pos_timer_id = 0
        return False

    def _fade_out_intro_picture(self, duration: float) -> None:
        if self.intro_picture is None:
            return
        if self._intro_fade_timer_id:
            GLib.source_remove(self._intro_fade_timer_id)
        t0 = time.monotonic()
        start_op = self.intro_picture.get_opacity()

        def step() -> bool:
            if self.phase == Phase.DONE or self.intro_picture is None:
                self._intro_fade_timer_id = 0
                return GLib.SOURCE_REMOVE
            k = min(1.0, (time.monotonic() - t0) / max(0.01, duration))
            eased = 0.5 - 0.5 * math.cos(math.pi * k)
            self.intro_picture.set_opacity(start_op * (1.0 - eased))
            if k >= 1.0:
                self._intro_fade_timer_id = 0
                self._dispose_intro_bin()
                return GLib.SOURCE_REMOVE
            return GLib.SOURCE_CONTINUE

        self._intro_fade_timer_id = GLib.timeout_add(16, step)

    def _dispose_intro_bin(self) -> None:
        if self.intro_picture is not None:
            self.intro_picture.set_visible(False)
            self.video_frame.remove_overlay(self.intro_picture)
            self.intro_picture = None
        if self.intro_bin is not None:
            bus = self.intro_bin.get_bus()
            if self._intro_bus_watch_id:
                bus.disconnect(self._intro_bus_watch_id)
                self._intro_bus_watch_id = 0
            bus.remove_signal_watch()
            self.intro_bin.set_state(Gst.State.NULL)
            self.intro_bin = None

    def reveal(self, quick: bool = False) -> None:
        if self.phase != Phase.INTRO:
            return
        self.phase = Phase.REVEALED
        self._set_click_through(True)
        self.video_frame.remove_css_class("agba-intro-bg")
        self.skip_button.set_visible(False)
        if not self.config.loop_background:
            log("进入循环帧，循环背景已关，淡出")
            self.finish(fast=False)
            return
        log("进入循环帧，透出界面")
        dur = 0.9 if quick else self.config.fade_duration_seconds
        self._ramp_video_alpha(self.config.residual_alpha, dur)

    def skip_intro(self) -> None:
        if self.phase != Phase.INTRO:
            return
        log("点了跳过开屏")
        if not self.config.loop_background:
            self.finish(fast=True)
            return
        self._hide_subtitles()
        self.intro_gone = True
        self.in_loop_audio = True
        # Keep intro_bin playing during the 0.9s skip crossfade so intro audio ramps down smoothly
        self.loop_bin.set_state(Gst.State.PLAYING)
        self._fade_out_intro_picture(0.9)
        self._ramp_volume(0.0, 0.9)
        self.reveal(quick=True)
        if self._pos_timer_id:
            GLib.source_remove(self._pos_timer_id)
            self._pos_timer_id = 0

    def set_paused(self, paused: bool) -> None:
        if self.phase != Phase.REVEALED or paused == self.paused:
            return
        self.paused = paused
        if paused:
            if not self.intro_gone and self.intro_bin is not None:
                self.intro_bin.set_state(Gst.State.PAUSED)
            else:
                self.loop_bin.set_state(Gst.State.PAUSED)
            log("暂停循环背景")
        else:
            if not self.intro_gone and self.intro_bin is not None:
                self.intro_bin.set_state(Gst.State.PLAYING)
            else:
                self.loop_bin.set_state(Gst.State.PLAYING)
            log("继续播放循环背景")

    def apply(self, config: Config) -> None:
        self.config = config
        if self.phase in (Phase.ENDING, Phase.DONE):
            return
        if self.phase == Phase.REVEALED and self._alpha_timer_id == 0:
            self._current_alpha = config.residual_alpha
            self.video_frame.set_opacity(self._current_alpha)
        if not self.in_loop_audio and self.intro_bin is not None:
            if self._vol_timer_id:
                GLib.source_remove(self._vol_timer_id)
                self._vol_timer_id = 0
            self._current_volume = float(config.intro_volume)
            self.intro_bin.set_property("volume", self._current_volume)

    def set_sleeping(self, sleeping: bool) -> None:
        self._sleeping = sleeping
        if not sleeping and self.phase != Phase.DONE:
            self._follow_tick()

    def _ramp_video_alpha(self, target: float, seconds: float, then: Callable[[], None] | None = None) -> None:
        if self._alpha_timer_id:
            GLib.source_remove(self._alpha_timer_id)
            self._alpha_timer_id = 0
        start = self._current_alpha
        dur = max(0.01, seconds)
        t0 = time.monotonic()

        def step() -> bool:
            if self.phase == Phase.DONE:
                self._alpha_timer_id = 0
                return GLib.SOURCE_REMOVE
            k = min(1.0, (time.monotonic() - t0) / dur)
            eased = 0.5 - 0.5 * math.cos(math.pi * k)
            self._current_alpha = start + (target - start) * eased
            self.video_frame.set_opacity(self._current_alpha)
            if k >= 1.0:
                self._alpha_timer_id = 0
                if then:
                    then()
                return GLib.SOURCE_REMOVE
            return GLib.SOURCE_CONTINUE

        self._alpha_timer_id = GLib.timeout_add(16, step)

    def _ramp_volume(self, target: float, seconds: float) -> None:
        if self.intro_bin is None:
            return
        if self._vol_timer_id:
            GLib.source_remove(self._vol_timer_id)
            self._vol_timer_id = 0
        start = float(self.intro_bin.get_property("volume"))
        dur = max(0.01, seconds)
        t0 = time.monotonic()

        def step() -> bool:
            if self.phase == Phase.DONE or self.intro_bin is None:
                self._vol_timer_id = 0
                return GLib.SOURCE_REMOVE
            k = min(1.0, (time.monotonic() - t0) / dur)
            val = max(0.0, start + (target - start) * k)
            self._current_volume = val
            self.intro_bin.set_property("volume", val)
            if k >= 1.0:
                self._vol_timer_id = 0
                return GLib.SOURCE_REMOVE
            return GLib.SOURCE_CONTINUE

        self._vol_timer_id = GLib.timeout_add(33, step)

    def finish(self, fast: bool) -> None:
        if self.phase in (Phase.ENDING, Phase.DONE):
            return
        self.phase = Phase.ENDING
        self._set_click_through(True)
        self.skip_button.set_visible(False)
        self._hide_subtitles()
        dur = 0.4 if fast else 1.2
        log("关闭（跳过）" if fast else "淡出")
        self._ramp_volume(0.0, dur)
        self._ramp_video_alpha(0.0, dur, then=self.teardown)

    def teardown(self) -> None:
        if self.phase == Phase.DONE:
            return
        self.phase = Phase.DONE
        for tid_attr in ("_follow_timer_id", "_pos_timer_id", "_alpha_timer_id", "_intro_fade_timer_id", "_vol_timer_id"):
            tid = getattr(self, tid_attr)
            if tid:
                GLib.source_remove(tid)
                setattr(self, tid_attr, 0)

        self._dispose_intro_bin()
        if self.loop_bin is not None:
            bus = self.loop_bin.get_bus()
            if self._loop_bus_watch_id:
                bus.disconnect(self._loop_bus_watch_id)
                self._loop_bus_watch_id = 0
            bus.remove_signal_watch()
            self.loop_bin.set_state(Gst.State.NULL)
            self.loop_bin = None

        try:
            self.tracker.detach_overlay(self.window)
        except Exception:
            pass
        try:
            self.window.close()
        except Exception:
            pass
        log("播放结束")
        self.on_done(self)

    @property
    def playing(self) -> bool:
        return self.phase in (Phase.INTRO, Phase.REVEALED)

    @property
    def can_pause(self) -> bool:
        return self.phase == Phase.REVEALED

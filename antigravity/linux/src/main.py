# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import argparse
import fcntl
import os
import subprocess
import sys
from pathlib import Path
from typing import Callable

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("Gio", "2.0")
gi.require_version("GLib", "2.0")
gi.require_version("Gst", "1.0")
from gi.repository import Gio, GLib, Gst, Gtk  # noqa: E402

from config import Config  # noqa: E402
from logger import log, log_path  # noqa: E402
from media import ResolvedMedia, find_asset_file, linux_root, resolve_media  # noqa: E402
from player import Show, preroll_intro  # noqa: E402
from settings_window import SettingsWindow  # noqa: E402
from subtitles import render_subtitle_to_png  # noqa: E402
from tray import TrayIcon  # noqa: E402
from window_tracker import (  # noqa: E402
    GNOME_DBUS_NAME,
    GnomeWaylandTracker,
    ProcessWatcher,
    TargetWindow,
    create_tracker,
    try_connect_gnome_tracker,
)

APP_BUS_ID = "io.github.wynneceles.AntigravityBootAnimation"
CTRL_DBUS_PATH = "/io/github/wynneceles/AntigravityBootAnimation/Control"

_CTRL_XML = f"""
<node>
  <interface name="{APP_BUS_ID}">
    <method name="ShowSettings"/>
    <method name="PreviewIntro"/>
    <method name="PreviewLoop"/>
    <method name="StopShow"/>
    <method name="Quit"/>
  </interface>
</node>
"""


class Controller:
    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.config = Config.load()
        self.tracker = create_tracker()
        self.media: ResolvedMedia | None = None
        self.show: Show | None = None
        self.settings: SettingsWindow | None = None
        self.tray: TrayIcon | None = None
        self.watcher = ProcessWatcher(ignore_existing=True)
        self._lock_fh = None
        self._ctrl_reg_id = 0
        self._ctrl_name_id = 0
        self._sleep_sub_id = 0
        self._gnome_sub_id = 0
        self._watch_timer_id = 0
        self._waiting_timer_id = 0
        self._waiting_pid = 0
        self._prerolled_intro = None
        self._hold = None

        self.app = Gtk.Application(
            application_id=APP_BUS_ID,
            flags=Gio.ApplicationFlags.NON_UNIQUE,
        )
        self.app.connect("activate", self._on_activate)

    def acquire_single_instance_or_notify(self) -> bool:
        Config.config_dir().mkdir(parents=True, exist_ok=True)
        lock_file = Config.config_dir() / ".lock"
        fh = lock_file.open("a+")
        try:
            fcntl.flock(fh.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            self._lock_fh = fh
            return True
        except BlockingIOError:
            fh.close()
            try:
                bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
                bus.call_sync(
                    APP_BUS_ID,
                    CTRL_DBUS_PATH,
                    APP_BUS_ID,
                    "ShowSettings",
                    None,
                    None,
                    Gio.DBusCallFlags.NONE,
                    1500,
                    None,
                )
            except Exception as exc:
                log(f"通知已运行实例打开设置窗口失败：{exc}")
            return False

    def run(self) -> int:
        return self.app.run(None)

    def _on_activate(self, _app: Gtk.Application) -> None:
        if self._hold is None:
            self._hold = self.app.hold()

        Gst.init(None)
        try:
            self.media = resolve_media(self.args.intro, self.args.loop)
        except Exception as exc:
            log(f"媒体初始化失败：{exc}")
            print(f"错误：{exc}", file=sys.stderr)
            self.quit()
            return

        test_mode = bool(
            self.args.preview
            or self.args.loop_only
            or self.args.play
            or self.args.target_title
        )
        if test_mode:
            self._run_test_mode()
            return

        self._export_control_dbus()
        self._subscribe_sleep_events()
        self._wire_gnome_tracker()
        self._subscribe_gnome_helper_appearance()
        if not self.args.no_tray:
            self.tray = TrayIcon(
                {
                    "get_state": self._tray_state,
                    "toggle_intro": self.toggle_intro,
                    "toggle_loop": self.toggle_loop,
                    "set_alpha": lambda a: self.update_config(lambda c: setattr(c, "residual_alpha", a)),
                    "set_volume": lambda v: self.update_config(lambda c: setattr(c, "intro_volume", v)),
                    "preview_intro": self.preview_intro,
                    "toggle_loop_now": self.toggle_loop_now,
                    "toggle_pause": self.toggle_pause,
                    "open_settings": self.open_settings,
                    "open_config": self.open_config,
                    "uninstall": self.uninstall,
                }
            )

        log(
            f"常驻启动，开屏={self.config.play_intro} 循环背景={self.config.loop_background} "
            f"追踪={self.tracker.name} 转码={self.media.transcoded}"
        )
        self._watch_timer_id = GLib.timeout_add(100, self._watch_tick)
        if self.args.settings:
            self.open_settings()

    def _export_control_dbus(self) -> None:
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        info = Gio.DBusNodeInfo.new_for_xml(_CTRL_XML)
        self._ctrl_reg_id = bus.register_object(
            CTRL_DBUS_PATH,
            info.interfaces[0],
            self._on_ctrl_method,
            None,
            None,
        )
        self._ctrl_name_id = Gio.bus_own_name_on_connection(
            bus, APP_BUS_ID, Gio.BusNameOwnerFlags.NONE, None, None
        )

    def _on_ctrl_method(self, _conn, _sender, _path, _iface, method: str, _params, invocation) -> None:
        if method == "ShowSettings":
            GLib.idle_add(self.open_settings)
        elif method == "PreviewIntro":
            GLib.idle_add(self.preview_intro)
        elif method == "PreviewLoop":
            GLib.idle_add(self.preview_loop)
        elif method == "StopShow":
            GLib.idle_add(self.stop_show)
        elif method == "Quit":
            GLib.idle_add(self.quit)
        invocation.return_value(None)

    def _subscribe_sleep_events(self) -> None:
        try:
            sys_bus = Gio.bus_get_sync(Gio.BusType.SYSTEM, None)
            self._sleep_sub_id = sys_bus.signal_subscribe(
                "org.freedesktop.login1",
                "org.freedesktop.login1.Manager",
                "PrepareForSleep",
                "/org/freedesktop/login1",
                None,
                Gio.DBusSignalFlags.NONE,
                self._on_prepare_for_sleep,
            )
        except Exception:
            pass

    def _on_prepare_for_sleep(self, _conn, _sender, _path, _iface, _signal, params) -> None:
        going_to_sleep = bool(params.unpack()[0])
        if self.show is not None:
            GLib.idle_add(self.show.set_sleeping, going_to_sleep)

    def _wire_gnome_tracker(self) -> None:
        if isinstance(self.tracker, GnomeWaylandTracker):
            self.tracker.subscribe_target_appeared(self._on_gnome_target_appeared)

    def _subscribe_gnome_helper_appearance(self) -> None:
        if isinstance(self.tracker, GnomeWaylandTracker):
            return
        try:
            bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
            self._gnome_sub_id = bus.signal_subscribe(
                "org.freedesktop.DBus",
                "org.freedesktop.DBus",
                "NameOwnerChanged",
                "/org/freedesktop/DBus",
                GNOME_DBUS_NAME,
                Gio.DBusSignalFlags.NONE,
                self._on_gnome_helper_name_changed,
            )
            self._upgrade_to_gnome_tracker()
        except Exception:
            pass

    def _on_gnome_helper_name_changed(self, _conn, _sender, _path, _iface, _signal, params) -> None:
        _name, _old_owner, new_owner = params.unpack()
        if not new_owner or isinstance(self.tracker, GnomeWaylandTracker):
            return
        GLib.idle_add(self._upgrade_to_gnome_tracker)

    def _upgrade_to_gnome_tracker(self) -> bool:
        if isinstance(self.tracker, GnomeWaylandTracker):
            return False
        new_tracker = try_connect_gnome_tracker()
        if new_tracker is None:
            return False
        old_tracker = self.tracker
        self.tracker = new_tracker
        old_tracker.close()
        self._wire_gnome_tracker()
        if self._gnome_sub_id:
            try:
                Gio.bus_get_sync(Gio.BusType.SESSION, None).signal_unsubscribe(self._gnome_sub_id)
            except Exception:
                pass
            self._gnome_sub_id = 0
        log("检测到 GNOME 窗口跟随扩展上线，已热切换至 GNOME Wayland Extension 追踪")
        self._refresh_ui()
        return False

    def _on_gnome_target_appeared(self, target: TargetWindow) -> None:
        if target.pid == os.getpid():
            return
        if self.show is not None:
            return
        if target.pid > 0 and target.pid in self.watcher.seen_pids and target.pid != self._waiting_pid:
            return
        self.watcher.mark_seen(target.pid)
        if self._waiting_timer_id:
            GLib.source_remove(self._waiting_timer_id)
            self._waiting_timer_id = 0
            self._waiting_pid = 0
        self.config = Config.load()
        if not (self.config.play_intro or self.config.loop_background):
            self._dispose_prerolled()
            return
        log(f"检测到 Antigravity 窗口出现 pid={target.pid}，立即覆盖开屏={self.config.play_intro}")
        self._start_show(target, with_intro=self.config.play_intro, exit_when_done=False)

    def _dispose_prerolled(self) -> None:
        if self._prerolled_intro is not None:
            pb, _sink = self._prerolled_intro
            self._prerolled_intro = None
            try:
                pb.set_state(Gst.State.NULL)
            except Exception:
                pass

    def _watch_tick(self) -> bool:
        launched, all_exited = self.watcher.poll()
        if all_exited:
            if self._waiting_timer_id:
                GLib.source_remove(self._waiting_timer_id)
                self._waiting_timer_id = 0
                self._waiting_pid = 0
            self._dispose_prerolled()
            if self.show is not None:
                self.show.teardown()
        for pid in launched:
            self.config = Config.load()
            log(f"检测到 Antigravity 启动 pid={pid}，开屏={self.config.play_intro} 循环背景={self.config.loop_background}")
            if self.config.play_intro or self.config.loop_background:
                self._wait_and_start(pid, with_intro=self.config.play_intro, exit_when_done=False)
        return GLib.SOURCE_CONTINUE

    def _wait_and_start(self, pid: int, with_intro: bool, exit_when_done: bool) -> None:
        if self._waiting_timer_id:
            GLib.source_remove(self._waiting_timer_id)
            self._waiting_timer_id = 0
        self._dispose_prerolled()
        if self.show is not None:
            self.show.teardown()
        self._waiting_pid = pid
        if with_intro and self.media is not None:
            self._prerolled_intro = preroll_intro(self.media.intro_path, self.config.intro_volume)
        attempts = [0]

        def poll_window() -> bool:
            attempts[0] += 1
            target = self.tracker.find_target(target_pid=pid, title_hint=self.args.target_title)
            if target is not None:
                self._waiting_timer_id = 0
                self._waiting_pid = 0
                self._start_show(target, with_intro=with_intro, exit_when_done=exit_when_done)
                return GLib.SOURCE_REMOVE
            if attempts[0] >= 1000:  # 30s timeout at 30ms interval
                self._waiting_timer_id = 0
                self._waiting_pid = 0
                self._dispose_prerolled()
                log("等不到 Antigravity 主窗口，放弃这次")
                if exit_when_done:
                    self.quit()
                return GLib.SOURCE_REMOVE
            return GLib.SOURCE_CONTINUE

        self._waiting_timer_id = GLib.timeout_add(30, poll_window)

    def _start_show(self, target: TargetWindow, with_intro: bool, exit_when_done: bool) -> None:
        if self.show is not None:
            self.show.teardown()
        assert self.media is not None

        prerolled = self._prerolled_intro if with_intro else None
        self._prerolled_intro = None
        if not with_intro:
            self._dispose_prerolled()

        def on_done(finished: Show) -> None:
            if self.show is finished:
                self.show = None
            self._refresh_ui()
            if exit_when_done:
                GLib.timeout_add(200, lambda: (self.quit(), GLib.SOURCE_REMOVE)[1])

        self.show = Show(
            app=self.app,
            tracker=self.tracker,
            media=self.media,
            target=target,
            config=self.config,
            with_intro=with_intro,
            on_done=on_done,
            title_hint=self.args.target_title,
            prerolled_intro=prerolled,
        )
        if self.args.test_skip is not None and with_intro:
            s_ref = self.show
            GLib.timeout_add(
                int(max(0.05, self.args.test_skip) * 1000),
                lambda: (s_ref.skip_intro(), GLib.SOURCE_REMOVE)[1],
            )
        self._refresh_ui()

    def start_on_current_window(self, with_intro: bool, exit_when_done: bool) -> bool:
        target = self.tracker.find_target(title_hint=self.args.target_title)
        if target is None:
            return False
        self.watcher.mark_seen(target.pid)
        self._start_show(target, with_intro=with_intro, exit_when_done=exit_when_done)
        return True

    def _run_test_mode(self) -> None:
        with_intro = not self.args.loop_only and self.args.play != "loop"
        if not self.start_on_current_window(with_intro=with_intro, exit_when_done=True):
            log("没找到正在运行的 Antigravity 窗口")
            print("没找到正在运行的 Antigravity 窗口（可用 --target-title 指定测试窗口）。", file=sys.stderr)
            self.quit()
            return
        if self.args.exit_after is not None:
            GLib.timeout_add(
                int(max(0.1, self.args.exit_after) * 1000),
                lambda: (self.show.teardown() if self.show else None, self.quit(), GLib.SOURCE_REMOVE)[2],
            )

    def _tray_state(self) -> dict:
        s = self.show
        playing = bool(s and s.playing)
        return {
            "play_intro": self.config.play_intro,
            "loop_background": self.config.loop_background,
            "residual_alpha": self.config.residual_alpha,
            "intro_volume": self.config.intro_volume,
            "playing": playing,
            "in_intro": bool(s and s.phase.value == "intro"),
            "can_pause": bool(s and s.can_pause),
            "paused": bool(s and s.paused),
        }

    def _refresh_ui(self) -> None:
        if self.settings is not None:
            self.settings.refresh()
        if self.tray is not None:
            self.tray.refresh()

    def update_config(self, mutate: Callable[[Config], None], source=None) -> None:
        mutate(self.config)
        self.config.save()
        if self.show is not None:
            self.show.apply(self.config)
        if source is not self.settings and self.settings is not None:
            self.settings.refresh()
        if source is not self.tray and self.tray is not None:
            self.tray.refresh()

    def toggle_intro(self) -> None:
        self.update_config(lambda c: setattr(c, "play_intro", not c.play_intro))
        log(f"开屏动画 → {self.config.play_intro}")

    def toggle_loop(self) -> None:
        self.update_config(lambda c: setattr(c, "loop_background", not c.loop_background))
        log(f"循环背景 → {self.config.loop_background}")

    def preview_intro(self) -> None:
        self.config = Config.load()
        if not self.start_on_current_window(with_intro=True, exit_when_done=False):
            log("预览失败：没找到 Antigravity 窗口")

    def preview_loop(self) -> None:
        self.config = Config.load()
        if not self.start_on_current_window(with_intro=False, exit_when_done=False):
            log("开启循环背景失败：没找到 Antigravity 窗口")

    def stop_show(self) -> None:
        if self.show is not None:
            self.show.finish(fast=True)

    def toggle_loop_now(self) -> None:
        if self.show is not None and self.show.playing:
            self.show.finish(fast=True)
        else:
            self.preview_loop()

    def toggle_pause(self) -> None:
        if self.show is not None:
            self.show.set_paused(not self.show.paused)
            self._refresh_ui()

    def open_settings(self) -> None:
        self.config = Config.load()
        if self.settings is None:
            self.settings = SettingsWindow(self)
        self.settings.present()

    def open_config(self) -> None:
        self.config.save()
        try:
            subprocess.Popen(["xdg-open", str(Config.file_path())], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except Exception as exc:
            log(f"打开设置文件失败：{exc}")

    def uninstall(self) -> None:
        if self.show is not None:
            self.show.teardown()
        script = linux_root() / "uninstall.sh"
        if script.is_file():
            subprocess.Popen(
                ["/bin/sh", str(script), "--from-app"],
                start_new_session=True,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        self.quit()

    def quit(self) -> None:
        if self._watch_timer_id:
            GLib.source_remove(self._watch_timer_id)
            self._watch_timer_id = 0
        if self._waiting_timer_id:
            GLib.source_remove(self._waiting_timer_id)
            self._waiting_timer_id = 0
        if self._gnome_sub_id:
            try:
                Gio.bus_get_sync(Gio.BusType.SESSION, None).signal_unsubscribe(self._gnome_sub_id)
            except Exception:
                pass
            self._gnome_sub_id = 0
        self._dispose_prerolled()
        if self.show is not None:
            self.show.teardown()
        if self.tray is not None:
            self.tray.close()
            self.tray = None
        if self._ctrl_reg_id:
            try:
                Gio.bus_get_sync(Gio.BusType.SESSION, None).unregister_object(self._ctrl_reg_id)
            except Exception:
                pass
            self._ctrl_reg_id = 0
        if self._ctrl_name_id:
            try:
                Gio.bus_unown_name(self._ctrl_name_id)
            except Exception:
                pass
            self._ctrl_name_id = 0
        self.tracker.close()
        if self._lock_fh is not None:
            try:
                self._lock_fh.close()
            except Exception:
                pass
        self.app.quit()


def parse_args(argv: list[str]) -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Gemini Boot Animation for Antigravity (Linux)")
    p.add_argument("--settings", action="store_true", help="打开设置窗口")
    p.add_argument("--preview", action="store_true", help="在当前 Antigravity 窗口预览一次开屏")
    p.add_argument("--loop-only", action="store_true", help="仅预览循环背景")
    p.add_argument("--play", choices=["intro", "loop"], default=None, help="测试用：指定播放模式")
    p.add_argument("--test-skip", type=float, default=None, help="测试用：N 秒后自动点击跳过")
    p.add_argument("--exit-after", type=float, default=None, help="测试用：N 秒后退出")
    p.add_argument("--target-title", type=str, default=None, help="测试用：按窗口标题匹配目标窗口")
    p.add_argument("--render-subtitle", nargs=3, metavar=("BG_PNG", "OUT_PNG", "INDEX"), help="离线字幕渲染测试")
    p.add_argument("--no-tray", action="store_true", help="禁用系统托盘图标")
    p.add_argument("--intro", type=Path, default=None, help="指定开屏视频路径")
    p.add_argument("--loop", type=Path, default=None, help="指定循环视频路径")
    args, unknown = p.parse_known_args(argv)
    if unknown:
        log(f"忽略未知启动参数：{' '.join(unknown)}")
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)

    if args.render_subtitle:
        bg_path, out_path, idx_str = args.render_subtitle
        cfg = Config.load()
        idx = max(0, min(len(cfg.subtitles) - 1, int(idx_str) - 1))
        text = cfg.subtitles[idx].text if cfg.subtitles else ""
        render_subtitle_to_png(Path(bg_path), Path(out_path), text, width_ratio=cfg.subtitle_width_ratio)
        return 0

    single_instance = not (args.preview or args.loop_only or args.play or args.target_title)
    ctrl = Controller(args)
    if single_instance and not ctrl.acquire_single_instance_or_notify():
        log("已经有一个在运行，通知它弹出设置窗口后退出")
        return 0
    return ctrl.run()


if __name__ == "__main__":
    raise SystemExit(main())

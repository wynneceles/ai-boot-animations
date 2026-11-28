# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SRC_DIR = Path(__file__).resolve().parent.parent / "src"
sys.path.insert(0, str(SRC_DIR))

import cairo  # noqa: E402
from config import Config  # noqa: E402
from icon import render_sni_pixmaps, write_png  # noqa: E402
from player import _set_surface_input_region  # noqa: E402
from subtitles import compute_font_size, render_subtitle_to_png  # noqa: E402
from tray import TrayIcon  # noqa: E402
from window_tracker import (  # noqa: E402
    FallbackTracker,
    GnomeWaylandTracker,
    ProcessWatcher,
    X11Tracker,
    find_antigravity_pids,
    is_antigravity_main_process,
)


class ConfigTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self._old_cfg = os.environ.get("XDG_CONFIG_HOME")
        os.environ["XDG_CONFIG_HOME"] = self._tmp.name

    def tearDown(self) -> None:
        if self._old_cfg is None:
            os.environ.pop("XDG_CONFIG_HOME", None)
        else:
            os.environ["XDG_CONFIG_HOME"] = self._old_cfg
        self._tmp.cleanup()

    def test_defaults_and_roundtrip(self) -> None:
        cfg = Config.load()
        self.assertTrue(cfg.play_intro)
        self.assertTrue(cfg.loop_background)
        self.assertAlmostEqual(cfg.fade_start_seconds, 12.93)
        self.assertAlmostEqual(cfg.fade_duration_seconds, 1.2)
        self.assertAlmostEqual(cfg.residual_alpha, 0.25)
        self.assertAlmostEqual(cfg.intro_volume, 1.0)
        self.assertFalse(cfg.show_subtitles)
        self.assertEqual(len(cfg.subtitles), 2)
        self.assertTrue(Config.file_path().is_file())

    def test_legacy_enabled_and_clamping(self) -> None:
        p = Config.file_path()
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(
            json.dumps({"enabled": False, "residualAlpha": 4.2, "introVolume": -0.5}),
            encoding="utf-8",
        )
        cfg = Config.load()
        self.assertFalse(cfg.play_intro)
        self.assertAlmostEqual(cfg.residual_alpha, 1.0)
        self.assertAlmostEqual(cfg.intro_volume, 0.0)


class IconTests(unittest.TestCase):
    def test_sni_argb_big_endian(self) -> None:
        pixmaps = render_sni_pixmaps((22, 32, 48))
        self.assertEqual([w for w, _h, _b in pixmaps], [22, 32, 48])
        w, h, data = pixmaps[1]
        self.assertEqual(len(data), w * h * 4)
        # Top-left corner (0,0) should be transparent
        self.assertEqual(data[0], 0)
        # Center (16,16) should be opaque ARGB with blue/purple components
        off = (16 * w + 16) * 4
        a, r, g, b = data[off], data[off + 1], data[off + 2], data[off + 3]
        self.assertEqual(a, 255)
        self.assertGreater(b, 150)
        self.assertGreater(r, 80)
        self.assertGreater(g, 100)

    def test_write_png(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "icon.png"
            write_png(out, 128)
            raw = out.read_bytes()
            self.assertTrue(raw.startswith(b"\x89PNG"))


class SubtitleTests(unittest.TestCase):
    def test_font_size_formula(self) -> None:
        self.assertAlmostEqual(compute_font_size(1550, 1000, 0.6), 60.0)
        self.assertAlmostEqual(compute_font_size(300, 200, 0.6), 18.0)

    def test_render_subtitle_to_png(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            bg = Path(tmp) / "bg.png"
            out = Path(tmp) / "out.png"
            surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, 1280, 720)
            ctx = cairo.Context(surf)
            ctx.set_source_rgb(0.1, 0.12, 0.2)
            ctx.paint()
            surf.write_to_png(str(bg))

            render_subtitle_to_png(bg, out, "Where twin stars align, my blades carve a new horizon.")
            self.assertTrue(out.is_file())

            s_bg = cairo.ImageSurface.create_from_png(str(bg))
            s_out = cairo.ImageSurface.create_from_png(str(out))
            d_bg = bytes(s_bg.get_data())
            d_out = bytes(s_out.get_data())
            self.assertNotEqual(d_bg, d_out)

            # Bottom subtitle band around 85% height must differ, top band must remain unchanged
            stride = s_out.get_stride()
            top_slice = slice(0, 100 * stride)
            bot_slice = slice(int(0.78 * 720) * stride, int(0.92 * 720) * stride)
            self.assertEqual(d_bg[top_slice], d_out[top_slice])
            self.assertNotEqual(d_bg[bot_slice], d_out[bot_slice])


class ProcessTests(unittest.TestCase):
    def test_excludes_self_and_shell(self) -> None:
        self.assertFalse(is_antigravity_main_process(os.getpid()))
        self.assertFalse(is_antigravity_main_process(1))
        pids = find_antigravity_pids()
        # Any returned pid must have executable or comm named antigravity and no --type=
        for pid in pids:
            cmd = Path(f"/proc/{pid}/cmdline").read_bytes().replace(b"\x00", b" ").decode("utf-8", "replace")
            self.assertNotIn("--type=", cmd)
            self.assertNotIn("--import=", cmd)
        watcher = ProcessWatcher(ignore_existing=True)
        launched, exited = watcher.poll()
        self.assertEqual(launched, [])
        self.assertFalse(exited)


class ShellScriptTests(unittest.TestCase):
    def test_bash_syntax(self) -> None:
        linux_dir = Path(__file__).resolve().parent.parent
        for name in ("antigravity-boot-animation", "install.sh", "uninstall.sh", "web-install.sh"):
            script = linux_dir / name
            res = subprocess.run(["bash", "-n", str(script)], capture_output=True, text=True)
            self.assertEqual(res.returncode, 0, f"{name}: {res.stderr}")


class TrayAndX11Tests(unittest.TestCase):
    def test_x11_tracker_smoke(self) -> None:
        if not os.environ.get("DISPLAY"):
            self.skipTest("DISPLAY not set")
        tracker = X11Tracker()
        try:
            # Should return None or a TargetWindow without throwing ctypes errors
            _ = tracker.find_target(title_hint="__nonexistent_window_title__")
        finally:
            tracker.close()

    def test_tray_dbus_layout(self) -> None:
        import threading
        from gi.repository import Gio, GLib

        state = {
            "play_intro": True,
            "loop_background": True,
            "residual_alpha": 0.25,
            "intro_volume": 1.0,
            "playing": False,
            "in_intro": False,
            "can_pause": False,
            "paused": False,
        }
        noop = lambda *a, **k: None
        tray = TrayIcon(
            {
                "get_state": lambda: state,
                "toggle_intro": noop,
                "toggle_loop": noop,
                "set_alpha": noop,
                "set_volume": noop,
                "preview_intro": noop,
                "toggle_loop_now": noop,
                "toggle_pause": noop,
                "open_settings": noop,
                "open_config": noop,
                "uninstall": noop,
            }
        )
        loop = GLib.MainLoop()
        results: dict = {}

        def client_worker() -> None:
            try:
                bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
                layouts = []
                for pid in (0, 5, 6):
                    res = bus.call_sync(
                        tray.bus_name,
                        tray.MENU_PATH,
                        "com.canonical.dbusmenu",
                        "GetLayout",
                        GLib.Variant("(iias)", (pid, -1, [])),
                        None,
                        Gio.DBusCallFlags.NONE,
                        1500,
                        None,
                    )
                    layouts.append((pid, res.unpack()))
                pix_res = bus.call_sync(
                    tray.bus_name,
                    tray.SNI_PATH,
                    "org.freedesktop.DBus.Properties",
                    "Get",
                    GLib.Variant("(ss)", ("org.kde.StatusNotifierItem", "IconPixmap")),
                    None,
                    Gio.DBusCallFlags.NONE,
                    1500,
                    None,
                )
                results["layouts"] = layouts
                results["pixmaps"] = pix_res.unpack()[0]
            except Exception as exc:
                results["error"] = exc
            finally:
                GLib.idle_add(loop.quit)

        try:
            GLib.timeout_add(50, lambda: (threading.Thread(target=client_worker, daemon=True).start(), False)[1])
            GLib.timeout_add(4000, lambda: (loop.quit(), False)[1])
            loop.run()
            self.assertNotIn("error", results, repr(results.get("error")))
            for pid, (rev, layout) in results["layouts"]:
                self.assertGreaterEqual(rev, 1)
                self.assertEqual(layout[0], pid)
                self.assertGreater(len(layout[2]), 0)
            self.assertEqual(len(results["pixmaps"]), 3)
        finally:
            tray.close()

    def test_gnome_target_parser_and_fallback(self) -> None:
        self.assertIsNone(GnomeWaylandTracker._parse_target_json('{"found": false}'))
        tw = GnomeWaylandTracker._parse_target_json(
            '{"found": true, "id": "42", "pid": 1234, "x": 10, "y": 20, "width": 1200, "height": 800, "maximized": true, "coveredByOther": false, "fullyCovered": false}'
        )
        self.assertIsNotNone(tw)
        assert tw is not None
        self.assertEqual((tw.window_id, tw.pid, tw.width, tw.height, tw.maximized), ("42", 1234, 1200, 800, True))
        fb = FallbackTracker("test note")
        self.assertEqual(fb.status_note, "test note")
        self.assertFalse(fb.compositor_manages_geometry)
        self.assertTrue(GnomeWaylandTracker.compositor_manages_geometry if hasattr(GnomeWaylandTracker, "compositor_manages_geometry") else False)

    def test_ctypes_input_region(self) -> None:
        if not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
            self.skipTest("no display server")
        import gi

        gi.require_version("Gtk", "4.0")
        from gi.repository import GLib, Gtk

        app = Gtk.Application(application_id="io.github.wynneceles.TestInputRegion", flags=0)
        errors: list[Exception] = []

        def on_act(a: Gtk.Application) -> None:
            win = Gtk.Window(application=a)
            win.set_default_size(200, 200)
            win.present()

            def apply_it() -> bool:
                try:
                    surf = win.get_surface()
                    self.assertIsNotNone(surf)
                    _set_surface_input_region(surf, True, 200, 200)
                    _set_surface_input_region(surf, False, 200, 200)
                except Exception as exc:
                    errors.append(exc)
                finally:
                    a.quit()
                return False

            GLib.timeout_add(150, apply_it)

        app.connect("activate", on_act)
        app.run(None)
        self.assertEqual(errors, [])


class MainArgsAndTrayLifecycleTests(unittest.TestCase):
    def test_parse_args_ignores_injected_flags(self) -> None:
        import main as agba_main

        with tempfile.TemporaryDirectory() as tmp:
            old_state = os.environ.get("XDG_STATE_HOME")
            os.environ["XDG_STATE_HOME"] = tmp
            try:
                args1 = agba_main.parse_args(["--remote-debugging-port=0"])
                self.assertFalse(args1.settings)

                args2 = agba_main.parse_args(["--settings", "--remote-debugging-port=0", "--extra-flag", "val"])
                self.assertTrue(args2.settings)

                log_file = Path(tmp) / "antigravity-boot-animation" / "log.txt"
                self.assertTrue(log_file.is_file())
                self.assertIn("--remote-debugging-port=0", log_file.read_text(encoding="utf-8"))
            finally:
                if old_state is None:
                    os.environ.pop("XDG_STATE_HOME", None)
                else:
                    os.environ["XDG_STATE_HOME"] = old_state

    def test_tray_retry_timer_cleaned_on_close(self) -> None:
        from gi.repository import GLib

        noop = lambda *a, **k: None
        state = {
            "play_intro": True,
            "loop_background": True,
            "residual_alpha": 0.25,
            "intro_volume": 1.0,
            "playing": False,
            "in_intro": False,
            "can_pause": False,
            "paused": False,
        }
        tray = TrayIcon(
            {
                "get_state": lambda: state,
                "toggle_intro": noop,
                "toggle_loop": noop,
                "set_alpha": noop,
                "set_volume": noop,
                "preview_intro": noop,
                "toggle_loop_now": noop,
                "toggle_pause": noop,
                "open_settings": noop,
                "open_config": noop,
                "uninstall": noop,
            }
        )
        try:
            tray._retry_timer_id = GLib.timeout_add_seconds(60, lambda: False)
            self.assertGreater(tray._retry_timer_id, 0)
        finally:
            tray.close()
        self.assertTrue(tray._closed)
        self.assertEqual(tray._retry_timer_id, 0)

    def test_gnome_target_appeared_ignores_own_pid(self) -> None:
        import main as agba_main
        from window_tracker import TargetWindow

        with tempfile.TemporaryDirectory() as tmp:
            old_cfg = os.environ.get("XDG_CONFIG_HOME")
            old_state = os.environ.get("XDG_STATE_HOME")
            os.environ["XDG_CONFIG_HOME"] = tmp
            os.environ["XDG_STATE_HOME"] = tmp
            ctrl = None
            try:
                ctrl = agba_main.Controller(agba_main.parse_args(["--no-tray"]))
                started: list[TargetWindow] = []
                ctrl._start_show = lambda target, **kwargs: started.append(target)
                ctrl._on_gnome_target_appeared(
                    TargetWindow(window_id="999", pid=os.getpid(), x=0, y=0, width=405, height=654)
                )
                self.assertEqual(started, [])
            finally:
                if ctrl is not None:
                    ctrl.tracker.close()
                if old_cfg is None:
                    os.environ.pop("XDG_CONFIG_HOME", None)
                else:
                    os.environ["XDG_CONFIG_HOME"] = old_cfg
                if old_state is None:
                    os.environ.pop("XDG_STATE_HOME", None)
                else:
                    os.environ["XDG_STATE_HOME"] = old_state

        ext_js = (
            Path(__file__).resolve().parent.parent
            / "gnome-extension"
            / "antigravity-boot-animation@wynneceles.github.io"
            / "extension.js"
        ).read_text(encoding="utf-8")
        self.assertIn("antigravitybootanimation", ext_js)
        self.assertIn("wynneceles", ext_js)


if __name__ == "__main__":
    unittest.main()

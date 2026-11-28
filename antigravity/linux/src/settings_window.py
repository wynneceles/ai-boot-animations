# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
from gi.repository import Gdk, Gtk  # noqa: E402

from icon import render_gdk_texture  # noqa: E402

_SETTINGS_CSS = b"""
.agba-header-title {
    font-size: 16px;
    font-weight: 700;
}
.agba-section-title {
    font-size: 12px;
    font-weight: 600;
    opacity: 0.72;
}
.agba-muted {
    font-size: 11px;
    opacity: 0.68;
}
.agba-warn {
    font-size: 11px;
    color: #e6a23c;
}
.agba-mono {
    font-family: monospace;
    font-size: 12px;
}
"""
_SETTINGS_CSS_LOADED = False


def _ensure_settings_css() -> None:
    global _SETTINGS_CSS_LOADED
    if _SETTINGS_CSS_LOADED:
        return
    display = Gdk.Display.get_default()
    if display is None:
        return
    provider = Gtk.CssProvider()
    provider.load_from_data(_SETTINGS_CSS)
    Gtk.StyleContext.add_provider_for_display(display, provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
    _SETTINGS_CSS_LOADED = True


class SettingsWindow:
    """Native GTK4 settings window with live sliders matching macOS & Windows controls."""

    def __init__(self, app_ctrl) -> None:
        _ensure_settings_css()
        self.ctrl = app_ctrl
        self._loading = False

        self.window = Gtk.Window(application=app_ctrl.app)
        self.window.set_title("Gemini 开机动画 · 设置")
        self.window.set_default_size(400, -1)
        self.window.set_resizable(False)
        self.window.set_hide_on_close(True)

        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        box.set_margin_top(18)
        box.set_margin_bottom(18)
        box.set_margin_start(22)
        box.set_margin_end(22)
        self.window.set_child(box)

        # Header with Gemini ✦ icon
        header = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
        icon_pic = Gtk.Picture.new_for_paintable(render_gdk_texture(28))
        icon_pic.set_can_shrink(False)
        icon_pic.set_size_request(28, 28)
        title_lbl = Gtk.Label(label="Gemini 开机动画")
        title_lbl.add_css_class("agba-header-title")
        title_lbl.set_halign(Gtk.Align.START)
        header.append(icon_pic)
        header.append(title_lbl)
        box.append(header)

        sec1 = Gtk.Label(label="打开 Antigravity 时", xalign=0.0)
        sec1.add_css_class("agba-section-title")
        sec1.set_margin_top(4)
        box.append(sec1)

        self.intro_check = Gtk.CheckButton(label="播放开屏动画")
        self.intro_check.connect("toggled", self._on_toggled)
        box.append(self.intro_check)

        self.loop_check = Gtk.CheckButton(label="保留循环背景（半透明，点击穿透）")
        self.loop_check.connect("toggled", self._on_toggled)
        box.append(self.loop_check)

        hint1 = Gtk.Label(label="两个都不勾 = 原装 Antigravity", xalign=0.0)
        hint1.add_css_class("agba-muted")
        box.append(hint1)

        box.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL))

        # Alpha slider (0 - 80%)
        alpha_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        alpha_title = Gtk.Label(label="背景不透明度", xalign=0.0)
        alpha_title.set_hexpand(True)
        self.alpha_val = Gtk.Label(label="25%", xalign=1.0)
        self.alpha_val.add_css_class("agba-mono")
        alpha_row.append(alpha_title)
        alpha_row.append(self.alpha_val)
        box.append(alpha_row)

        self.alpha_scale = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 0.0, 80.0, 1.0)
        self.alpha_scale.set_draw_value(False)
        self.alpha_scale.connect("value-changed", self._on_alpha_changed)
        box.append(self.alpha_scale)

        # Volume slider (0 - 100%)
        vol_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        vol_title = Gtk.Label(label="开屏音量（循环背景静音）", xalign=0.0)
        vol_title.set_hexpand(True)
        self.vol_val = Gtk.Label(label="100%", xalign=1.0)
        self.vol_val.add_css_class("agba-mono")
        vol_row.append(vol_title)
        vol_row.append(self.vol_val)
        box.append(vol_row)

        self.vol_scale = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 0.0, 100.0, 1.0)
        self.vol_scale.set_draw_value(False)
        self.vol_scale.connect("value-changed", self._on_vol_changed)
        box.append(self.vol_scale)

        box.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL))

        sec2 = Gtk.Label(label="现在，在 Antigravity 窗口上", xalign=0.0)
        sec2.add_css_class("agba-section-title")
        box.append(sec2)

        btn_intro = Gtk.Button(label="播放一次开屏动画")
        btn_intro.connect("clicked", lambda _b: self.ctrl.preview_intro())
        box.append(btn_intro)

        row_actions = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        btn_loop = Gtk.Button(label="开启循环背景")
        btn_loop.set_hexpand(True)
        btn_loop.connect("clicked", lambda _b: self.ctrl.preview_loop())
        self.btn_pause = Gtk.Button(label="暂停循环背景")
        self.btn_pause.set_hexpand(True)
        self.btn_pause.connect("clicked", lambda _b: self.ctrl.toggle_pause())
        row_actions.append(btn_loop)
        row_actions.append(self.btn_pause)
        box.append(row_actions)

        btn_stop = Gtk.Button(label="关掉动画 / 背景")
        btn_stop.connect("clicked", lambda _b: self.ctrl.stop_show())
        box.append(btn_stop)

        box.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL))

        footer = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        btn_cfg = Gtk.Button(label="打开设置文件…")
        btn_cfg.set_hexpand(True)
        btn_cfg.connect("clicked", lambda _b: self.ctrl.open_config())
        btn_uninst = Gtk.Button(label="卸载 Antigravity 开机动画…")
        btn_uninst.set_hexpand(True)
        btn_uninst.add_css_class("destructive-action")
        btn_uninst.connect("clicked", lambda _b: self._confirm_uninstall())
        footer.append(btn_cfg)
        footer.append(btn_uninst)
        box.append(footer)

        self.status_label = Gtk.Label(label="", xalign=0.0, wrap=True)
        self.status_label.set_max_width_chars(46)
        self.status_label.add_css_class("agba-muted")
        box.append(self.status_label)

        self.refresh()

    def present(self) -> None:
        self.refresh()
        self.window.present()

    def refresh(self) -> None:
        self._loading = True
        try:
            c = self.ctrl.config
            self.intro_check.set_active(c.play_intro)
            self.loop_check.set_active(c.loop_background)
            a_pct = int(round(c.residual_alpha * 100))
            v_pct = int(round(c.intro_volume * 100))
            self.alpha_scale.set_value(a_pct)
            self.alpha_val.set_text(f"{a_pct}%")
            self.vol_scale.set_value(v_pct)
            self.vol_val.set_text(f"{v_pct}%")

            show = self.ctrl.show
            can_pause = bool(show and show.playing and show.can_pause)
            self.btn_pause.set_sensitive(can_pause)
            self.btn_pause.set_label("继续播放循环背景" if (can_pause and show.paused) else "暂停循环背景")
            self.status_label.set_text(f"窗口追踪：{self.ctrl.tracker.status_note}")
            if self.ctrl.tracker.name == "Wayland Fallback":
                self.status_label.remove_css_class("agba-muted")
                self.status_label.add_css_class("agba-warn")
            else:
                self.status_label.remove_css_class("agba-warn")
                self.status_label.add_css_class("agba-muted")
        finally:
            self._loading = False

    def _on_toggled(self, _btn) -> None:
        if self._loading:
            return
        play_intro = self.intro_check.get_active()
        loop_bg = self.loop_check.get_active()
        self.ctrl.update_config(
            lambda c: (setattr(c, "play_intro", play_intro), setattr(c, "loop_background", loop_bg)),
            source=self,
        )

    def _on_alpha_changed(self, scale: Gtk.Scale) -> None:
        pct = int(round(scale.get_value()))
        self.alpha_val.set_text(f"{pct}%")
        if self._loading:
            return
        self.ctrl.update_config(lambda c: setattr(c, "residual_alpha", pct / 100.0), source=self)

    def _on_vol_changed(self, scale: Gtk.Scale) -> None:
        pct = int(round(scale.get_value()))
        self.vol_val.set_text(f"{pct}%")
        if self._loading:
            return
        self.ctrl.update_config(lambda c: setattr(c, "intro_volume", pct / 100.0), source=self)

    def _confirm_uninstall(self) -> None:
        dialog = Gtk.AlertDialog()
        dialog.set_message("卸载 Antigravity 开机动画？")
        dialog.set_detail("会删除这个小程序、开机自启、设置、缓存和日志。Antigravity 本身没有被改过，卸载后就是原装的样子。")
        dialog.set_buttons(["取消", "卸载"])
        dialog.set_cancel_button(0)
        dialog.set_default_button(0)

        def on_choice(dlg: Gtk.AlertDialog, res) -> None:
            try:
                idx = dlg.choose_finish(res)
                if idx == 1:
                    self.ctrl.uninstall()
            except Exception:
                pass

        dialog.choose(self.window, None, on_choice)

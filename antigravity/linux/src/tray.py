# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import os
from typing import Any, Callable

import gi

gi.require_version("Gio", "2.0")
gi.require_version("GLib", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

from icon import render_sni_pixmaps  # noqa: E402
from logger import log  # noqa: E402

_SNI_XML = """
<node>
  <interface name="org.kde.StatusNotifierItem">
    <property name="Category" type="s" access="read"/>
    <property name="Id" type="s" access="read"/>
    <property name="Title" type="s" access="read"/>
    <property name="Status" type="s" access="read"/>
    <property name="WindowId" type="i" access="read"/>
    <property name="IconThemePath" type="s" access="read"/>
    <property name="Menu" type="o" access="read"/>
    <property name="ItemIsMenu" type="b" access="read"/>
    <property name="IconName" type="s" access="read"/>
    <property name="IconPixmap" type="a(iiay)" access="read"/>
    <property name="AttentionIconName" type="s" access="read"/>
    <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
    <method name="ContextMenu">
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
    </method>
    <method name="Activate">
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
    </method>
    <method name="SecondaryActivate">
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
    </method>
    <method name="Scroll">
      <arg type="i" name="delta" direction="in"/>
      <arg type="s" name="orientation" direction="in"/>
    </method>
    <signal name="NewTitle"/>
    <signal name="NewIcon"/>
    <signal name="NewAttentionIcon"/>
    <signal name="NewOverlayIcon"/>
    <signal name="NewToolTip"/>
    <signal name="NewStatus">
      <arg type="s" name="status"/>
    </signal>
  </interface>
</node>
"""

_MENU_XML = """
<node>
  <interface name="com.canonical.dbusmenu">
    <property name="Version" type="u" access="read"/>
    <property name="TextDirection" type="s" access="read"/>
    <property name="Status" type="s" access="read"/>
    <property name="IconThemePath" type="as" access="read"/>
    <method name="GetLayout">
      <arg type="i" name="parentId" direction="in"/>
      <arg type="i" name="recursionDepth" direction="in"/>
      <arg type="as" name="propertyNames" direction="in"/>
      <arg type="u" name="revision" direction="out"/>
      <arg type="(ia{sv}av)" name="layout" direction="out"/>
    </method>
    <method name="GetGroupProperties">
      <arg type="ai" name="ids" direction="in"/>
      <arg type="as" name="propertyNames" direction="in"/>
      <arg type="a(ia{sv})" name="properties" direction="out"/>
    </method>
    <method name="GetProperty">
      <arg type="i" name="id" direction="in"/>
      <arg type="s" name="name" direction="in"/>
      <arg type="v" name="value" direction="out"/>
    </method>
    <method name="Event">
      <arg type="i" name="id" direction="in"/>
      <arg type="s" name="eventId" direction="in"/>
      <arg type="v" name="data" direction="in"/>
      <arg type="u" name="timestamp" direction="in"/>
    </method>
    <method name="EventGroup">
      <arg type="a(isvu)" name="events" direction="in"/>
      <arg type="ai" name="idErrors" direction="out"/>
    </method>
    <method name="AboutToShow">
      <arg type="i" name="id" direction="in"/>
      <arg type="b" name="needUpdate" direction="out"/>
    </method>
    <method name="AboutToShowGroup">
      <arg type="ai" name="ids" direction="in"/>
      <arg type="ai" name="updatesNeeded" direction="out"/>
      <arg type="ai" name="idErrors" direction="out"/>
    </method>
    <signal name="ItemsPropertiesUpdated">
      <arg type="a(ia{sv})" name="updatedProps"/>
      <arg type="a(ias)" name="removedProps"/>
    </signal>
    <signal name="LayoutUpdated">
      <arg type="u" name="revision"/>
      <arg type="i" name="parent"/>
    </signal>
    <signal name="ItemActivationRequested">
      <arg type="i" name="id"/>
      <arg type="u" name="timestamp"/>
    </signal>
  </interface>
</node>
"""

ALPHA_PRESETS = (0, 10, 15, 25, 35, 50, 65, 80)
VOL_PRESETS = (0, 25, 50, 75, 100)


class TrayIcon:
    """Zero-dependency StatusNotifierItem + DBusMenu system tray implementation."""

    SNI_PATH = "/StatusNotifierItem"
    MENU_PATH = "/MenuBar"

    def __init__(self, callbacks: dict[str, Callable[..., Any]]) -> None:
        self.callbacks = callbacks
        self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        self.bus_name = f"org.kde.StatusNotifierItem-{os.getpid()}-1"
        self.pixmaps = render_sni_pixmaps()
        self.revision = 1
        self._sni_reg_id = 0
        self._menu_reg_id = 0
        self._name_owner_id = 0
        self._watcher_sub_id = 0
        self._retry_timer_id = 0
        self._retry_count = 0
        self._closed = False
        self._items_map: dict[int, tuple[dict[str, GLib.Variant], Callable[[], None] | None]] = {}

        sni_info = Gio.DBusNodeInfo.new_for_xml(_SNI_XML)
        menu_info = Gio.DBusNodeInfo.new_for_xml(_MENU_XML)

        self._sni_reg_id = self.bus.register_object(
            self.SNI_PATH,
            sni_info.interfaces[0],
            self._on_sni_method,
            self._on_sni_get_prop,
            None,
        )
        self._menu_reg_id = self.bus.register_object(
            self.MENU_PATH,
            menu_info.interfaces[0],
            self._on_menu_method,
            self._on_menu_get_prop,
            None,
        )
        self._name_owner_id = Gio.bus_own_name_on_connection(
            self.bus,
            self.bus_name,
            Gio.BusNameOwnerFlags.NONE,
            self._on_name_acquired,
            None,
        )
        self._watcher_sub_id = self.bus.signal_subscribe(
            "org.freedesktop.DBus",
            "org.freedesktop.DBus",
            "NameOwnerChanged",
            "/org/freedesktop/DBus",
            "org.kde.StatusNotifierWatcher",
            Gio.DBusSignalFlags.NONE,
            self._on_watcher_changed,
        )
        self._rebuild_menu_items()

    def _on_name_acquired(self, *_args) -> None:
        self._register_with_watcher()

    def _on_watcher_changed(self, _conn, _sender, _path, _iface, _signal, params) -> None:
        _name, _old_owner, new_owner = params.unpack()
        if new_owner:
            self._retry_count = 0
            if self._retry_timer_id:
                GLib.source_remove(self._retry_timer_id)
                self._retry_timer_id = 0
            self._register_with_watcher()

    def _register_with_watcher(self) -> None:
        if self._closed:
            return
        try:
            self.bus.call(
                "org.kde.StatusNotifierWatcher",
                "/StatusNotifierWatcher",
                "org.kde.StatusNotifierWatcher",
                "RegisterStatusNotifierItem",
                GLib.Variant("(s)", (self.bus_name,)),
                None,
                Gio.DBusCallFlags.NONE,
                5000,
                None,
                self._on_register_done,
            )
        except Exception as exc:
            log(f"托盘注册失败：{exc}")

    def _on_register_done(self, source, res) -> None:
        try:
            source.call_finish(res)
            self._retry_count = 0
            if self._retry_timer_id:
                GLib.source_remove(self._retry_timer_id)
                self._retry_timer_id = 0
        except Exception as exc:
            log(f"StatusNotifierWatcher 注册异常（如果没有安装托盘扩展可忽略）：{exc}")
            if not self._closed and self._retry_count < 5 and not self._retry_timer_id:
                self._retry_count += 1
                self._retry_timer_id = GLib.timeout_add_seconds(2, self._retry_register)

    def _retry_register(self) -> bool:
        self._retry_timer_id = 0
        if not self._closed:
            self._register_with_watcher()
        return GLib.SOURCE_REMOVE

    def refresh(self) -> None:
        self.revision += 1
        self._rebuild_menu_items()
        try:
            self.bus.emit_signal(
                None,
                self.MENU_PATH,
                "com.canonical.dbusmenu",
                "LayoutUpdated",
                GLib.Variant("(ui)", (self.revision, 0)),
            )
        except Exception:
            pass

    def _rebuild_menu_items(self) -> None:
        st = self.callbacks["get_state"]()
        alpha_pct = int(round(st["residual_alpha"] * 100))
        vol_pct = int(round(st["intro_volume"] * 100))

        self._items_map = {}

        def v_str(s: str) -> GLib.Variant:
            return GLib.Variant("s", s)

        def v_bool(b: bool) -> GLib.Variant:
            return GLib.Variant("b", b)

        def v_int(i: int) -> GLib.Variant:
            return GLib.Variant("i", i)

        def make_leaf(
            item_id: int,
            label: str,
            cb: Callable[[], None] | None = None,
            enabled: bool = True,
            check: bool | None = None,
            radio: bool | None = None,
        ) -> GLib.Variant:
            props: dict[str, GLib.Variant] = {"label": v_str(label), "enabled": v_bool(enabled)}
            if check is not None:
                props["toggle-type"] = v_str("checkmark")
                props["toggle-state"] = v_int(1 if check else 0)
            elif radio is not None:
                props["toggle-type"] = v_str("radio")
                props["toggle-state"] = v_int(1 if radio else 0)
            self._items_map[item_id] = (props, cb)
            return GLib.Variant("(ia{sv}av)", (item_id, props, []))

        def make_sep(item_id: int) -> GLib.Variant:
            props = {"type": v_str("separator")}
            self._items_map[item_id] = (props, None)
            return GLib.Variant("(ia{sv}av)", (item_id, props, []))

        def make_submenu(item_id: int, label: str, children: list[GLib.Variant]) -> GLib.Variant:
            props = {"label": v_str(label), "children-display": v_str("submenu"), "enabled": v_bool(True)}
            self._items_map[item_id] = (props, None)
            return GLib.Variant("(ia{sv}av)", (item_id, props, children))

        alpha_children = [
            make_leaf(
                100 + idx,
                f"{pct}%",
                cb=(lambda p=pct: self.callbacks["set_alpha"](p / 100.0)),
                radio=(abs(alpha_pct - pct) <= 4),
            )
            for idx, pct in enumerate(ALPHA_PRESETS)
        ]
        vol_children = [
            make_leaf(
                200 + idx,
                f"{pct}%",
                cb=(lambda p=pct: self.callbacks["set_volume"](p / 100.0)),
                radio=(abs(vol_pct - pct) <= 12),
            )
            for idx, pct in enumerate(VOL_PRESETS)
        ]

        playing = st["playing"]
        in_intro = st["in_intro"]
        can_pause = st["can_pause"]
        paused = st["paused"]
        loop_now_label = "开启循环背景" if not playing else ("关掉开屏动画" if in_intro else "关掉循环背景")
        pause_label = "继续播放循环背景" if (playing and paused) else "暂停循环背景"

        top_children = [
            make_leaf(1, "打开 Antigravity 时", enabled=False),
            make_leaf(2, "播放开屏动画", cb=self.callbacks["toggle_intro"], check=st["play_intro"]),
            make_leaf(3, "保留循环背景", cb=self.callbacks["toggle_loop"], check=st["loop_background"]),
            make_sep(4),
            make_submenu(5, f"背景不透明度  {alpha_pct}%", alpha_children),
            make_submenu(6, f"开屏音量  {vol_pct}%", vol_children),
            make_sep(7),
            make_leaf(8, "现在，在 Antigravity 窗口上", enabled=False),
            make_leaf(9, "播放一次开屏动画", cb=self.callbacks["preview_intro"]),
            make_leaf(10, loop_now_label, cb=self.callbacks["toggle_loop_now"]),
            make_leaf(11, pause_label, cb=self.callbacks["toggle_pause"], enabled=bool(playing and can_pause)),
            make_sep(12),
            make_leaf(13, "设置窗口…", cb=self.callbacks["open_settings"]),
            make_leaf(14, "打开设置文件…", cb=self.callbacks["open_config"]),
            make_leaf(15, "卸载 Antigravity 开机动画…", cb=self.callbacks["uninstall"]),
        ]
        root_props = {"children-display": v_str("submenu")}
        self._items_map[0] = (root_props, None)
        self._root_layout = (0, root_props, top_children)

    def _on_sni_get_prop(self, _conn, _sender, _path, _iface, prop_name: str) -> GLib.Variant:
        if prop_name == "Category":
            return GLib.Variant("s", "ApplicationStatus")
        if prop_name == "Id":
            return GLib.Variant("s", "antigravity-boot-animation")
        if prop_name == "Title":
            return GLib.Variant("s", "Gemini 开机动画")
        if prop_name == "Status":
            return GLib.Variant("s", "Active")
        if prop_name == "WindowId":
            return GLib.Variant("i", 0)
        if prop_name == "IconThemePath":
            return GLib.Variant("s", "")
        if prop_name == "Menu":
            return GLib.Variant("o", self.MENU_PATH)
        if prop_name == "ItemIsMenu":
            return GLib.Variant("b", False)
        if prop_name == "IconName":
            return GLib.Variant("s", "")
        if prop_name == "IconPixmap":
            return GLib.Variant("a(iiay)", self.pixmaps)
        if prop_name == "AttentionIconName":
            return GLib.Variant("s", "")
        if prop_name == "ToolTip":
            return GLib.Variant("(sa(iiay)ss)", ("", [], "Gemini 开机动画", "Antigravity 开机动画与循环背景"))
        return GLib.Variant("s", "")

    def _on_sni_method(self, _conn, _sender, _path, _iface, method: str, _params, invocation) -> None:
        if method in ("Activate", "SecondaryActivate"):
            GLib.idle_add(self.callbacks["open_settings"])
        invocation.return_value(None)

    def _on_menu_get_prop(self, _conn, _sender, _path, _iface, prop_name: str) -> GLib.Variant:
        if prop_name == "Version":
            return GLib.Variant("u", 3)
        if prop_name == "TextDirection":
            return GLib.Variant("s", "ltr")
        if prop_name == "Status":
            return GLib.Variant("s", "normal")
        if prop_name == "IconThemePath":
            return GLib.Variant("as", [])
        return GLib.Variant("s", "")

    def _on_menu_method(self, _conn, _sender, _path, _iface, method: str, params, invocation) -> None:
        if method == "GetLayout":
            parent_id, _depth, _prop_names = params.unpack()
            self._rebuild_menu_items()
            if parent_id == 0:
                invocation.return_value(GLib.Variant("(u(ia{sv}av))", (self.revision, self._root_layout)))
            else:
                props, _ = self._items_map.get(parent_id, ({}, None))
                children = []
                if parent_id == 5:
                    av = self._root_layout[2][4].get_child_value(2)
                    children = [av.get_child_value(i).get_variant() for i in range(av.n_children())]
                elif parent_id == 6:
                    av = self._root_layout[2][5].get_child_value(2)
                    children = [av.get_child_value(i).get_variant() for i in range(av.n_children())]
                invocation.return_value(
                    GLib.Variant("(u(ia{sv}av))", (self.revision, (parent_id, props, children)))
                )
            return

        if method == "GetGroupProperties":
            ids, prop_names = params.unpack()
            out = []
            for item_id in ids:
                props, _ = self._items_map.get(item_id, ({}, None))
                if prop_names:
                    filtered = {k: v for k, v in props.items() if k in prop_names}
                else:
                    filtered = props
                out.append((item_id, filtered))
            invocation.return_value(GLib.Variant("(a(ia{sv}))", (out,)))
            return

        if method == "GetProperty":
            item_id, name = params.unpack()
            props, _ = self._items_map.get(item_id, ({}, None))
            val = props.get(name, GLib.Variant("s", ""))
            invocation.return_value(GLib.Variant("(v)", (val,)))
            return

        if method == "Event":
            item_id, event_id, _data, _ts = params.unpack()
            if event_id == "clicked":
                _, cb = self._items_map.get(item_id, ({}, None))
                if cb is not None:
                    GLib.idle_add(cb)
            invocation.return_value(None)
            return

        if method == "EventGroup":
            events = params.unpack()[0]
            for item_id, event_id, _data, _ts in events:
                if event_id == "clicked":
                    _, cb = self._items_map.get(item_id, ({}, None))
                    if cb is not None:
                        GLib.idle_add(cb)
            invocation.return_value(GLib.Variant("(ai)", ([],)))
            return

        if method == "AboutToShow":
            self._rebuild_menu_items()
            invocation.return_value(GLib.Variant("(b)", (True,)))
            return

        if method == "AboutToShowGroup":
            ids = params.unpack()[0]
            self._rebuild_menu_items()
            invocation.return_value(GLib.Variant("(aiai)", (list(ids), [])))
            return

        invocation.return_value(None)

    def close(self) -> None:
        self._closed = True
        if self._retry_timer_id:
            GLib.source_remove(self._retry_timer_id)
            self._retry_timer_id = 0
        if self._watcher_sub_id:
            self.bus.signal_unsubscribe(self._watcher_sub_id)
            self._watcher_sub_id = 0
        if self._sni_reg_id:
            self.bus.unregister_object(self._sni_reg_id)
            self._sni_reg_id = 0
        if self._menu_reg_id:
            self.bus.unregister_object(self._menu_reg_id)
            self._menu_reg_id = 0
        if self._name_owner_id:
            Gio.bus_unown_name(self._name_owner_id)
            self._name_owner_id = 0

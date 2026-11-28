// GNOME Shell 45–50 companion extension for Antigravity Boot Animation (Linux Wayland).
// Tracks Antigravity window geometry & stacking without modifying Antigravity itself.
// Copyright (C) 2026 wynneceles
// Licensed under the GNU General Public License v3.

import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Meta from 'gi://Meta';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const BUS_NAME = 'io.github.wynneceles.AntigravityBootAnimation.GnomeHelper';
const OBJ_PATH = '/io/github/wynneceles/AntigravityBootAnimation/GnomeHelper';
const OVERLAY_DEFAULT_TITLE = 'AntigravityBootAnimationOverlay';

const IFACE_XML = `
<node>
  <interface name="${BUS_NAME}">
    <method name="Ping">
      <arg type="b" name="ok" direction="out"/>
    </method>
    <method name="GetTarget">
      <arg type="i" name="pid" direction="in"/>
      <arg type="s" name="titleHint" direction="in"/>
      <arg type="s" name="jsonResult" direction="out"/>
    </method>
    <method name="AttachOverlay">
      <arg type="i" name="overlayPid" direction="in"/>
      <arg type="s" name="overlayTitle" direction="in"/>
      <arg type="i" name="targetPid" direction="in"/>
      <arg type="s" name="titleHint" direction="in"/>
    </method>
    <method name="SyncOverlay">
      <arg type="s" name="overlayTitle" direction="in"/>
      <arg type="i" name="targetPid" direction="in"/>
      <arg type="s" name="titleHint" direction="in"/>
      <arg type="b" name="visible" direction="in"/>
    </method>
    <method name="DetachOverlay">
      <arg type="s" name="overlayTitle" direction="in"/>
    </method>
    <method name="GetOverlayState">
      <arg type="s" name="jsonResult" direction="out"/>
    </method>
    <signal name="TargetAppeared">
      <arg type="s" name="jsonResult"/>
    </signal>
  </interface>
</node>
`;

function isMaximized(win) {
    if (!win)
        return false;
    if (typeof win.is_maximized === 'function')
        return win.is_maximized();
    if (typeof win.get_maximized === 'function')
        return win.get_maximized() !== 0;
    return Boolean(win.maximized_horizontally && win.maximized_vertically);
}

function rectsIntersect(a, b) {
    return a.x < b.x + b.width &&
        a.x + a.width > b.x &&
        a.y < b.y + b.height &&
        a.y + a.height > b.y;
}

function rectContains(outer, inner) {
    return outer.x <= inner.x &&
        outer.y <= inner.y &&
        outer.x + outer.width >= inner.x + inner.width &&
        outer.y + outer.height >= inner.y + inner.height;
}

export default class AntigravityBootHelperExtension extends Extension {
    enable() {
        this._dbusImpl = null;
        this._busOwnerId = 0;
        this._displaySignals = [];
        this._wmSignals = [];
        this._overviewSignals = [];
        this._targetSignals = [];
        this._overlaySignals = [];
        this._trackedTarget = null;
        this._trackedOverlay = null;
        this._overlayPid = 0;
        this._overlayTitle = OVERLAY_DEFAULT_TITLE;
        this._targetPid = 0;
        this._titleHint = '';
        this._desiredVisible = false;
        this._syncIdleId = 0;
        this._syncing = false;
        this._origIsSkipTaskbar = null;

        this._installSkipTaskbarPatch();
        this._exportDbus();
        this._connectGlobalSignals();
    }

    disable() {
        if (this._syncIdleId) {
            GLib.source_remove(this._syncIdleId);
            this._syncIdleId = 0;
        }
        this._disconnectTarget();
        this._disconnectOverlay();

        for (const id of this._displaySignals)
            global.display.disconnect(id);
        this._displaySignals = [];

        for (const id of this._wmSignals)
            global.window_manager.disconnect(id);
        this._wmSignals = [];

        for (const id of this._overviewSignals)
            Main.overview.disconnect(id);
        this._overviewSignals = [];

        this._restoreSkipTaskbarPatch();

        if (this._dbusImpl) {
            this._dbusImpl.unexport();
            this._dbusImpl = null;
        }
        if (this._busOwnerId) {
            Gio.bus_unown_name(this._busOwnerId);
            this._busOwnerId = 0;
        }
    }

    _installSkipTaskbarPatch() {
        if (this._origIsSkipTaskbar)
            return;
        const orig = Meta.Window.prototype.is_skip_taskbar;
        if (typeof orig !== 'function')
            return;
        this._origIsSkipTaskbar = orig;
        Meta.Window.prototype.is_skip_taskbar = function () {
            if (this._agbaSkipTaskbar)
                return true;
            return orig.call(this);
        };
    }

    _restoreSkipTaskbarPatch() {
        if (this._origIsSkipTaskbar) {
            Meta.Window.prototype.is_skip_taskbar = this._origIsSkipTaskbar;
            this._origIsSkipTaskbar = null;
        }
        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window?.();
            if (win && win._agbaSkipTaskbar)
                delete win._agbaSkipTaskbar;
        }
    }

    _exportDbus() {
        this._dbusImpl = Gio.DBusExportedObject.wrapJSObject(IFACE_XML, this);
        this._busOwnerId = Gio.bus_own_name(
            Gio.BusType.SESSION,
            BUS_NAME,
            Gio.BusNameOwnerFlags.NONE,
            connection => {
                if (this._dbusImpl)
                    this._dbusImpl.export(connection, OBJ_PATH);
            },
            null,
            null
        );
    }

    _connectGlobalSignals() {
        this._displaySignals.push(
            global.display.connect('window-created', (_disp, win) => {
                if (win && this._isAntigravityWindow(win))
                    this._emitTargetAppeared(win);
                this._scheduleSync();
            }),
            global.display.connect('restacked', () => this._scheduleSync()),
            global.display.connect('notify::focus-window', () => this._scheduleSync())
        );
        this._wmSignals.push(
            global.window_manager.connect_after('map', (_wm, actor) => {
                const win = actor?.get_meta_window?.();
                if (!win)
                    return;
                const title = win.get_title() || '';
                if (title === this._overlayTitle) {
                    this._attachOverlayWindow(win);
                    this._syncOverlayNow();
                    return;
                }
                if (this._isAntigravityWindow(win)) {
                    this._emitTargetAppeared(win);
                    this._scheduleSync();
                }
            })
        );
        this._overviewSignals.push(
            Main.overview.connect('showing', () => this._syncOverlayNow()),
            Main.overview.connect('hiding', () => this._syncOverlayNow())
        );
    }

    _emitTargetAppeared(win) {
        if (!this._dbusImpl || !this._isNormalAppWindow(win))
            return;
        const rect = win.get_frame_rect();
        if (rect.width < 200 || rect.height < 160)
            return;
        const payload = JSON.stringify({
            found: true,
            id: String(win.get_id()),
            pid: win.get_pid(),
            x: rect.x,
            y: rect.y,
            width: rect.width,
            height: rect.height,
            maximized: isMaximized(win),
            visible: true,
            coveredByOther: false,
            fullyCovered: false,
        });
        try {
            this._dbusImpl.emit_signal('TargetAppeared', GLib.Variant.new('(s)', [payload]));
        } catch (_e) {
        }
    }

    _scheduleSync() {
        if (this._syncIdleId)
            return;
        this._syncIdleId = GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            this._syncIdleId = 0;
            this._syncOverlayNow();
            return GLib.SOURCE_REMOVE;
        });
    }

    _disconnectTarget() {
        if (this._trackedTarget) {
            for (const id of this._targetSignals)
                this._trackedTarget.disconnect(id);
            this._trackedTarget = null;
        }
        this._targetSignals = [];
    }

    _disconnectOverlay() {
        if (this._trackedOverlay) {
            for (const id of this._overlaySignals)
                this._trackedOverlay.disconnect(id);
            if (this._trackedOverlay._agbaSkipTaskbar)
                delete this._trackedOverlay._agbaSkipTaskbar;
            try {
                this._trackedOverlay.unmake_above();
            } catch (_e) {
            }
            this._trackedOverlay = null;
        }
        this._overlaySignals = [];
    }

    _isNormalAppWindow(win) {
        if (!win)
            return false;
        const wt = win.get_window_type();
        return wt === Meta.WindowType.NORMAL || wt === Meta.WindowType.DIALOG;
    }

    _isOwnAppWindow(win) {
        if (!win)
            return false;
        const title = win.get_title() || '';
        if (title === this._overlayTitle)
            return true;
        const wmClass = (win.get_wm_class() || '').toLowerCase();
        if (wmClass.includes('boot-animation') ||
            wmClass.includes('antigravitybootanimation') ||
            wmClass.includes('wynneceles'))
            return true;
        if (this._overlayPid > 0 && win.get_pid() === this._overlayPid)
            return true;
        return false;
    }

    _isAntigravityWindow(win) {
        if (!win || !this._isNormalAppWindow(win) || this._isOwnAppWindow(win))
            return false;
        const wmClass = (win.get_wm_class() || '').toLowerCase();
        return wmClass.includes('antigravity');
    }

    _findOverlayWindow() {
        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window?.();
            if (!win)
                continue;
            const title = win.get_title() || '';
            if (title === this._overlayTitle && (!this._overlayPid || win.get_pid() === this._overlayPid))
                return win;
        }
        return null;
    }

    _findTargetInfo(pid, titleHint) {
        const actors = global.get_window_actors();
        const activeWs = global.workspace_manager.get_active_workspace();
        let best = null;
        let bestArea = 0;
        let bestIdx = -1;

        for (let i = 0; i < actors.length; i++) {
            const actor = actors[i];
            const win = actor.get_meta_window?.();
            if (!win || !this._isNormalAppWindow(win))
                continue;
            const title = win.get_title() || '';
            if (title === this._overlayTitle)
                continue;
            if (win.minimized || !actor.visible)
                continue;
            if (!win.located_on_workspace(activeWs))
                continue;

            if (titleHint) {
                if (!title.toLowerCase().includes(titleHint.toLowerCase()))
                    continue;
            } else {
                if (this._isOwnAppWindow(win))
                    continue;
                const wmClass = (win.get_wm_class() || '').toLowerCase();
                const wpid = win.get_pid();
                const isAg = wmClass.includes('antigravity');
                if (pid > 0) {
                    if (wpid !== pid && !isAg)
                        continue;
                } else if (!isAg) {
                    continue;
                }
            }

            const rect = win.get_frame_rect();
            if (rect.width < 200 || rect.height < 160)
                continue;
            const area = rect.width * rect.height;
            if (area > bestArea) {
                bestArea = area;
                best = { win, actor, rect, wid: String(win.get_id()) };
                bestIdx = i;
            }
        }

        if (!best)
            return null;

        let coveredByOther = false;
        let fullyCovered = false;
        const targetPid = best.win.get_pid();

        for (let i = bestIdx + 1; i < actors.length; i++) {
            const actor = actors[i];
            const win = actor.get_meta_window?.();
            if (!win || !actor.visible || win.minimized)
                continue;
            if (!this._isNormalAppWindow(win))
                continue;
            const title = win.get_title() || '';
            if (title === this._overlayTitle)
                continue;
            if (win.is_skip_taskbar())
                continue;
            if (win.get_pid() === targetPid)
                continue;
            if (!win.located_on_workspace(activeWs))
                continue;
            const oRect = win.get_frame_rect();
            if (rectsIntersect(best.rect, oRect)) {
                coveredByOther = true;
                if (rectContains(oRect, best.rect))
                    fullyCovered = true;
            }
        }

        return {
            ...best,
            coveredByOther,
            fullyCovered,
            maximized: isMaximized(best.win),
        };
    }

    _attachTargetWindow(win) {
        if (this._trackedTarget === win)
            return;
        this._disconnectTarget();
        this._trackedTarget = win;
        this._targetSignals.push(
            win.connect('position-changed', () => this._syncOverlayNow()),
            win.connect('size-changed', () => this._syncOverlayNow()),
            win.connect('notify::minimized', () => this._syncOverlayNow()),
            win.connect('workspace-changed', () => this._syncOverlayNow()),
            win.connect('raised', () => this._syncOverlayNow()),
            win.connect('unmanaged', () => {
                this._disconnectTarget();
                this._scheduleSync();
            })
        );
    }

    _attachOverlayWindow(win) {
        if (this._trackedOverlay === win)
            return;
        this._disconnectOverlay();
        this._trackedOverlay = win;
        win._agbaSkipTaskbar = true;
        this._overlaySignals.push(
            win.connect('position-changed', () => {
                if (!this._syncing)
                    this._scheduleSync();
            }),
            win.connect('size-changed', () => {
                if (!this._syncing)
                    this._scheduleSync();
            }),
            win.connect('unmanaged', () => {
                this._disconnectOverlay();
            })
        );
    }

    _syncOverlayNow() {
        if (this._syncing)
            return;
        this._syncing = true;
        try {
            const overlayWin = this._trackedOverlay || this._findOverlayWindow();
            if (overlayWin)
                this._attachOverlayWindow(overlayWin);
            if (!overlayWin)
                return;

            const info = this._findTargetInfo(this._targetPid, this._titleHint);
            const overlayActor = overlayWin.get_compositor_private?.();

            if (!info || !this._desiredVisible || Main.overview.visible) {
                if (overlayActor)
                    overlayActor.hide();
                if (overlayWin.above)
                    overlayWin.unmake_above();
                return;
            }

            this._attachTargetWindow(info.win);

            const targetWs = info.win.get_workspace();
            if (targetWs && !overlayWin.located_on_workspace(targetWs))
                overlayWin.change_workspace(targetWs);

            if (isMaximized(overlayWin) && !info.maximized)
                overlayWin.unmaximize();

            const oRect = overlayWin.get_frame_rect();
            if (oRect.x !== info.rect.x || oRect.y !== info.rect.y ||
                oRect.width !== info.rect.width || oRect.height !== info.rect.height) {
                overlayWin.move_resize_frame(true, info.rect.x, info.rect.y, info.rect.width, info.rect.height);
            }

            const targetFocused = info.win.has_focus();
            const keepAbove = targetFocused || !info.coveredByOther;

            if (!keepAbove && info.fullyCovered) {
                if (overlayActor)
                    overlayActor.hide();
                if (overlayWin.above)
                    overlayWin.unmake_above();
                return;
            }

            if (overlayActor && !overlayActor.visible)
                overlayActor.show();

            const parent = overlayActor?.get_parent?.();
            if (keepAbove) {
                if (!overlayWin.above)
                    overlayWin.make_above();
                if (parent && info.actor?.get_parent?.() === parent)
                    parent.set_child_above_sibling(overlayActor, info.actor);
            } else {
                if (overlayWin.above)
                    overlayWin.unmake_above();
                if (parent && info.actor?.get_parent?.() === parent)
                    parent.set_child_above_sibling(overlayActor, info.actor);
            }
        } catch (_e) {
        } finally {
            this._syncing = false;
        }
    }

    Ping() {
        return true;
    }

    GetTarget(pid, titleHint) {
        const info = this._findTargetInfo(pid, titleHint);
        if (!info)
            return JSON.stringify({ found: false });
        this._attachTargetWindow(info.win);
        return JSON.stringify({
            found: true,
            id: info.wid,
            pid: info.win.get_pid(),
            x: info.rect.x,
            y: info.rect.y,
            width: info.rect.width,
            height: info.rect.height,
            maximized: info.maximized,
            visible: true,
            coveredByOther: info.coveredByOther,
            fullyCovered: info.fullyCovered,
        });
    }

    AttachOverlay(overlayPid, overlayTitle, targetPid, titleHint) {
        this._overlayPid = overlayPid;
        this._overlayTitle = overlayTitle || OVERLAY_DEFAULT_TITLE;
        this._targetPid = targetPid || 0;
        this._titleHint = titleHint || '';
        this._desiredVisible = true;
        this._syncOverlayNow();
    }

    SyncOverlay(overlayTitle, targetPid, titleHint, visible) {
        if (overlayTitle)
            this._overlayTitle = overlayTitle;
        if (targetPid !== undefined)
            this._targetPid = targetPid || 0;
        if (titleHint !== undefined)
            this._titleHint = titleHint || '';
        this._desiredVisible = Boolean(visible);
        this._syncOverlayNow();
    }

    DetachOverlay(overlayTitle) {
        if (overlayTitle && overlayTitle !== this._overlayTitle)
            return;
        this._targetPid = 0;
        this._titleHint = '';
        this._desiredVisible = false;
        this._disconnectTarget();
        this._disconnectOverlay();
    }

    GetOverlayState() {
        const overlayWin = this._trackedOverlay || this._findOverlayWindow();
        const info = this._findTargetInfo(this._targetPid, this._titleHint);
        const actors = global.get_window_actors();
        let targetActorIdx = -1;
        let overlayActorIdx = -1;
        for (let i = 0; i < actors.length; i++) {
            const w = actors[i].get_meta_window?.();
            if (!w)
                continue;
            if (info && w === info.win)
                targetActorIdx = i;
            if (overlayWin && w === overlayWin)
                overlayActorIdx = i;
        }
        const oRect = overlayWin ? overlayWin.get_frame_rect() : null;
        const overlayActor = overlayWin?.get_compositor_private?.();
        return JSON.stringify({
            attached: Boolean(overlayWin),
            targetFound: Boolean(info),
            target: info ? {
                id: info.wid,
                pid: info.win.get_pid(),
                x: info.rect.x,
                y: info.rect.y,
                width: info.rect.width,
                height: info.rect.height,
                maximized: info.maximized,
                focused: info.win.has_focus(),
                coveredByOther: info.coveredByOther,
                fullyCovered: info.fullyCovered,
            } : null,
            overlay: overlayWin && oRect ? {
                x: oRect.x,
                y: oRect.y,
                width: oRect.width,
                height: oRect.height,
                above: Boolean(overlayWin.above),
                maximized: isMaximized(overlayWin),
                visible: Boolean(overlayActor?.visible),
            } : null,
            overlayAboveTarget: overlayActorIdx > targetActorIdx && targetActorIdx >= 0,
        });
    }
}

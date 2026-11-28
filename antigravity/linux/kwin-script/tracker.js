// KDE KWin 5/6 dynamic window tracking script for Antigravity Boot Animation.
// Copyright (C) 2026 wynneceles
// Licensed under the GNU General Public License v3.

(function () {
    const OVERLAY_TITLE = "AntigravityBootAnimationOverlay";
    const BUS_NAME = "io.github.wynneceles.AntigravityBootAnimation.KWinBridge";
    const OBJ_PATH = "/io/github/wynneceles/AntigravityBootAnimation/KWinBridge";
    const IFACE = BUS_NAME;

    function getWindows() {
        if (typeof workspace.windowList === "function")
            return workspace.windowList();
        if (typeof workspace.clientList === "function")
            return workspace.clientList();
        return [];
    }

    function sync() {
        const wins = getWindows();
        let target = null;
        let overlay = null;
        let bestArea = 0;

        for (let i = 0; i < wins.length; i++) {
            const w = wins[i];
            const caption = (w.caption || "").toString();
            if (caption === OVERLAY_TITLE) {
                overlay = w;
                continue;
            }
            if (w.minimized || w.deleted)
                continue;
            const rc = (w.resourceClass || "").toString().toLowerCase();
            if (rc.indexOf("boot-animation") !== -1 ||
                rc.indexOf("antigravitybootanimation") !== -1 ||
                rc.indexOf("wynneceles") !== -1)
                continue;
            if (rc.indexOf("antigravity") === -1)
                continue;
            const g = w.frameGeometry;
            if (!g || g.width < 300 || g.height < 200)
                continue;
            const area = g.width * g.height;
            if (area > bestArea) {
                bestArea = area;
                target = w;
            }
        }

        if (overlay) {
            overlay.skipTaskbar = true;
            overlay.skipPager = true;
            overlay.skipSwitcher = true;
            if (target) {
                overlay.frameGeometry = Object.assign({}, target.frameGeometry);
                overlay.keepAbove = true;
            }
        }

        const payload = target ? {
            found: true,
            id: String(target.internalId || ""),
            pid: Number(target.pid || 0),
            x: Math.round(target.frameGeometry.x),
            y: Math.round(target.frameGeometry.y),
            width: Math.round(target.frameGeometry.width),
            height: Math.round(target.frameGeometry.height),
            maximized: false,
            visible: true,
            coveredByOther: false,
            fullyCovered: false
        } : { found: false };

        try {
            callDBus(BUS_NAME, OBJ_PATH, IFACE, "ReportTarget", JSON.stringify(payload));
        } catch (_e) {
        }
    }

    workspace.windowAdded && workspace.windowAdded.connect(sync);
    workspace.clientAdded && workspace.clientAdded.connect(sync);
    workspace.windowActivated && workspace.windowActivated.connect(sync);
    workspace.clientActivated && workspace.clientActivated.connect(sync);
    sync();
})();

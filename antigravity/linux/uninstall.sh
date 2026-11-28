#!/usr/bin/env bash
# 彻底卸载 Antigravity 开机动画（Linux 版）。Antigravity 本体从来没被改过。
# Copyright (C) 2026 wynneceles
# Licensed under the GNU General Public License v3 (see repository LICENSE).

set -euo pipefail

FROM_APP=0
if [ "${1:-}" = "--from-app" ]; then
    FROM_APP=1
fi

if [ "$FROM_APP" -eq 0 ] && command -v gdbus >/dev/null 2>&1; then
    gdbus call --session \
        --dest io.github.wynneceles.AntigravityBootAnimation \
        --object-path /io/github/wynneceles/AntigravityBootAnimation/Control \
        --method io.github.wynneceles.AntigravityBootAnimation.Quit >/dev/null 2>&1 || true
fi
if command -v systemctl >/dev/null 2>&1; then
    systemctl --user stop antigravity-boot-animation.service >/dev/null 2>&1 || true
fi
sleep 0.3
pkill -f 'antigravity-boot-animation/src/main.py' 2>/dev/null || true

XDG_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
XDG_CACHE_HOME="${XDG_CACHE_HOME:-$HOME/.cache}"
XDG_STATE_HOME="${XDG_STATE_HOME:-$HOME/.local/state}"
EXT_UUID="antigravity-boot-animation@wynneceles.github.io"

find_system_tool() {
    local name="$1"
    for cand in "/usr/bin/$name" "/usr/local/bin/$name" "/bin/$name" "$(command -v "$name" 2>/dev/null || true)"; do
        [ -n "$cand" ] && [ -x "$cand" ] && { printf '%s\n' "$cand"; return 0; }
    done
    return 1
}

GNOME_EXT_BIN=$(find_system_tool gnome-extensions || true)
GSETTINGS_BIN=$(find_system_tool gsettings || true)
if [ -n "$GNOME_EXT_BIN" ]; then
    env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GNOME_EXT_BIN" disable "$EXT_UUID" >/dev/null 2>&1 || true
fi
if [ -n "$GSETTINGS_BIN" ]; then
    CUR_EXTS=$(env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" get org.gnome.shell enabled-extensions 2>/dev/null || echo "@as []")
    NEW_EXTS=$(/usr/bin/python3 -c "
import ast, sys
raw = sys.argv[1].strip()
if raw.startswith('@as '):
    raw = raw[4:]
try:
    items = [x for x in ast.literal_eval(raw) if x != sys.argv[2]]
except Exception:
    items = []
print('@as []' if not items else '[' + ', '.join(repr(x) for x in items) + ']')
" "$CUR_EXTS" "$EXT_UUID" 2>/dev/null || true)
    if [ -n "$NEW_EXTS" ]; then
        env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" set org.gnome.shell enabled-extensions "$NEW_EXTS" >/dev/null 2>&1 || true
    fi
fi

rm -f "$XDG_CONFIG_HOME/autostart/antigravity-boot-animation.desktop"
rm -f "$XDG_DATA_HOME/applications/antigravity-boot-animation.desktop"
rm -f "$HOME/.local/bin/antigravity-boot-animation"
rm -f "$XDG_DATA_HOME/icons/hicolor/128x128/apps/antigravity-boot-animation.png"
rm -rf "$XDG_DATA_HOME/gnome-shell/extensions/$EXT_UUID"
rm -rf "$XDG_DATA_HOME/antigravity-boot-animation"
rm -rf "$XDG_CONFIG_HOME/antigravity-boot-animation"
rm -rf "$XDG_CACHE_HOME/antigravity-boot-animation"
rm -rf "$XDG_STATE_HOME/antigravity-boot-animation"

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$XDG_DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

if [ "$FROM_APP" -eq 0 ]; then
    echo "已卸载干净。"
fi

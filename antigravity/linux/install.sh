#!/usr/bin/env bash
# Antigravity 开机动画（Gemini 芥末泥双剑）· Linux 版安装脚本
# 用法：./install.sh [开屏.mp4] [循环.mp4]
# Copyright (C) 2026 wynneceles
# Licensed under the GNU General Public License v3 (see repository LICENSE).

set -euo pipefail

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
NO_LAUNCH=0
ARGS=()
for arg in "$@"; do
    if [ "$arg" = "--no-launch" ]; then
        NO_LAUNCH=1
    else
        ARGS+=("$arg")
    fi
done

INTRO_SRC="${ARGS[0]:-}"
LOOP_SRC="${ARGS[1]:-}"
if [ -z "$INTRO_SRC" ]; then
    if [ -f "$SCRIPT_DIR/assets/intro.mp4" ]; then
        INTRO_SRC="$SCRIPT_DIR/assets/intro.mp4"
    else
        INTRO_SRC="$SCRIPT_DIR/../assets/intro.mp4"
    fi
fi
if [ -z "$LOOP_SRC" ]; then
    if [ -f "$SCRIPT_DIR/assets/loop.mp4" ]; then
        LOOP_SRC="$SCRIPT_DIR/assets/loop.mp4"
    else
        LOOP_SRC="$SCRIPT_DIR/../assets/loop.mp4"
    fi
fi

[ -f "$INTRO_SRC" ] || { echo "找不到开屏视频：$INTRO_SRC" >&2; exit 1; }
[ -f "$LOOP_SRC" ] || { echo "找不到循环视频：$LOOP_SRC" >&2; exit 1; }

FONT_SRC=""
for cand in "$SCRIPT_DIR/assets/LibreBodoni-Italic.ttf" "$SCRIPT_DIR/../windows/assets/LibreBodoni-Italic.ttf"; do
    if [ -f "$cand" ]; then
        FONT_SRC="$cand"
        break
    fi
done

find_python() {
    for candidate in "${AGBA_PYTHON:-}" /usr/bin/python3 /usr/local/bin/python3 "$(command -v python3 2>/dev/null || true)"; do
        [ -n "$candidate" ] && [ -x "$candidate" ] || continue
        if "$candidate" -c "
import cairo, gi
gi.require_version('Gtk', '4.0')
gi.require_version('Gdk', '4.0')
gi.require_version('Gst', '1.0')
gi.require_version('Pango', '1.0')
gi.require_version('PangoCairo', '1.0')
from gi.repository import Gtk, Gdk, Gst, Pango, PangoCairo
Gst.init(None)
assert Gst.ElementFactory.find('gtk4paintablesink') is not None
" >/dev/null 2>&1; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done
    return 1
}

if ! PY_BIN=$(find_python); then
    echo "错误：未找到完整依赖的 Python 3 环境（需要 PyGObject、GTK 4、GStreamer 1.0 + gtk4paintablesink、PyCairo）。" >&2
    echo "" >&2
    echo "请先按发行版安装依赖：" >&2
    echo "  Ubuntu/Debian: sudo apt install python3-gi python3-cairo gir1.2-gtk-4.0 gir1.2-gst-plugins-base-1.0 gir1.2-pango-1.0 gstreamer1.0-gtk4 gstreamer1.0-plugins-good" >&2
    echo "  Fedora:        sudo dnf install python3-gobject python3-cairo gtk4 gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugin-gtk4" >&2
    echo "  Arch:          sudo pacman -S python-gobject python-cairo gtk4 gst-plugins-base gst-plugins-good gst-plugin-gtk4" >&2
    exit 1
fi

XDG_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
XDG_STATE_HOME="${XDG_STATE_HOME:-$HOME/.local/state}"
INSTALL_DIR="$XDG_DATA_HOME/antigravity-boot-animation"
BIN_DIR="$HOME/.local/bin"
APP_DIR="$XDG_DATA_HOME/applications"
AUTOSTART_DIR="$XDG_CONFIG_HOME/autostart"
ICON_FILE="$XDG_DATA_HOME/icons/hicolor/128x128/apps/antigravity-boot-animation.png"

# 停掉正在运行的旧版本
if command -v systemctl >/dev/null 2>&1; then
    systemctl --user stop antigravity-boot-animation.service >/dev/null 2>&1 || true
fi
if command -v gdbus >/dev/null 2>&1; then
    gdbus call --session \
        --dest io.github.wynneceles.AntigravityBootAnimation \
        --object-path /io/github/wynneceles/AntigravityBootAnimation/Control \
        --method io.github.wynneceles.AntigravityBootAnimation.Quit >/dev/null 2>&1 || true
fi
pkill -f 'antigravity-boot-animation/src/main.py' 2>/dev/null || true
sleep 0.4

mkdir -p "$INSTALL_DIR/src" "$INSTALL_DIR/assets" "$INSTALL_DIR/kwin-script" \
         "$BIN_DIR" "$APP_DIR" "$AUTOSTART_DIR" "$(dirname "$ICON_FILE")"

cp -f "$SCRIPT_DIR/antigravity-boot-animation" "$INSTALL_DIR/antigravity-boot-animation"
chmod +x "$INSTALL_DIR/antigravity-boot-animation"
cp -f "$SCRIPT_DIR/uninstall.sh" "$INSTALL_DIR/uninstall.sh"
chmod +x "$INSTALL_DIR/uninstall.sh"
cp -f "$SCRIPT_DIR"/src/*.py "$INSTALL_DIR/src/"
cp -f "$SCRIPT_DIR/kwin-script/tracker.js" "$INSTALL_DIR/kwin-script/tracker.js"

if [ ! -f "$INSTALL_DIR/assets/intro.mp4" ] || ! cmp -s "$INTRO_SRC" "$INSTALL_DIR/assets/intro.mp4"; then
    cp -f "$INTRO_SRC" "$INSTALL_DIR/assets/intro.mp4"
fi
if [ ! -f "$INSTALL_DIR/assets/loop.mp4" ] || ! cmp -s "$LOOP_SRC" "$INSTALL_DIR/assets/loop.mp4"; then
    cp -f "$LOOP_SRC" "$INSTALL_DIR/assets/loop.mp4"
fi
if [ -n "$FONT_SRC" ]; then
    cp -f "$FONT_SRC" "$INSTALL_DIR/assets/LibreBodoni-Italic.ttf"
fi

# 生成四芒星应用图标
PYTHONDONTWRITEBYTECODE=1 "$PY_BIN" -c "
import sys
sys.path.insert(0, '$INSTALL_DIR/src')
from pathlib import Path
from icon import write_png
write_png(Path('$ICON_FILE'), 128)
"
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$XDG_DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

# 检查 GStreamer 解码能力；若缺少专利编解码插件，则自动本地转码一份 VP9+Opus 到安装目录
echo "检查视频解码兼容性…"
PYTHONDONTWRITEBYTECODE=1 "$PY_BIN" "$INSTALL_DIR/src/media.py" \
    --prepare \
    --intro "$INSTALL_DIR/assets/intro.mp4" \
    --loop "$INSTALL_DIR/assets/loop.mp4" \
    --out-dir "$INSTALL_DIR/assets" >/dev/null

ln -sf "$INSTALL_DIR/antigravity-boot-animation" "$BIN_DIR/antigravity-boot-animation"

cat > "$APP_DIR/antigravity-boot-animation.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Gemini 开机动画
GenericName=Antigravity Boot Animation
Comment=为 Antigravity IDE 添加 Gemini 开屏与半透明循环背景
Exec=$INSTALL_DIR/antigravity-boot-animation --settings
Icon=antigravity-boot-animation
Terminal=false
Categories=Utility;
StartupNotify=false
EOF

cat > "$AUTOSTART_DIR/antigravity-boot-animation.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Gemini 开机动画（后台守护）
Comment=监听 Antigravity 启动并播放开机动画
Exec=$INSTALL_DIR/antigravity-boot-animation
Icon=antigravity-boot-animation
Terminal=false
Categories=Utility;
StartupNotify=false
X-GNOME-Autostart-enabled=true
EOF

find_system_tool() {
    local name="$1"
    for cand in "/usr/bin/$name" "/usr/local/bin/$name" "/bin/$name" "$(command -v "$name" 2>/dev/null || true)"; do
        [ -n "$cand" ] && [ -x "$cand" ] && { printf '%s\n' "$cand"; return 0; }
    done
    return 1
}

GNOME_NEEDS_RELOGIN=0
DE_LOWER=$(printf '%s' "${XDG_CURRENT_DESKTOP:-${DESKTOP_SESSION:-}}" | tr '[:upper:]' '[:lower:]')
if [[ "$DE_LOWER" == *gnome* || "$DE_LOWER" == *ubuntu* ]] && [ -n "${WAYLAND_DISPLAY:-}" ]; then
    EXT_UUID="antigravity-boot-animation@wynneceles.github.io"
    EXT_DEST="$XDG_DATA_HOME/gnome-shell/extensions/$EXT_UUID"
    mkdir -p "$EXT_DEST"
    cp -f "$SCRIPT_DIR/gnome-extension/$EXT_UUID/"* "$EXT_DEST/"

    GNOME_EXT_BIN=$(find_system_tool gnome-extensions || true)
    GSETTINGS_BIN=$(find_system_tool gsettings || true)
    if [ -n "$GNOME_EXT_BIN" ] && env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GNOME_EXT_BIN" enable "$EXT_UUID" >/dev/null 2>&1; then
        :
    elif [ -n "$GSETTINGS_BIN" ]; then
        CUR_EXTS=$(env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" get org.gnome.shell enabled-extensions 2>/dev/null || echo "@as []")
        NEW_EXTS=$("$PY_BIN" -c "
import ast, sys
raw = sys.argv[1].strip()
if raw.startswith('@as '):
    raw = raw[4:]
try:
    items = list(ast.literal_eval(raw))
except Exception:
    items = []
uuid = sys.argv[2]
if uuid not in items:
    items.append(uuid)
print('[' + ', '.join(repr(x) for x in items) + ']')
" "$CUR_EXTS" "$EXT_UUID")
        env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" set org.gnome.shell enabled-extensions "$NEW_EXTS" >/dev/null 2>&1 || true

        CUR_DIS=$(env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" get org.gnome.shell disabled-extensions 2>/dev/null || echo "@as []")
        NEW_DIS=$("$PY_BIN" -c "
import ast, sys
raw = sys.argv[1].strip()
if raw.startswith('@as '):
    raw = raw[4:]
try:
    items = [x for x in ast.literal_eval(raw) if x != sys.argv[2]]
except Exception:
    items = []
print('@as []' if not items else '[' + ', '.join(repr(x) for x in items) + ']')
" "$CUR_DIS" "$EXT_UUID")
        env -u GSETTINGS_SCHEMA_DIR -u GSETTINGS_BACKEND "$GSETTINGS_BIN" set org.gnome.shell disabled-extensions "$NEW_DIS" >/dev/null 2>&1 || true
    fi

    GNOME_HELPER_UP=0
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        if gdbus call --session \
            --dest io.github.wynneceles.AntigravityBootAnimation.GnomeHelper \
            --object-path /io/github/wynneceles/AntigravityBootAnimation/GnomeHelper \
            --method io.github.wynneceles.AntigravityBootAnimation.GnomeHelper.Ping >/dev/null 2>&1; then
            GNOME_HELPER_UP=1
            break
        fi
        sleep 0.1
    done
    if [ "$GNOME_HELPER_UP" -eq 0 ]; then
        GNOME_NEEDS_RELOGIN=1
    fi
fi

if [ "$NO_LAUNCH" -eq 0 ]; then
    if command -v systemd-run >/dev/null 2>&1 && systemd-run --user --quiet --collect \
        --unit=antigravity-boot-animation \
        --setenv=DISPLAY="${DISPLAY:-}" \
        --setenv=WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-}" \
        --setenv=XDG_CURRENT_DESKTOP="${XDG_CURRENT_DESKTOP:-}" \
        "$INSTALL_DIR/antigravity-boot-animation" --settings >/dev/null 2>&1; then
        :
    else
        setsid nohup "$INSTALL_DIR/antigravity-boot-animation" --settings \
            >/dev/null 2>&1 </dev/null &
    fi
    sleep 1
fi

echo ""
echo "✦ 装好了！已安装到 $INSTALL_DIR"
echo "   托盘会出现蓝紫色四芒星图标，也弹出了设置窗口。"
echo "   下次打开 Antigravity 就会播放开机动画；想马上看效果，点托盘图标 →「播放一次开屏动画」。"
if [ "$GNOME_NEEDS_RELOGIN" -eq 1 ]; then
    echo ""
    echo "⚠️  重要提示：检测到你正在使用 GNOME Wayland，已安装并启用配套窗口跟随扩展；"
    echo "   由于 GNOME Wayland 仅在会话登录时加载新安装的扩展，**仅重启 Antigravity 不会生效**，"
    echo "   请**注销并重新登录一次系统桌面**，重登后即可自动覆盖默认启动画面并固定贴合在 Antigravity 窗口上。"
fi

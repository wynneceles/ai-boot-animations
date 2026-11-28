#!/usr/bin/env bash
# 一行命令安装 Antigravity 开机动画（Linux 版）：
#   curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/antigravity/linux/web-install.sh | bash
# Copyright (C) 2026 wynneceles
# Licensed under the GNU General Public License v3 (see repository LICENSE).

set -euo pipefail

[ "$(uname -s)" = "Linux" ] || { echo "只支持 Linux"; exit 1; }

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

REPO_URL="${AGBA_REPO_URL:-https://github.com/wynneceles/ai-boot-animations.git}"
REF="${AGBA_REF:-main}"

echo "⬇️  正在获取 Gemini 开机动画（Linux 版）…"
if command -v git >/dev/null 2>&1; then
    git clone --depth 1 --filter=blob:none --sparse --branch "$REF" "$REPO_URL" "$TMP/repo"
    git -C "$TMP/repo" sparse-checkout set \
        antigravity/assets \
        antigravity/windows/assets/LibreBodoni-Italic.ttf \
        antigravity/windows/assets/LibreBodoni-OFL.txt \
        antigravity/linux \
        LICENSE
    bash "$TMP/repo/antigravity/linux/install.sh"
else
    TAR_URL="https://github.com/wynneceles/ai-boot-animations/archive/refs/heads/$REF.tar.gz"
    curl -fL --progress-bar -o "$TMP/repo.tar.gz" "$TAR_URL"
    tar -xzf "$TMP/repo.tar.gz" -C "$TMP"
    SRC_DIR=$(find "$TMP" -maxdepth 1 -mindepth 1 -type d | head -n 1)
    bash "$SRC_DIR/antigravity/linux/install.sh"
fi

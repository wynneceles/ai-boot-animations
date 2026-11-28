#!/bin/zsh
# 一行命令安装（不用装 Xcode 命令行工具）：
#   curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/deepseek/web-install.sh | zsh
# 从 GitHub Releases 下载编好的通用版程序（Apple 芯片 / Intel 都能用），再走 install.sh 安装
set -e
# 和 Claude 版共用一个仓库：Claude 版占着 releases/latest，DeepSeek 版固定指向自己的版本号（发新版时改这里）
TAG="deepseek-v1.0.0"
URL="https://github.com/wynneceles/ai-boot-animations/releases/download/$TAG/DeepSeekBootAnimation.zip"
[[ "$(uname)" == Darwin ]] || { echo "只支持 macOS"; exit 1; }
MAJOR=$(sw_vers -productVersion | cut -d. -f1)
(( MAJOR >= 13 )) || { echo "需要 macOS 13 或更新，你的是 $(sw_vers -productVersion)"; exit 1; }
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
echo "⬇️  正在下载 DeepSeek 开机动画（约 70MB）…"
curl -fL --progress-bar -o "$TMP/pkg.zip" "$URL"
ditto -x -k "$TMP/pkg.zip" "$TMP/pkg"
zsh "$TMP/pkg/DeepSeekBootAnimation/install.sh"

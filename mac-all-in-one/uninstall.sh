#!/bin/zsh
# 彻底卸载四合一：停掉常驻、删登录项、删小程序、删设置和日志。四个应用本身本来就没被改过。
UID_=$(id -u)
L=io.github.celestewynne0506-pixel.ai-boot-animations
launchctl bootout "gui/$UID_/$L" 2>/dev/null
rm -f "$HOME/Library/LaunchAgents/$L.plist"
pkill -f 'AIBootAnimations.app/Contents/MacOS/AIBootAnimations' 2>/dev/null
rm -rf "$HOME/Applications/AIBootAnimations.app"
rm -rf "$HOME/Library/Application Support/AIBootAnimations"
rm -f  "$HOME/Library/Logs/AIBootAnimations.log"
defaults delete $L 2>/dev/null
echo "已卸载干净。"

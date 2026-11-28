#!/bin/zsh
# 彻底卸载：停掉常驻、删登录项、删小程序、删设置和日志。DeepSeek Harness.app 本来就没被改过。
UID_=$(id -u)
for L in io.github.celestewynne0506-pixel.deepseek-boot-animation; do
  launchctl bootout "gui/$UID_/$L" 2>/dev/null
  rm -f "$HOME/Library/LaunchAgents/$L.plist"
done
pkill -f 'DeepSeekBootAnimation.app/Contents/MacOS/DeepSeekBootAnimation' 2>/dev/null
rm -rf "$HOME/Applications/DeepSeekBootAnimation.app"
rm -rf "$HOME/Library/Application Support/DeepSeekBootAnimation"
rm -f  "$HOME/Library/Logs/DeepSeekBootAnimation.log"
defaults delete io.github.celestewynne0506-pixel.deepseek-boot-animation 2>/dev/null
echo "已卸载干净。"

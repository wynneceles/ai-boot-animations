#!/bin/zsh
# 编译 → 装到 ~/Applications → 设为登录自动启动 → 弹出设置窗口
# 发现装着原版单独小程序时，先把它们的设置搬过来，再问要不要卸掉（两个都开着会在同一个窗口上叠两层动画）
# 不会修改 Claude、ChatGPT、Antigravity、DeepSeek Harness 的任何文件
# 用法：./install.sh            （--remove-old / --keep-old：不问，直接卸掉 / 保留原版）
set -e
cd "${0:A:h}"
if [[ -f Sources/main.swift ]]; then
  ./build.sh
  SRC_APP=build/AIBootAnimations.app
else
  SRC_APP=AIBootAnimations.app
  [[ -d "$SRC_APP" ]] || { echo "找不到 $SRC_APP"; exit 1; }
fi
LABEL=io.github.celestewynne0506-pixel.ai-boot-animations
DEST="$HOME/Applications/AIBootAnimations.app"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
CFG="$HOME/Library/Application Support/AIBootAnimations"
UID_=$(id -u)

# 原版：设置键  程序名  登录项名
OLD=(claude ClaudeBootAnimation claude-boot-animation
     gpt GPTBootAnimation gpt-boot-animation
     antigravity AntigravityBootAnimation antigravity-boot-animation
     deepseek DeepSeekBootAnimation deepseek-boot-animation)
found=()
mkdir -p "$CFG"
for k n l in $OLD; do
  src="$HOME/Library/Application Support/$n/config.json"
  [[ -f "$src" && ! -f "$CFG/$k.json" ]] && cp "$src" "$CFG/$k.json" && echo "已搬过来 $n 的设置"
  if [[ -f "$HOME/Library/LaunchAgents/io.github.celestewynne0506-pixel.$l.plist" || -d "$HOME/Applications/$n.app" ]]; then found+=($n); fi
done
if (( ${#found} )); then
  if [[ "$1" == --remove-old ]]; then yes=1
  elif [[ "$1" == --keep-old ]]; then yes=0
  else
    echo
    echo "发现已经装着原版小程序：${(j:、:)found}"
    echo "四合一已经包含它们的动画。两个都开着的话，同一个窗口上会叠两层动画。"
    read "ans?要卸掉这些原版吗？设置已经搬过来了 [Y/n] " < /dev/tty || ans=""
    [[ -z "$ans" || "$ans" == [yY]* ]] && yes=1 || yes=0
  fi
  if (( yes )); then
    for k n l in $OLD; do
      (( ${found[(Ie)$n]} )) || continue
      for L in io.github.celestewynne0506-pixel.$l io.github.celeste.$l; do
        launchctl bootout "gui/$UID_/$L" 2>/dev/null || true
        rm -f "$HOME/Library/LaunchAgents/$L.plist"
      done
      pkill -f "$n.app/Contents/MacOS/$n" 2>/dev/null || true
      rm -rf "$HOME/Applications/$n.app" "$HOME/Library/Application Support/$n"
      rm -f "$HOME/Library/Logs/$n.log"
      defaults delete io.github.celestewynne0506-pixel.$l 2>/dev/null || true
      echo "已卸载原版 $n"
    done
  else
    echo "保留了原版：${(j:、:)found}。建议在四合一菜单里把对应应用的「播放开屏动画」「保留循环背景」关掉，免得叠两层。"
  fi
fi

# 先停掉旧的四合一
launchctl bootout "gui/$UID_/$LABEL" 2>/dev/null || true
pkill -f 'AIBootAnimations.app/Contents/MacOS/AIBootAnimations' 2>/dev/null || true
mkdir -p "$HOME/Applications" "$HOME/Library/LaunchAgents"
rm -rf "$DEST"; ditto "$SRC_APP" "$DEST"
xattr -dr com.apple.quarantine "$DEST" 2>/dev/null || true
cat > "$PLIST" <<PL
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array><string>$DEST/Contents/MacOS/AIBootAnimations</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
  <key>ProcessType</key><string>Interactive</string>
</dict></plist>
PL
launchctl bootstrap "gui/$UID_" "$PLIST"
sleep 1.5
open "$DEST"   # 已在运行时再打开一次 = 弹出设置窗口
echo
echo "✨ 装好了！已弹出设置窗口。四个应用里任意一个开着时，菜单栏右边会出现 ✨ 图标。"
echo "   点 ✨ → 先选应用（Claude / GPT / Gemini / DeepSeek）→ 再调那个应用的动画。"

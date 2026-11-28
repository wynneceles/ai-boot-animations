#!/bin/zsh
# 编译并打包成 build/DeepSeekBootAnimation.app
# 用法：./build.sh                      用仓库自带的动画（assets/intro.mp4 + assets/loop.mp4）
#       ./build.sh 开屏.mp4 循环.mp4     换成你自己的动画（开屏片尾要能接上循环片头）
#       ./build.sh 开屏.mp4 循环.mp4 音轨  另配一条音轨到开屏画面上（需要 ffmpeg）
set -e
cd "${0:A:h}"
INTRO="${1:-assets/intro.mp4}"
LOOP="${2:-assets/loop.mp4}"
AUDIO="${3:-}"
[[ -f "$INTRO" && -f "$LOOP" ]] || { echo "找不到视频：$INTRO / $LOOP"; exit 1; }
command -v swiftc >/dev/null || { echo "需要先装 Xcode 命令行工具：xcode-select --install"; exit 1; }

APP=build/DeepSeekBootAnimation.app
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# 同时编出 Apple 芯片和 Intel 两份，再合成一个通用程序
TMPB=$(mktemp -d)
for ARCH in arm64 x86_64; do
  swiftc -O -target $ARCH-apple-macos13 -o "$TMPB/$ARCH" Sources/main.swift \
    -framework AppKit -framework AVFoundation 2> >(grep -v -E 'deprecated|^\s*(\||`|[0-9]+ \|)' >&2)
done
lipo -create "$TMPB/arm64" "$TMPB/x86_64" -output "$APP/Contents/MacOS/DeepSeekBootAnimation"
rm -rf "$TMPB"
if [[ -n "$AUDIO" ]]; then
  command -v ffmpeg >/dev/null || { echo "配音轨需要 ffmpeg：brew install ffmpeg"; exit 1; }
  ffmpeg -v error -y -i "$INTRO" -i "$AUDIO" -map 0:v:0 -map 1:a:0 -c copy -tag:v hvc1 \
    -movflags +faststart "$APP/Contents/Resources/intro.mp4"
else
  cp "$INTRO" "$APP/Contents/Resources/intro.mp4"
fi
cp "$LOOP" "$APP/Contents/Resources/loop.mp4"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>io.github.celestewynne0506-pixel.deepseek-boot-animation</string>
  <key>CFBundleName</key><string>DeepSeek 开机动画</string>
  <key>CFBundleExecutable</key><string>DeepSeekBootAnimation</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0.1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$APP" 2>/dev/null
echo "打包完成：$APP"

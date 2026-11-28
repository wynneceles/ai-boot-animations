#!/bin/zsh
# 编译并打包成 build/AIBootAnimations.app（四合一：Claude / GPT / Gemini / DeepSeek）
# 四段动画直接取仓库里各自子项目的素材：claude/ codex/ antigravity/ deepseek/ 下的 assets/intro.mp4 + loop.mp4
set -e
cd "${0:A:h}"
command -v swiftc >/dev/null || { echo "需要先装 Xcode 命令行工具：xcode-select --install"; exit 1; }
typeset -A SRC=(claude ../claude gpt ../codex antigravity ../antigravity deepseek ../deepseek)
for k d in ${(kv)SRC}; do
  [[ -f "$d/assets/intro.mp4" && -f "$d/assets/loop.mp4" ]] || { echo "找不到 $k 的动画：$d/assets/"; exit 1; }
done

APP=build/AIBootAnimations.app
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# 同时编出 Apple 芯片和 Intel 两份，再合成一个通用程序
TMPB=$(mktemp -d)
for ARCH in arm64 x86_64; do
  swiftc -O -target $ARCH-apple-macos13 -o "$TMPB/$ARCH" Sources/main.swift \
    -framework AppKit -framework AVFoundation 2> >(grep -v -E 'deprecated|^\s*(\||`|[0-9]+ \|)' >&2)
done
lipo -create "$TMPB/arm64" "$TMPB/x86_64" -output "$APP/Contents/MacOS/AIBootAnimations"
rm -rf "$TMPB"
for k d in ${(kv)SRC}; do
  mkdir -p "$APP/Contents/Resources/$k"
  cp "$d/assets/intro.mp4" "$d/assets/loop.mp4" "$APP/Contents/Resources/$k/"
done
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>io.github.celestewynne0506-pixel.ai-boot-animations</string>
  <key>CFBundleName</key><string>AI 开机动画</string>
  <key>CFBundleExecutable</key><string>AIBootAnimations</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$APP" 2>/dev/null
echo "打包完成：$APP"

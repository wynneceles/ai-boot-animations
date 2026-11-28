# Gemini 开机动画 · Linux 版

> 属于 [Antigravity 开机动画](../README.md) 的 Linux 适配版，不修改 Antigravity 本体（`/opt/Antigravity` 等），独立运行、随时一键卸载。

## 效果与功能

与 macOS / Windows 版保持一致：

- 🎬 **开屏动画**：在 Antigravity 主窗口上播放双剑少女开屏（含音效与语音）
- 🔁 **无缝循环背景**：到 `fadeStartSeconds`（默认 `12.93s`）自动淡出透出界面并开启**点击穿透**，随后无缝切入半透明动态循环背景
- ⏭ **右上角「跳过 ›」**：点击后保留当前开屏定格并柔和淡出到循环背景，同时音效淡出
- ✦ **系统托盘与设置窗口**：蓝紫色四芒星托盘图标（StatusNotifierItem + DBusMenu）与原生 GTK4 设置窗口，支持：
  - 开屏动画开关、循环背景开关
  - 背景不透明度（0–80%）与开屏音量（0–100%）实时滑块调节
  - 播放一次开屏动画 / 开启循环背景 / 暂停与继续 / 关掉动画或背景
  - 打开 `config.json`（`~/.config/antigravity-boot-animation/config.json`）与一键卸载
- 🪟 **多桌面环境窗口跟随**：
  - **X11（任意桌面）**：原生 Xlib + XShape 跟随窗口位置、大小、遮挡层级与点击穿透
  - **GNOME Wayland（Ubuntu 24.04 / 26.04、Fedora 等，GNOME 45–50）**：配套安装轻量 GNOME Shell 扩展，精确同步位置、尺寸、工作区、最小化、Overview 隐藏与堆叠层级（不盖住压在 Antigravity 上方的其他窗口）
  - **KDE Plasma Wayland（KWin 5/6）**：通过 KWin Scripting D-Bus 动态加载跟随脚本
  - **Hyprland**：通过 `hyprctl` IPC 定位与跟随

## 依赖准备

运行需要 Python 3（带 PyGObject）、GTK 4、GStreamer 1.0（含 `gtk4paintablesink`）与 PyCairo：

- **Ubuntu / Debian**（24.04+ / 26.04）：
  ```bash
  sudo apt install python3-gi python3-cairo gir1.2-gtk-4.0 gir1.2-gst-plugins-base-1.0 gir1.2-pango-1.0 gstreamer1.0-gtk4 gstreamer1.0-plugins-good
  ```
- **Fedora**：
  ```bash
  sudo dnf install python3-gobject python3-cairo gtk4 gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugin-gtk4
  ```
- **Arch Linux**：
  ```bash
  sudo pacman -S python-gobject python-cairo gtk4 gst-plugins-base gst-plugins-good gst-plugin-gtk4
  ```

> **编解码说明**：上游素材 `intro.mp4` / `loop.mp4` 为 HEVC + AAC。若系统未安装 `gstreamer1.0-libav` 等解码插件，安装脚本会自动调用本机 `ffmpeg`（包括 Python 环境里的 `imageio-ffmpeg`）在本地安装目录一次性转码为开源免费的 VP9 + Opus（`.webm`）用于播放，不改动仓库素材。

## 安装

### 一行命令安装

```bash
curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/antigravity/linux/web-install.sh | bash
```

### 从源码安装

```bash
git clone https://github.com/wynneceles/ai-boot-animations.git
cd ai-boot-animations/antigravity/linux
./install.sh
```

也可以指定自己的开屏与循环视频：

```bash
./install.sh 你的开屏.mp4 你的循环.mp4
```

> **GNOME Wayland 提示**：首次安装配套 GNOME Shell 扩展后，需要**注销并重新登录一次桌面**（Wayland 仅在会话启动时加载新安装的扩展），之后窗口移动、遮挡、最小化即可完全精确跟随；重登前仍可通过通用浮层模式预览和调参。

## 配置文件与日志

- 配置文件：`~/.config/antigravity-boot-animation/config.json`（字段与 macOS / Windows 版完全一致）
- 日志文件：`~/.local/state/antigravity-boot-animation/log.txt`
- 安装位置：`~/.local/share/antigravity-boot-animation/`
- 开机自启：`~/.config/autostart/antigravity-boot-animation.desktop`

常用开发 / 测试参数：

```bash
antigravity-boot-animation --settings                 # 打开设置窗口
antigravity-boot-animation --preview                  # 在当前 Antigravity 窗口预览一次开屏
antigravity-boot-animation --loop-only                # 仅预览循环背景
antigravity-boot-animation --test-skip 3              # 3 秒后自动触发跳过
antigravity-boot-animation --render-subtitle bg.png out.png 1
```

## 卸载

托盘四芒星图标 →「卸载 Antigravity 开机动画…」，或者运行：

```bash
~/.local/share/antigravity-boot-animation/uninstall.sh
```

会清理后台进程、自启项、应用菜单项、设置、缓存、日志与 GNOME 扩展，不会触碰 Antigravity 本身。

---

**English (short):** Native Linux port (GTK 4 + GStreamer + StatusNotifierItem tray) for Antigravity IDE on X11, GNOME Wayland (45–50 via companion extension), KDE Plasma Wayland (KWin script), and Hyprland. Never modifies Antigravity itself. Install with `./install.sh` (or the one-liner above); uninstall anytime with `./uninstall.sh` or from the tray/settings window. Code is GPL-3.0; assets remain CC BY-NC-ND 4.0.

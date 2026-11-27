# Gemini 开机动画 · Gemini Boot Animation for Antigravity

> 属于 [ai-boot-animations](../README.md) 合集，是 [Claude 开机动画](../claude/) 和 [GPT 开机动画](../codex/) 的姊妹版。

给 Google 的 AI 编程工具 Antigravity（Mac / Windows）加一段开机动画：每次打开 Antigravity，先在窗口上播放一段 Gemini 双剑少女的开屏，进入循环画面后慢慢透出真正的 Antigravity 界面，留下一层半透明的动态背景。

![预览](docs/preview.jpg)

- 🎬 **开屏动画**：发光线稿 → 化为实体 → 剑尖星芒 → 双剑相击，带音效、英文配音和字幕
- 🔁 **循环背景**：开屏结束后半透明循环播放，点击直接穿透到 Antigravity，不影响使用
- ⏭ **跳过**：开屏时右上角有「跳过」，点了会淡出接到循环背景
- ✦ **菜单栏随时调**：开屏开关、循环背景开关、背景不透明度、开屏音量，拖滑块实时生效（[详细说明](#菜单栏--能干什么)）
- 🧼 **不改 Antigravity 本体**：它是一个独立的小程序，盖在 Antigravity 窗口上播放；Antigravity 自动更新、签名、权限都不受影响，卸载后什么都不留
- 🤝 **可以和 Claude、GPT 开机动画同时装**：各管各的，菜单栏图标也不一样（Claude ✨、GPT 🌙、这个 ✦）

> 非官方同人项目，与 Google 无关。Gemini、Antigravity 是 Google 的商标。

## 安装

### Mac

需要 **macOS 13 或更新**，Apple 芯片和 Intel 都行。

**一行命令安装（推荐）**：打开「终端」，粘贴这一行，回车：

```bash
curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/antigravity/web-install.sh | zsh
```

会从本仓库的 [Releases](https://github.com/wynneceles/ai-boot-animations/releases) 下载编好的程序（约 70MB）并装好，不需要装 Xcode。

**从源码编译安装**：需要 Xcode 命令行工具（没有的话先运行 `xcode-select --install`）：

```bash
git clone https://github.com/wynneceles/ai-boot-animations.git
cd ai-boot-animations/antigravity
./install.sh
```

装好后会弹出设置窗口。下次打开 Antigravity 就会播放，Antigravity 开着时菜单栏右边会出现 ✦ 图标（四芒星）；想马上看效果，先打开 Antigravity，再点 ✦ →「播放一次开屏动画」。

不需要辅助功能、屏幕录制等任何系统授权，也不会修改 Antigravity 本身。

### Windows

需要 **Windows 10 / 11**。打开「PowerShell」（开始菜单搜索 PowerShell），粘贴这一行，回车：

```powershell
[Net.ServicePointManager]::SecurityProtocol='Tls12'; irm https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/antigravity/windows/web-install.ps1 | iex
```

打不开 raw.githubusercontent.com 的话，把网址换成 `https://cdn.jsdelivr.net/gh/wynneceles/ai-boot-animations@main/antigravity/windows/web-install.ps1`。

会下载约 49MB，用 Windows 自带的编译器现场编译（不用装任何 SDK），装到**剩余空间最大的非系统盘**（例如 `D:\AIBootAnimations\AntigravityBootAnimation`，只有 C 盘时才装 C 盘；想自己指定位置，先运行 `$env:AGBA_DIR='E:\某个文件夹'` 再粘贴安装命令），右下角托盘出现蓝紫色四芒星图标并弹出设置窗口。打开 Antigravity（窗口进程 `Antigravity.exe`）时自动播放。和 Claude、GPT 版可以同时装。卸载：托盘四芒星图标右键 →「卸载 Gemini 开机动画…」。

设置文件：`%APPDATA%\AntigravityBootAnimation\config.json`；日志：`%LOCALAPPDATA%\AntigravityBootAnimation\log.txt`。

## 菜单栏 ✦ 能干什么

Antigravity 开着的时候，菜单栏右边会出现一个四芒星 ✦，点开就能调（Antigravity 没开时图标自动隐藏；想改设置，再打开一次「Antigravity 开机动画」App 就会弹出设置窗口）。所有调节**拖动时立刻生效**，正在播放的动画马上跟着变，不用重启 Antigravity。

<p align="center">
  <img src="docs/menu.png" width="300" alt="菜单栏 ✦ 菜单">
  &nbsp;&nbsp;
  <img src="docs/settings.png" width="330" alt="设置窗口">
</p>

**打开 Antigravity 时**放什么，两个开关自由组合：

| 播放开屏动画 | 保留循环背景 | 效果 |
|:---:|:---:|---|
| ✅ | ✅ | 默认：先放开屏，再透出界面，留下半透明动态背景，直到你手动关掉 |
| ✅ | ❌ | 只放开屏，透出界面时整个淡出消失 |
| ❌ | ✅ | 不放开屏，Antigravity 一打开就直接有动态背景 |
| ❌ | ❌ | 原装启动，什么都不放 |

**两个滑块：**

- **背景不透明度**（0–80%）：循环背景有多浓。调低更不挡字，调高更有氛围。默认 25%
- **开屏音量**：开屏音效和配音的音量。电脑静音时不出声；循环背景本身是静音的

**现在，在 Antigravity 窗口上：** 不用重启 Antigravity，直接在当前窗口上「播放一次开屏动画」「开启循环背景」或「关掉动画 / 背景」。循环背景就是从这里关的。

**其他：**

- **跳过**：开屏时右上角有「跳过」，点了会把画面淡出、接到循环背景；进入循环背景后跳过键自动消失
- **设置窗口…**：跟菜单一样的内容，单独一个窗口。找不到 ✦ 时，在聚焦搜索（⌘ 空格）里搜 `AntigravityBootAnimation` 打开也会弹出它
- **打开设置文件…**：更细的参数，见下表
- **卸载 Antigravity 开机动画…**：确认后一键删干净

### 设置文件

`~/Library/Application Support/AntigravityBootAnimation/config.json`，改完保存，下次播放生效：

| 键 | 默认 | 含义 |
|---|---|---|
| `playIntro` / `loopBackground` | true / true | 同菜单里的两个开关 |
| `residualAlpha` | 0.25 | 循环背景不透明度 |
| `introVolume` | 1.0 | 开屏音量（循环背景是静音的） |
| `fadeStartSeconds` | 12.93 | 开屏里进入循环画面、开始透出界面的时间 |
| `fadeDurationSeconds` | 1.2 | 透出界面的渐变时长 |
| `showSubtitles` | false | 是否由插件另画字幕（这版字幕已经做进视频里，所以默认关） |
| `subtitles` | 两句 | 字幕文字和起止秒数（打开 `showSubtitles` 才用得到） |
| `subtitleFadeSeconds` | 0.5 | 字幕渐显渐隐时长 |
| `subtitleWidthRatio` | 0.6 | 字幕大小（第一句约占窗口宽度的比例） |

## 换成自己的动画

```bash
./build.sh 你的开屏.mp4 你的循环.mp4          # 只打包
./install.sh                                  # 打包并安装（用 assets/ 里的默认动画）
```

想装自己的动画：把两个文件替换成 `assets/intro.mp4`、`assets/loop.mp4` 再运行 `./install.sh`。要求：

- 开屏的最后一帧能接上循环的第一帧，循环首尾相接
- 推荐 H.264 或 HEVC（`hvc1` 标签）；音轨放在开屏文件里，循环文件不用带声音（带了也会被静音）
- 改了开屏长度，记得在设置文件里调 `fadeStartSeconds`

## 卸载

菜单栏 ✦ →「卸载 Antigravity 开机动画…」，或者：

```bash
./uninstall.sh
```

会删除小程序、登录自启、设置和日志。Antigravity 本身从来没被改过，卸载后就是原装的样子。

## 原理

- 小程序登录后在后台常驻（`~/Library/LaunchAgents`），监听 Antigravity 启动（应用标识 `com.google.antigravity`）
- 用 `CGWindowListCopyWindowInfo` 只读取 Antigravity 主窗口的位置和大小（不读标题和内容，所以不需要授权），在它正上方盖一个无边框窗口播放动画，并跟着窗口移动、缩放；Antigravity 窗口被别的 App 压着时，动画也退到那个窗口下面，不会盖到别的 App
- 播放时把开屏和 120 段循环拼成一条时间线，放到末尾后自动跳回循环开头。停止播放或退出 Antigravity 时释放时间线。
- 应用或桌面切换时立即检查窗口。静止可见时每秒检查约 10 次，拖动时按屏幕刷新率提高到 60–120 次，完全遮挡时约 2 次，最小化时约 1 次。
- 系统休眠时停止窗口检查，唤醒后恢复检查。
- 进入循环后窗口变半透明并开启点击穿透，Antigravity 照常使用

日志：`~/Library/Logs/AntigravityBootAnimation.log`

开发用参数：`--preview`（在当前 Antigravity 窗口上预览）、`--loop-only`、`--test-skip 秒`、`--repeats N`、`--render-subtitle 背景.png 输出.png 第几句`、`--show-menu`（自动弹出菜单，方便截图）、`--settings`。

## 授权

- 代码：[GPL-3.0](../LICENSE)：可以自由使用、学习和修改；发布改过的版本时必须同样以 GPL-3.0 开源，并保留原作者署名
- 动画素材：[CC BY-NC-ND 4.0](assets/LICENSE.md)，可以安装、分享，不能商用、不能改后再发布。角色是网上流传的 Gemini 拟人形象，画面由即梦（Seedance 模型）生成，分镜与动作设计由作者完成
- 音效：Freesound CC0 素材；配音用 ElevenLabs 生成。见 [CREDITS.md](CREDITS.md)

---

**English (short):** An unofficial Gemini boot animation for Google's Antigravity IDE (bundle id `com.google.antigravity`) on macOS and Windows (`Antigravity.exe`). A tiny menu-bar helper overlays Antigravity's window with an intro animation, then fades into a translucent, click-through looping background. It never modifies Antigravity.app and can be installed alongside the Claude and GPT boot animations. Install with the one-liners above (macOS 13+ / Windows 10–11); uninstall from the ✦ menu or with `./uninstall.sh`. Code is GPL-3.0; animation assets are CC BY-NC-ND 4.0.

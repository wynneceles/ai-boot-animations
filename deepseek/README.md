# DeepSeek 开机动画 · DeepSeek Boot Animation for DeepSeek Harness

> 属于 [ai-boot-animations](../README.md) 合集，是 [Claude 开机动画](../claude/)、[GPT 开机动画](../codex/)、[Gemini 开机动画](../antigravity/) 的姊妹版。

给 DeepSeek 的 AI 编程工具 DeepSeek Harness（Mac / Windows）加一段开机动画：每次打开 DeepSeek Harness，先在窗口上播放一段大肥鱼深海弓手的开屏，进入循环画面后慢慢透出真正的 DeepSeek Harness 界面，留下一层半透明的动态背景。

![预览](docs/preview.jpg)

- 🎬 **开屏动画**：发光线稿 → 跃出海面、对月张弓 → 箭迎面掠过、拖着光尾消失 → 月下睁眼，带音效、英文配音和字幕
- 🔁 **循环背景**：开屏结束后半透明循环播放，点击直接穿透到 DeepSeek Harness，不影响使用
- ⏭ **跳过**：开屏时右上角有「跳过」，点了会淡出接到循环背景
- 🐟 **菜单栏随时调**：开屏开关、循环背景开关、背景不透明度、开屏音量，拖滑块实时生效（[详细说明](#菜单栏--能干什么)）
- 🧼 **不改 DeepSeek Harness 本体**：它是一个独立的小程序，盖在 DeepSeek Harness 窗口上播放；DeepSeek Harness 自动更新、签名、权限都不受影响，卸载后什么都不留
- 🤝 **可以和 Claude、GPT、Gemini 开机动画同时装**：各管各的，菜单栏图标也不一样（Claude ✨、GPT 🌙、Gemini ✦、这个 🐟）

> 非官方同人项目，与 DeepSeek 无关。DeepSeek、DeepSeek Harness 是其所有者的商标。

## 安装

### Mac

需要 **macOS 13 或更新**，Apple 芯片和 Intel 都行。

**一行命令安装（推荐）**：打开「终端」，粘贴这一行，回车：

```bash
curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/deepseek/web-install.sh | zsh
```

会从本仓库的 [Releases](https://github.com/wynneceles/ai-boot-animations/releases) 下载编好的程序并装好，不需要装 Xcode。

**从源码编译安装**：需要 Xcode 命令行工具（没有的话先运行 `xcode-select --install`）：

```bash
git clone https://github.com/wynneceles/ai-boot-animations.git
cd ai-boot-animations/deepseek
./install.sh
```

装好后会弹出设置窗口。下次打开 DeepSeek Harness 就会播放，DeepSeek Harness 开着时菜单栏右边会出现 🐟 图标；想马上看效果，先打开 DeepSeek Harness，再点 🐟 →「播放一次开屏动画」。

不需要辅助功能、屏幕录制等任何系统授权，也不会修改 DeepSeek Harness 本身。

### Windows

需要 **Windows 10 / 11**。打开「PowerShell」（开始菜单搜索 PowerShell），粘贴这一行，回车：

```powershell
[Net.ServicePointManager]::SecurityProtocol='Tls12'; irm https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/deepseek/windows/web-install.ps1 | iex
```

打不开 raw.githubusercontent.com 的话，把网址换成 `https://cdn.jsdelivr.net/gh/wynneceles/ai-boot-animations@main/deepseek/windows/web-install.ps1`。

会用 Windows 自带的编译器现场编译（不用装任何 SDK），装到**剩余空间最大的非系统盘**（例如 `D:\AIBootAnimations\DeepSeekBootAnimation`，只有 C 盘时才装 C 盘；想自己指定位置，先运行 `$env:AGBA_DIR='E:\某个文件夹'` 再粘贴安装命令），右下角托盘出现深海蓝四芒星图标并弹出设置窗口。打开 DeepSeek Harness（窗口进程 `DeepSeek Harness.exe`）时自动播放。和 Claude、GPT、Gemini 版可以同时装。卸载：托盘图标右键 →「卸载 DeepSeek 开机动画…」。

设置文件：`%APPDATA%\DeepSeekBootAnimation\config.json`；日志：`%LOCALAPPDATA%\DeepSeekBootAnimation\log.txt`。

## 菜单栏 🐟 能干什么

DeepSeek Harness 开着的时候，菜单栏右边会出现一条小鱼 🐟，点开就能调（DeepSeek Harness 没开时图标自动隐藏；想改设置，再打开一次「DeepSeek 开机动画」App 就会弹出设置窗口）。所有调节**拖动时立刻生效**，正在播放的动画马上跟着变，不用重启 DeepSeek Harness。

<p align="center">
  <img src="docs/menu.png" width="300" alt="菜单栏 🐟 菜单">
  &nbsp;&nbsp;
  <img src="docs/settings.png" width="330" alt="设置窗口">
</p>

**打开 DeepSeek Harness 时**放什么，两个开关自由组合：

| 播放开屏动画 | 保留循环背景 | 效果 |
|:---:|:---:|---|
| ✅ | ✅ | 默认：先放开屏，再透出界面，留下半透明动态背景，直到你手动关掉 |
| ✅ | ❌ | 只放开屏，透出界面时整个淡出消失 |
| ❌ | ✅ | 不放开屏，DeepSeek Harness 一打开就直接有动态背景 |
| ❌ | ❌ | 原装启动，什么都不放 |

**两个滑块：**

- **背景不透明度**（0–80%）：循环背景有多浓。调低更不挡字，调高更有氛围。默认 25%
- **开屏音量**：开屏音效和配音的音量。电脑静音时不出声；循环背景本身是静音的

**现在，在 DeepSeek Harness 窗口上：** 不用重启 DeepSeek Harness，直接在当前窗口上「播放一次开屏动画」「开启循环背景」「暂停循环背景」或「关掉动画 / 背景」。

**其他：**

- **跳过**：开屏时右上角有「跳过」，点了会把画面淡出、接到循环背景；进入循环背景后跳过键自动消失
- **设置窗口…**：跟菜单一样的内容，单独一个窗口。找不到 🐟 时，在聚焦搜索（⌘ 空格）里搜 `DeepSeekBootAnimation` 打开也会弹出它
- **打开设置文件…**：更细的参数，见下表
- **卸载 DeepSeek 开机动画…**：确认后一键删干净

### 设置文件

`~/Library/Application Support/DeepSeekBootAnimation/config.json`，改完保存，下次播放生效：

| 键 | 默认 | 含义 |
|---|---|---|
| `playIntro` / `loopBackground` | true / true | 同菜单里的两个开关 |
| `residualAlpha` | 0.25 | 循环背景不透明度 |
| `introVolume` | 1.0 | 开屏音量（循环背景是静音的） |
| `fadeStartSeconds` | 11.7 | 开屏里进入循环画面、开始透出界面的时间 |
| `fadeDurationSeconds` | 1.2 | 透出界面的渐变时长 |
| `showSubtitles` | true | 是否由插件画字幕（这版视频本身不带字幕，字幕由插件按窗口大小实时画） |
| `subtitles` | 两句 | 字幕文字和起止秒数 |
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
- 改了开屏长度，记得在设置文件里调 `fadeStartSeconds`，不要字幕就把 `showSubtitles` 关掉

## 卸载

菜单栏 🐟 →「卸载 DeepSeek 开机动画…」，或者：

```bash
./uninstall.sh
```

会删除小程序、登录自启、设置和日志。DeepSeek Harness 本身从来没被改过，卸载后就是原装的样子。

## 原理

- 小程序登录后在后台常驻（`~/Library/LaunchAgents`），监听 DeepSeek Harness 启动（应用标识 `com.deepseek.dsh`）
- 用 `CGWindowListCopyWindowInfo` 只读取 DeepSeek Harness 主窗口的位置和大小（不读标题和内容，所以不需要授权），在它正上方盖一个无边框窗口播放动画，并跟着窗口移动、缩放；DeepSeek Harness 窗口被别的 App 压着时，动画也退到那个窗口下面，不会盖到别的 App
- 窗口静止时降低检查频率、拖动时才高频跟随，被挡住或最小化时进一步降频，循环背景平时 CPU 占用约 2%（窗口跟随的降耗做法来自 [PR #10](https://github.com/wynneceles/ai-boot-animations/pull/10)，作者 yechu12138-sudo）
- 开屏和循环在内存里拼成一条时间线播放，接缝不卡；放到头自动跳回循环开头
- 进入循环后窗口变半透明并开启点击穿透，DeepSeek Harness 照常使用

日志：`~/Library/Logs/DeepSeekBootAnimation.log`

开发用参数：`--preview`（在当前 DeepSeek Harness 窗口上预览）、`--loop-only`、`--test-skip 秒`、`--repeats N`、`--render-subtitle 背景.png 输出.png 第几句`、`--show-menu`（自动弹出菜单，方便截图）、`--settings`。

## 授权

- 代码：[GPL-3.0](../LICENSE)：可以自由使用、学习和修改；发布改过的版本时必须同样以 GPL-3.0 开源，并保留原作者署名
- 动画素材：[CC BY-NC-ND 4.0](assets/LICENSE.md)，可以安装、分享，不能商用、不能改后再发布。分镜与动作设计由作者完成，画面由即梦（Seedance 模型）生成。**服饰与头饰是作者原创设计，保留所有权利，不在 CC 授权范围内**，未经许可不得用于其他作品、周边或 AI 生图参考；角色的脸型和鲸鳍耳参考了网上流传的 DeepSeek 拟人形象
- 声音：环境声来自即梦生成视频的原声（已去掉配乐），另叠两条 Freesound CC0 音效；配音见 [CREDITS.md](CREDITS.md)

---

**English (short):** An unofficial boot animation for DeepSeek Harness (bundle id `com.deepseek.dsh`) on macOS and Windows (`DeepSeek Harness.exe`), featuring a deep-sea archer whale girl. A tiny menu-bar helper overlays DeepSeek Harness's window with an intro animation, then fades into a translucent, click-through looping background. It never modifies DeepSeek Harness and can be installed alongside the Claude, GPT and Gemini boot animations. Install with the one-liners above (macOS 13+ / Windows 10–11); uninstall from the 🐟 menu or with `./uninstall.sh`. Code is GPL-3.0; animation assets are CC BY-NC-ND 4.0, except the costume and headpiece design, which is © wynneceles, all rights reserved.

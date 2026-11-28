# Windows 合并版 · Claude / GPT / Gemini / DeepSeek 四合一

把本仓库里 Windows 版的四个开机动画小程序（`claude/windows`、`codex/windows`、`antigravity/windows`、`deepseek/windows`）合并成一个程序：一个托盘图标、一个「启动设置」窗口、一个开机自启，同时管 Claude、ChatGPT（含 Codex）、Antigravity 和 DeepSeek Harness。和原版一样，它不修改这四个应用的任何文件，只在它们的窗口上盖一层浮层播放动画。每个应用有独立的播放器、暂停状态和窗口跟随。

<p align="center">
  <img src="docs/settings.png" width="560" alt="三应用时期的「启动设置」窗口">
  &nbsp;&nbsp;
  <img src="docs/traymenu.png" width="160" alt="托盘右键菜单">
</p>

截图沿用三应用版本，托盘图标和右键菜单已改版，以文字说明为准。

> 非官方同人项目，与 Anthropic、OpenAI、Google、DeepSeek 无关。Claude 是 Anthropic 的商标，ChatGPT、Codex 是 OpenAI 的商标，Gemini、Antigravity 是 Google 的商标，DeepSeek 是深度求索的商标。

## 安装（Windows 10 / 11）

在这个文件夹里打开 PowerShell，运行：

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

- **素材**：动画视频不重复放在这里。缺素材时，`install.ps1` 会从本仓库的 Releases（`v1.2.6` / `gpt-v1.2.5` / `gemini-v1.1.0` / `deepseek-v1.0.1`）取对应的 Windows 包，原样取出 `intro.mp4`、`loop.mp4`、`loop-x2.mp4`。`-OnlyAssets` 参数只下载素材。
- **已装原版**：安装时会迁移原版设置，并可用原版自带的 `uninstall.ps1` 卸掉已装的单独版，免得同一个窗口上叠两层动画。
- **编译**：只用 Windows 自带的 .NET Framework 编译器和 `d3dcompiler_47.dll`，不需要安装任何 SDK。单独运行 `build.ps1` 时，四份素材齐全即可离线编译。如缺开屏或循环视频，构建脚本会复用 `install.ps1 -OnlyAssets` 获取素材。
- **安装位置**：程序就在这个文件夹里运行，并设为开机自启。

## 用法

- **托盘图标**：Claude 版的三颗金色小星星。左键单击打开「启动设置」。右键是两级菜单：一级先选应用（只列这台电脑上装了的 Claude / GPT / Gemini / DeepSeek，装了几个就有几个；开着的后面带 ●）；二级是原版单独小程序那一整套：播放开屏动画、保留循环背景、背景不透明度、开屏音量、播放一次开屏动画、开启 / 关掉循环背景、暂停循环背景。二级菜单里调的只影响这一个应用；在「启动设置」里改的是总设置，四个应用一起变。
- **「启动设置」**：
  - 左栏：
    - 四个应用各带图标，打勾即启用；右边的「预览」会在该应用当前的窗口上播放一次开屏动画。
    - 播放开屏动画、保留循环背景两个开关。
    - 关闭窗口时缩小到托盘，还是完全退出。
  - 右栏：
    - 背景不透明度：拉杆或直接输入，预设 10%。
    - 背景适配：浅色 / 原样，见下一节。
    - 开屏音量：拉杆或直接输入，预设 0，也就是静音。
  - 右下角「设定」：收起正在播放的动画、打开设置文件夹、卸载。
  - 所有改动立刻生效。
- **跳过开屏**：开屏时在画面上任意位置点一下即可，代替右上角的「跳过」按钮。进入循环背景后浮层是点击穿透的。
- **什么时候播放**：只有应用真正新启动时才播。本程序启动时已经开着的应用，包括最小化、缩在托盘里的，都不会补播，之后最小化、还原也不会重播。

## 背景适配（只作用于循环背景）

四段动画都有暗色背景。原版把整段画面半透明地盖上去：浅色界面会被压出一层灰，亮的人物在白底上反而看不见；深色界面会被提亮一层。

选「浅色」时，用像素着色器（`src/ContrastBackground.hlsl`）在运行时处理：

1. **去背景**：按亮度把星空抠成透明，只留人物和光效。阈值按每段动画星空的实际亮度分别设定（`Profile.KeyLo` / `KeyHi`）。
2. **调色**：在原色上加饱和、加对比，中间调稍微压深，白底上颜色立得住。

「原样」使用主干的原生分层外壳和 WPF 子窗口，由系统混合整窗透明度。「浅色」使用逐像素透明窗口，让抠空的位置透出下面的应用。素材文件不做任何修改，开屏动画始终原样播放。

## 多块屏幕

浮层使用 Per-Monitor V2，按目标窗口的实际像素摆放。移动、缩放或最小化时由窗口事件通知浮层跟随，定时器补查漏报的位置和 DPI 变化。

## 播放诊断

`--bench` 在选定应用的真实窗口上运行，`--target-title` 可改为按测试窗口标题匹配。两者都支持 `--profile claude|gpt|antigravity|deepseek`，默认使用 Claude。例如：

```powershell
.\AIBootAnimations.exe --bench --profile deepseek --play loop --perf-log 2 --exit-after 30
```

`--perf-log` 按秒记录整个进程的 CPU、内存和句柄数。`--overlay-mode hosted|perpixel|opaque` 可强制选择渲染路径；其中 hosted 测原样背景，opaque 测不透明窗口。`--recycle-every N --standby-fail-test N` 可缩短待命播放器换新周期、模拟每第 N 次换新失败。失败时原播放器继续待命。

## 文件

| 路径 | 内容 |
|---|---|
| `src/AIBootAnimations.cs` | 合并版源码；开头注释写了改动说明 |
| `src/SettingsView.xaml` | 「启动设置」窗口的界面 |
| `src/ContrastBackground.hlsl` / `.ps` | 背景适配的着色器源码 / 编译结果 |
| `src/app.manifest`、`src/app.config` | 按屏幕 DPI 工作的清单 / 运行配置 |
| `src/app.ico` | 程序图标（`--render-ico` 生成） |
| `assets/<应用>/` | 各自的 `LICENSE.md`、`CREDITS.md`（复制自本仓库）；动画视频由 `install.ps1` 下载到这里 |
| `assets/fonts/` | 字幕字体 Libre Bodoni（SIL OFL） |
| `build.ps1` / `install.ps1` / `uninstall.ps1` | 编译 / 安装 / 卸载 |

- 设置：`%APPDATA%\AIBootAnimations\settings.json`，四个应用共用一份；`apps` 下是每个应用自己的开关和动画参数，字段和原版 `config.json` 一样。
- 日志：`%LOCALAPPDATA%\AIBootAnimations\log.txt`，每场播放的日志带应用名，共用进程的性能采样单独记录。

## 授权

- 代码：沿用本仓库的 GPL-3.0，保留原作者署名，并在源码开头写明修改说明。
- 动画素材：CC BY-NC-ND 4.0，见各 `assets/<应用>/LICENSE.md`。素材文件一个字节都没改。

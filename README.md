# AI Boot Animations · AI 应用开机动画

给 Mac 和 Windows 上的 AI 桌面应用加开机动画：打开应用时先放一段开屏，再透出真正的界面，留下一层半透明的动态背景。每个动画都是独立的小程序，**不修改应用本体**，随时一键卸载。

| 应用 | 动画 | 安装 |
|---|---|---|
| **Claude** 桌面端 | 月下持剑少女：线稿 → 上色 → 剑光、火凤、烟花，带音效和英文字幕；菜单栏 ✨ 可调背景浓度、开屏音量，可只留动态背景 | [claude/](claude/) |
| **ChatGPT** 桌面端（含 Codex） | 星空塔罗龙娘：发光线稿 → 展翅、七张牌亮起 → 光带环绕 → 光汇入手心 → 甩牌挡嘴，带音效、英文配音和字幕；菜单栏 🌙 可调，和 Claude 版可以同时装；Mac 和 Windows 都有 | [codex/](codex/) |
| **Antigravity**（Google 的 AI 编程工具） | Gemini 双剑少女：发光线稿 → 剑尖星芒 → 双剑相击，带音效、英文配音和字幕；菜单栏 ✦ 可调，和 Claude、GPT 版可以同时装；Mac 和 Windows 都有 | [antigravity/](antigravity/) |
| **DeepSeek Harness**（DeepSeek 的 AI 编程工具） | 大肥鱼深海弓手：发光线稿 → 跃出海面对月张弓 → 箭拖着光尾消失 → 月下睁眼，带音效、英文配音和字幕；菜单栏 🐟 可调，和 Claude、GPT、Gemini 版可以同时装；Mac 和 Windows 都有 | [deepseek/](deepseek/) |

更多动画陆续加入。

![Claude 开机动画预览](claude/docs/preview.jpg)

![GPT 开机动画预览](codex/docs/preview.jpg)

![Gemini 开机动画预览](antigravity/docs/preview.jpg)

![DeepSeek 开机动画预览](deepseek/docs/preview.jpg)

## 快速安装（以 Claude 为例）

### macOS

需要 macOS 13 或更新（Apple 芯片和 Intel 都行）。打开「终端」，粘贴这一行，回车：

```bash
curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/claude/web-install.sh | zsh
```

几秒钟就装好，菜单栏右边（电池左边）出现 ✨，并弹出设置窗口。不需要装 Xcode。

<details>
<summary>想自己从源码编译？</summary>

需要 Xcode 命令行工具（没有的话先运行 `xcode-select --install`）：

```bash
git clone https://github.com/wynneceles/ai-boot-animations.git
cd ai-boot-animations/claude
./install.sh
```

</details>

### Windows 10 / 11

打开「PowerShell」（开始菜单搜索 PowerShell），粘贴这一行，回车：

```powershell
[Net.ServicePointManager]::SecurityProtocol='Tls12'; irm https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/claude/windows/web-install.ps1 | iex
```

打不开 raw.githubusercontent.com 的话，把网址换成 `https://cdn.jsdelivr.net/gh/wynneceles/ai-boot-animations@main/claude/windows/web-install.ps1`。

会下载约 48MB，用 Windows 自带的编译器现场编译（不用装任何 SDK），装到**剩余空间最大的非系统盘**（例如 `D:\AIBootAnimations\ClaudeBootAnimation`，只有 C 盘时才装 C 盘；想自己指定位置，先运行 `$env:CBA_DIR='E:\某个文件夹'` 再粘贴安装命令），右下角托盘出现 ✨ 并弹出设置窗口。

## 菜单栏 ✨ 能干什么

装好后菜单栏（Windows 是右下角托盘）常驻一个小星星 ✨，点开就能调，**拖动时立刻生效**：

- **打开 Claude 时**：要不要播开屏动画、要不要留循环背景（两个都关 = 原装 Claude）
- **背景不透明度**：循环背景盖得多浓，0% 到 60%
- **开屏音量**：跟随系统音量再按比例调，系统静音时自动没声
- **现在，在 Claude 窗口上**：马上播一次开屏 / 开启循环背景 / 关掉
- **渲染显卡**（仅 Windows 双显卡电脑）：让 Windows 决定 / 省电（核显）/ 高性能（独显）。哪个更省电、更流畅因机器而异，选完自动重启小程序生效
- **设置窗口、设置文件、卸载**

<p align="center">
  <img src="claude/docs/menu.png" width="300" alt="菜单栏 ✨ 菜单">
  &nbsp;&nbsp;
  <img src="claude/docs/settings.png" width="330" alt="设置窗口">
</p>

详细说明、设置文件参数和卸载见 [claude/README.md](claude/README.md)。

## 授权

- 代码：[GPL-3.0](LICENSE)：可以自由使用和修改，发布改版时须同样开源并保留署名
- 动画素材：各文件夹 `assets/LICENSE.md` 单独说明（Claude 动画为 CC BY-NC-ND 4.0：可安装分享，不可商用、不可改后再发布）

> 非官方同人项目，与各 AI 应用的开发公司无关。Claude 是 Anthropic 的商标，ChatGPT、Codex 是 OpenAI 的商标，Gemini、Antigravity 是 Google 的商标，DeepSeek 是其所有者的商标。

---

**English:** Boot animations for AI desktop apps on macOS and Windows. Each one is a small menu-bar helper that overlays the app's window with an intro, then fades into a translucent, click-through looping background, without modifying the app itself. One-line install: `curl -fsSL https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/claude/web-install.sh | zsh` (macOS 13+, Apple Silicon or Intel). Windows 10/11: in PowerShell, `irm https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/claude/windows/web-install.ps1 | iex`.

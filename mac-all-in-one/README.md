# AI 开机动画 · Mac 四合一

一个菜单栏图标同时管四个开机动画：Claude、ChatGPT（含 Codex）、Antigravity（Gemini）、DeepSeek Harness。和单独版一样，不修改这四个应用的任何文件，只在它们的窗口上盖一层浮层播放动画。

- **菜单栏图标**：Claude 版的 ✨。四个应用里任意一个开着时才出现。
- **两级菜单**：一级先选应用（只列这台 Mac 上装了的应用，装了几个就有几个；开着的后面带 ●）；二级是单独版那一整套：播放开屏动画、保留循环背景、背景不透明度、开屏音量、播放一次开屏动画、开启 / 关掉循环背景、暂停循环背景。每个应用的设置互不影响。
- **设置窗口**：最上面先选应用，下面是那个应用的设置。在聚焦搜索里搜 AIBootAnimations 也能打开。

## 安装

```bash
cd mac-all-in-one && ./install.sh
```

需要 Xcode 命令行工具（`xcode-select --install`）。发现已经装着原版单独小程序时，会先把它们的设置搬过来，再问要不要卸掉（两个都开着会在同一个窗口上叠两层动画）。`--remove-old` / `--keep-old` 可以跳过询问。

卸载：菜单「卸载 AI 开机动画…」，或运行 `./uninstall.sh`。

## 文件

| 位置 | 内容 |
|---|---|
| `Sources/main.swift` | 程序本体。播放部分沿用 GPT 版（含 PR #10 的降耗改动） |
| `build.sh` | 编译通用程序，把四段动画从 `../claude` `../codex` `../antigravity` `../deepseek` 的 `assets/` 打包进去 |
| 设置 | `~/Library/Application Support/AIBootAnimations/<应用>.json` |
| 日志 | `~/Library/Logs/AIBootAnimations.log` |

## 致谢与授权

- 四合一的想法来自 Windows 合并版（PR #11，作者 q438241zy）。
- Mac 版的窗口跟随降耗来自 PR #10（作者 yechu12138-sudo）。
- 程序以 GNU GPL v3 发布。动画素材的授权见各子项目的 `assets/LICENSE.md`（CC BY-NC-ND 4.0，只许个人使用，禁止二次上传）。

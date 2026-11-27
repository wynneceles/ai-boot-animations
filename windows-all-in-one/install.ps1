# AI 开机动画 · 合并版 —— 安装 / 重新编译
# 编译 src\AIBootAnimations.cs → 项目根目录的 AIBootAnimations.exe（和 assets\ 放在一起）
# → 第一次安装时把原版三个小程序（Claude / GPT / Gemini 开机动画）的设置迁移过来，再用它们自带的 uninstall.ps1 卸掉，
#   免得同一个窗口上叠两层动画
# → 设为开机自启 → 启动并弹出设置窗口。
# 不会修改 Claude、ChatGPT、Antigravity 的任何文件。
# 用法：powershell -ExecutionPolicy Bypass -File install.ps1          （-OnlyAssets：只下载缺的动画素材，不编译不安装）
param([switch]$OnlyAssets)
$ErrorActionPreference = 'Stop'
$name = 'AIBootAnimations'
$root = $PSScriptRoot
$exe = Join-Path $root "$name.exe"
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path $run)) { New-Item -Path $run -Force | Out-Null }   # 有的电脑上这个键本来不存在

# 0. 动画素材（intro.mp4 / loop.mp4 / loop-x2.mp4）不放在本仓库里：缺的话从原作者仓库的 Release 下载对应的 Windows 包，
#    原样取出（文件一个字节都不改）。素材的授权和来源见 assets\<应用>\LICENSE.md、CREDITS.md
$releases = [ordered]@{
  claude      = @{ tag = 'v1.2.6';        zip = 'ClaudeBootAnimation-Windows.zip' }
  gpt         = @{ tag = 'gpt-v1.2.5';    zip = 'GPTBootAnimation-Windows.zip' }
  antigravity = @{ tag = 'gemini-v1.1.0'; zip = 'AntigravityBootAnimation-Windows.zip' }
}
$media = @('intro.mp4', 'loop.mp4', 'loop-x2.mp4')
foreach ($k in $releases.Keys) {
  $dst = Join-Path $root "assets\$k"
  if (-not ($media | Where-Object { -not (Test-Path (Join-Path $dst $_)) })) { continue }
  $r = $releases[$k]
  $url = "https://github.com/wynneceles/ai-boot-animations/releases/download/$($r.tag)/$($r.zip)"
  $tmp = Join-Path $env:TEMP ('aiba-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $tmp, $dst | Out-Null
  try {
    Write-Host "下载 $k 的动画素材（原作者 Release $($r.tag)，约 50MB）…"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$tmp\pkg.zip"
    Expand-Archive -Path "$tmp\pkg.zip" -DestinationPath "$tmp\x" -Force
    $assets = Get-ChildItem "$tmp\x" -Recurse -Directory -Filter assets | Select-Object -First 1
    foreach ($f in $media) { Copy-Item (Join-Path $assets.FullName $f) $dst -Force }
  } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
foreach ($k in $releases.Keys) {
  foreach ($f in @('intro.mp4', 'loop.mp4')) {
    if (-not (Test-Path (Join-Path $root "assets\$k\$f"))) { throw "缺少素材 assets\$k\$f" }
  }
}
if ($OnlyAssets) { Write-Host '动画素材已就绪。'; return }

# 1. 停掉正在运行的合并版，再编译
Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process $name -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
& "$root\build.ps1"

# 2. 原版三个小程序：先迁移设置（合并版还没有这份设置时），再卸掉
$cfgDir = Join-Path $env:APPDATA $name
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
$old = [ordered]@{ claude = 'ClaudeBootAnimation'; gpt = 'GPTBootAnimation'; antigravity = 'AntigravityBootAnimation' }
foreach ($k in $old.Keys) {
  $n = $old[$k]
  $src = Join-Path $env:APPDATA "$n\config.json"
  $dst = Join-Path $cfgDir "$k.json"
  if ((Test-Path $src) -and -not (Test-Path $dst)) { Copy-Item $src $dst; Write-Host "已迁移 $n 的设置 → $dst" }
  $reg = (Get-ItemProperty -Path $run -Name $n -ErrorAction SilentlyContinue).$n
  $oldDir = if ($reg) { Split-Path ($reg.Trim('"')) -Parent } else { $null }
  if ($oldDir -and (Test-Path (Join-Path $oldDir 'uninstall.ps1'))) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $oldDir 'uninstall.ps1') -Quiet
    Write-Host "已卸载原版 $n（$oldDir）"
  } elseif ($reg -or (Get-Process $n -ErrorAction SilentlyContinue)) {
    Get-Process $n -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty -Path $run -Name $n -ErrorAction SilentlyContinue
    Write-Host "已停掉原版 $n"
  }
}

# 3. 开机自启 + 启动
Set-ItemProperty -Path $run -Name $name -Value "`"$exe`""
Start-Process $exe -ArgumentList '--settings'
Write-Host ''
Write-Host "装好了：$exe"
Write-Host '   右下角托盘只有一个图标（金色星星 + 月牙 + 蓝紫四芒星）：左键打开「启动设置」，右键是「启动设置 / 退出」。'
Write-Host '   设置文件：%APPDATA%\AIBootAnimations\settings.json；日志：%LOCALAPPDATA%\AIBootAnimations\log.txt'

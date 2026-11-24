# Gemini Windows test on a real PC (no Antigravity installed: play against Notepad)
$ErrorActionPreference = 'Continue'; $ProgressPreference = 'SilentlyContinue'
Clear-Host
'== GPT after upgrade =='
Get-Process GPTBootAnimation -ErrorAction SilentlyContinue | ForEach-Object { 'running: ' + $_.Path }
Remove-Item 'D:\Apps\CodexBootAnimation\GPTBootAnimation.exe' -Force -ErrorAction SilentlyContinue
'old folder now: ' + ((Get-ChildItem 'D:\Apps\CodexBootAnimation' -ErrorAction SilentlyContinue | ForEach-Object Name) -join ', ')
'== Gemini install =='
$u = 'https://raw.githubusercontent.com/wynneceles/ai-boot-animations/ci-results/ci-results/test-packages'
$t = 'D:\aba-test'; New-Item -ItemType Directory -Force $t | Out-Null
Invoke-WebRequest -UseBasicParsing "$u/AntigravityBootAnimation-Windows.zip" -OutFile "$t\a.zip"
Expand-Archive "$t\a.zip" "$t\a" -Force
powershell -NoProfile -ExecutionPolicy Bypass -File "$t\a\AntigravityBootAnimation-Windows\install.ps1"
Start-Sleep 3
Get-Process AntigravityBootAnimation -ErrorAction SilentlyContinue | ForEach-Object { 'running: ' + $_.Path }
'Run key: ' + (Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).AntigravityBootAnimation
'== play intro over Notepad (30s) =='
$np = Start-Process notepad -WindowStyle Maximized -PassThru; Start-Sleep 3
$title = (Get-Process -Id $np.Id -ErrorAction SilentlyContinue).MainWindowTitle
if (-not $title) { $title = (Get-Process notepad | Where-Object MainWindowTitle | Select-Object -First 1).MainWindowTitle }
'target window: ' + $title
$exe = 'D:\AIBootAnimations\AntigravityBootAnimation\AntigravityBootAnimation.exe'
$p = Start-Process $exe -PassThru -ArgumentList "--target-title `"$title`" --play intro --exit-after 30"
$p | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
Get-Process notepad -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -eq $title | Stop-Process -Force
'== log tail =='
Get-Content "$env:LOCALAPPDATA\AntigravityBootAnimation\log.txt" -Encoding UTF8 -Tail 14

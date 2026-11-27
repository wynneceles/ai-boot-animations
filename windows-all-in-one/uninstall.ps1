# AI 开机动画 · 合并版 —— 卸载：停掉程序、删开机自启、删设置和日志、删「用高性能显卡」偏好。
# Claude、ChatGPT、Antigravity 本来就没被改过。
# 项目文件夹（程序、源码、动画素材）默认保留；加 -RemoveFiles 才连文件夹一起删。
param([switch]$Quiet, [switch]$RemoveFiles)
$name = 'AIBootAnimations'
$root = $PSScriptRoot
Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA $name) -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA $name) -Recurse -Force -ErrorAction SilentlyContinue
# 删掉启动时写的「用高性能显卡」偏好
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' -Name (Join-Path $root "$name.exe") -ErrorAction SilentlyContinue
if ($RemoveFiles) {
  # 卸载脚本自己就在项目文件夹里，延后删
  Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$root`"" -WindowStyle Hidden
}
if (-not $Quiet) {
  if ($RemoveFiles) { Write-Host "已卸载，项目文件夹 $root 也会被删除。" }
  else { Write-Host "已卸载（开机自启、设置和日志已删除）。项目文件夹 $root 还在，想重新装就运行 install.ps1。" }
}

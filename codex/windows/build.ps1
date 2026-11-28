# 用 Windows 自带的 .NET Framework 编译器把 src\GPTBootAnimation.cs 编成 exe，不需要装任何 SDK
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1 [-Out 输出目录]
param([string]$Out = "$PSScriptRoot\build")
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
$csc = "$fw\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到系统自带的 C# 编译器（$csc）。需要 Windows 10/11。" }
$wpf = "$fw\WPF"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
# icon只在构建时使用，生成到临时目录，不会复制到build文件夹
$icoDir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $icoDir | Out-Null
$icoProgram = Join-Path $icoDir "MakeIcon.exe"
$icoPath = Join-Path $icoDir "gpt.ico"
$icoRefs = @("$fw\System.Drawing.dll") | ForEach-Object { "/r:$_" }
$icoArgs = @('/nologo', '/target:exe', '/optimize+', '/codepage:65001', '/platform:anycpu', "/out:$icoProgram") + $icoRefs + @("$PSScriptRoot\src\MakeIcon.cs")
# 构建并运行
& $csc @icoArgs
if ($LASTEXITCODE -ne 0) { throw "图标生成器编译失败" }
& $icoProgram $icoPath
if ($LASTEXITCODE -ne 0) { throw "图标生成失败" }
$refs = @("$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll",
          "$fw\System.Xaml.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
          "$fw\System.Web.Extensions.dll", "$fw\System.Core.dll", "$fw\System.Management.dll") | ForEach-Object { "/r:$_" }
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/platform:anycpu', "/win32icon:$icoPath", "/out:$Out\GPTBootAnimation.exe") + $refs + @("$PSScriptRoot\src\GPTBootAnimation.cs")
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
New-Item -ItemType Directory -Force -Path "$Out\assets" | Out-Null
Copy-Item "$PSScriptRoot\assets\*" "$Out\assets\" -Force
Copy-Item "$PSScriptRoot\uninstall.ps1" "$Out\" -Force
Write-Host "编译完成：$Out\GPTBootAnimation.exe"

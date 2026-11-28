# 用 Windows 自带的工具编译，不需要装任何 SDK：
#   1. src\ContrastBackground.hlsl → src\ContrastBackground.ps（系统自带的 d3dcompiler_47.dll；没有的话沿用仓库里编好的 .ps）
#   2. src\AIBootAnimations.cs → AIBootAnimations.exe（.NET Framework 自带的 csc.exe），
#      嵌入 src\app.manifest（按每块屏幕的 DPI 工作）、SettingsView.xaml（「启动设置」界面）、ContrastBackground.ps
#   3. src\app.config → AIBootAnimations.exe.config（打开 WPF 的按屏幕 DPI 缩放）
# 默认输出到项目根目录：程序运行时从 exe 同目录的 assets\ 读素材
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1 [-Out 输出目录]
param([string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'

# ── 1. 着色器 ──
$hlsl = Join-Path $src 'ContrastBackground.hlsl'
$ps = Join-Path $src 'ContrastBackground.ps'
if (Test-Path "$env:WINDIR\System32\d3dcompiler_47.dll") {
  if (-not ('ShaderBuild' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
[ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ID3DBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
public static class ShaderBuild {
  [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
  static extern int D3DCompile(byte[] src, UIntPtr size, string name, IntPtr defines, IntPtr include,
                               string entry, string target, uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);
  public static byte[] Compile(string path, string entry, string target, out string error) {
    var bytes = System.IO.File.ReadAllBytes(path);
    ID3DBlob code, err;
    int hr = D3DCompile(bytes, (UIntPtr)bytes.Length, System.IO.Path.GetFileName(path), IntPtr.Zero, IntPtr.Zero, entry, target, 1u << 15, 0, out code, out err);
    error = err == null ? null : Marshal.PtrToStringAnsi(err.GetBufferPointer(), (int)err.GetBufferSize());
    if (hr < 0 || code == null) return null;
    var res = new byte[(int)code.GetBufferSize()];
    Marshal.Copy(code.GetBufferPointer(), res, 0, res.Length);
    return res;
  }
}
'@
  }
  $err = $null
  $bytes = [ShaderBuild]::Compile($hlsl, 'main', 'ps_2_0', [ref]$err)
  if (-not $bytes) { throw "着色器编译失败：$err" }
  [IO.File]::WriteAllBytes($ps, $bytes)
  Write-Host "着色器编译完成：$ps（$($bytes.Length) 字节）"
} elseif (-not (Test-Path $ps)) {
  throw '找不到 d3dcompiler_47.dll，也没有编好的 src\ContrastBackground.ps'
}

# ── 2. 程序 ──
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
$csc = "$fw\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到系统自带的 C# 编译器（$csc）。需要 Windows 10/11。" }
$wpf = "$fw\WPF"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$refs = @("$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll",
          "$fw\System.Xaml.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
          "$fw\System.Web.Extensions.dll", "$fw\System.Core.dll", "$fw\System.Management.dll") | ForEach-Object { "/r:$_" }
$exe = Join-Path $Out 'AIBootAnimations.exe'
$cscArgs = @('/nologo', '/langversion:5', '/target:winexe', '/optimize+', '/codepage:65001', '/platform:anycpu', "/out:$exe",
             "/win32manifest:$src\app.manifest",
             "/resource:$src\SettingsView.xaml,SettingsView.xaml",
             "/resource:$ps,ContrastBackground.ps")
# 程序图标（exe 在资源管理器里显示的图标）。重新生成：AIBootAnimations.exe --render-ico src\app.ico
if (Test-Path "$src\app.ico") { $cscArgs += "/win32icon:$src\app.ico" }
$cscArgs += $refs + @("$src\AIBootAnimations.cs")
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

# ── 3. 运行配置 ──
Copy-Item "$src\app.config" (Join-Path $Out 'AIBootAnimations.exe.config') -Force

# 与安装共用 Release 素材清单。已备齐的工作副本可以直接离线构建；
# 只有缺少播放必需的视频时才调用仅取素材的入口，loop-x2 是可选优化。
$profiles = @('claude', 'gpt', 'antigravity', 'deepseek')
$missing = foreach ($profileKey in $profiles) {
  foreach ($file in @('intro.mp4', 'loop.mp4')) {
    $asset = Join-Path $PSScriptRoot "assets\$profileKey\$file"
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { $asset }
  }
}
if ($missing) { & "$PSScriptRoot\install.ps1" -OnlyAssets }

# 输出到别的目录时，把素材和卸载脚本也带过去
if ((Resolve-Path $Out).Path.TrimEnd('\') -ne (Resolve-Path $PSScriptRoot).Path.TrimEnd('\')) {
  Copy-Item "$PSScriptRoot\assets" $Out -Recurse -Force
  Copy-Item "$PSScriptRoot\uninstall.ps1" $Out -Force
}
Write-Host "编译完成：$exe"

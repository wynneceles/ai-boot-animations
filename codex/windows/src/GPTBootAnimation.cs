// GPT 开机动画 · Windows 版 —— 外置浮层小程序
// 检测 ChatGPT 桌面端启动 → 盖在它的主窗口上播放开屏动画 → 进入循环帧后半透明、点击穿透，作为循环背景一直播放。
// 不修改 ChatGPT 的任何文件。用系统自带的 .NET Framework 4.x 编译器编译（C# 5 语法），不需要装任何 SDK。
//
// Copyright (C) 2026 wynneceles
// 本程序以 GNU GPL v3 发布，见仓库根目录 LICENSE。
// This program is free software under the GNU General Public License v3.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace GPTBootAnimation
{
    // ───────────────────────── 配置 ─────────────────────────

    public class Subtitle
    {
        public string Text; public double Start; public double End;
        public Subtitle(string t, double s, double e) { Text = t; Start = s; End = e; }
    }

    public class Config
    {
        public bool PlayIntro = true;            // ChatGPT 启动时播放开屏动画
        public bool LoopBackground = true;       // 开屏后保留半透明循环背景
        public double FadeStartSeconds = 11.4;   // 成片里进入循环帧的时间点：甩牌落定后第 342 帧
        public double FadeDurationSeconds = 1.2; // 从不透明淡到半透明用多久
        public double ResidualAlpha = 0.25;      // 循环背景不透明度
        public double IntroVolume = 1.0;         // 开屏音量（再乘系统音量）
        public bool ShowSubtitles = true;
        public double SubtitleFadeSeconds = 0.5;
        public double SubtitleWidthRatio = 0.6;  // 第一句字幕约占窗口宽度的比例
        public List<Subtitle> Subtitles = new List<Subtitle> {
            new Subtitle("Every answer... is already in my hand.", 3.2, 7.2),
            new Subtitle("All I lack... is your question.", 8.65, 11.6),
        };

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GPTBootAnimation"); } }
        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        static double D(Dictionary<string, object> d, string k, double def)
        {
            object v; if (d.TryGetValue(k, out v) && v != null) { try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { } }
            return def;
        }
        static bool B(Dictionary<string, object> d, string k, bool def)
        {
            object v; if (d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return def;
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d != null)
                    {
                        c.PlayIntro = B(d, "playIntro", c.PlayIntro);
                        c.LoopBackground = B(d, "loopBackground", c.LoopBackground);
                        c.FadeStartSeconds = D(d, "fadeStartSeconds", c.FadeStartSeconds);
                        c.FadeDurationSeconds = D(d, "fadeDurationSeconds", c.FadeDurationSeconds);
                        c.ResidualAlpha = D(d, "residualAlpha", c.ResidualAlpha);
                        c.IntroVolume = D(d, "introVolume", c.IntroVolume);
                        c.ShowSubtitles = B(d, "showSubtitles", c.ShowSubtitles);
                        c.SubtitleFadeSeconds = D(d, "subtitleFadeSeconds", c.SubtitleFadeSeconds);
                        c.SubtitleWidthRatio = D(d, "subtitleWidthRatio", c.SubtitleWidthRatio);
                        object subs;
                        if (d.TryGetValue("subtitles", out subs) && subs is object[])
                        {
                            var list = new List<Subtitle>();
                            foreach (var o in (object[])subs)
                            {
                                var s = o as Dictionary<string, object>;
                                if (s == null || !s.ContainsKey("text")) continue;
                                list.Add(new Subtitle(Convert.ToString(s["text"]), D(s, "start", 0), D(s, "end", 0)));
                            }
                            c.Subtitles = list;
                        }
                    }
                }
            }
            catch (Exception e) { Log.W("设置文件读不了，用默认值：" + e.Message); }
            c.Save(); // 补齐新字段
            return c;
        }

        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string L(bool v) { return v ? "true" : "false"; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var js = new JavaScriptSerializer();
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"fadeDurationSeconds\" : " + N(FadeDurationSeconds) + ",\n");
                sb.Append("  \"fadeStartSeconds\" : " + N(FadeStartSeconds) + ",\n");
                sb.Append("  \"introVolume\" : " + N(IntroVolume) + ",\n");
                sb.Append("  \"loopBackground\" : " + L(LoopBackground) + ",\n");
                sb.Append("  \"playIntro\" : " + L(PlayIntro) + ",\n");
                sb.Append("  \"residualAlpha\" : " + N(ResidualAlpha) + ",\n");
                sb.Append("  \"showSubtitles\" : " + L(ShowSubtitles) + ",\n");
                sb.Append("  \"subtitleFadeSeconds\" : " + N(SubtitleFadeSeconds) + ",\n");
                sb.Append("  \"subtitleWidthRatio\" : " + N(SubtitleWidthRatio) + ",\n");
                sb.Append("  \"subtitles\" : [\n");
                for (int i = 0; i < Subtitles.Count; i++)
                {
                    var s = Subtitles[i];
                    sb.Append("    {\n      \"end\" : " + N(s.End) + ",\n      \"start\" : " + N(s.Start) + ",\n      \"text\" : " + js.Serialize(s.Text) + "\n    }");
                    sb.Append(i < Subtitles.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ]\n}\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Log.W("设置文件写不了：" + e.Message); }
        }
    }

    // ───────────────────────── 日志 ─────────────────────────

    /// 双显卡笔记本上 Windows 默认让新程序用核显，播 4K 视频会掉帧。
    /// 启动时检查有没有两块以上显卡：有，就在「设置 → 系统 → 显示 → 图形」里把本程序设为「高性能」（等同用户手动去选）。
    /// 用户自己在那里选过（不管选了什么）就不再动；只有一块显卡什么都不做。卸载时 uninstall.ps1 会删掉这一项。
    public static class GpuPref
    {
        const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
        public static void Ensure()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (k == null) return;
                    var cur = k.GetValue(exe) as string;
                    if (cur != null) { Log.W("显卡偏好已有设置（" + cur + "），不改动"); return; }
                    var gpus = ListGpus();
                    if (gpus.Count < 2) { Log.W("显卡：" + (gpus.Count == 0 ? "未识别" : string.Join("、", gpus)) + "，不用切换独显"); return; }
                    k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
                    Log.W("检测到多块显卡（" + string.Join("、", gpus) + "），已设为用高性能显卡渲染");
                }
            }
            catch (Exception ex) { Log.W("独显设置失败：" + ex.Message); }
        }

        /// 列出真实显卡（排除远程桌面、虚拟显示器这类假适配器）
        static List<string> ListGpus()
        {
            var list = new List<string>();
            using (var q = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
            {
                foreach (System.Management.ManagementBaseObject o in q.Get())
                {
                    var name = (o["Name"] as string ?? "").Trim();
                    if (System.Text.RegularExpressions.Regex.IsMatch(name, "NVIDIA|GeForce|Quadro|RTX|AMD|Radeon|Intel|Arc", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        list.Add(name);
                }
            }
            return list;
        }
    }

    public static class Log
    {
        public static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTBootAnimation", "log.txt");
        static readonly object Lock = new object();
        public static void W(string msg)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // ───────────────────────── Win32 ─────────────────────────

    public static class Native
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        /// 每块屏单独按自己的缩放算坐标（Per-Monitor V2）。只认登录时缩放（SetProcessDPIAware）的话，
        /// 目标窗口在缩放不同的副屏上、或改了缩放没注销时，读到的窗口位置和摆浮层用的坐标对不上，浮层会缩小、错位（#7）。
        /// 老系统没有新接口就一级级往回退
        public static string UseDpiAwareness()
        {
            string before = "";
            try { before = "（之前=" + GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) + "）"; } catch { }
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return "每块屏单独缩放 V2" + before; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return "每块屏单独缩放" + before; } catch { }
            try { SetProcessDPIAware(); } catch { }
            return "只认登录时缩放" + before;
        }
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);

        /// 窗口所在显示器的整块屏幕矩形，物理像素
        public static bool MonitorRect(IntPtr h, out RECT r)
        {
            r = new RECT();
            IntPtr mon = MonitorFromWindow(h, 2 /*MONITOR_DEFAULTTONEAREST*/);
            if (mon == IntPtr.Zero) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            r = mi.rcMonitor; return true;
        }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int i, int v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] public static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);
        [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr h);
        /// 显示器亮 / 灭 / 变暗的通知
        public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        public const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000, WS_EX_APPWINDOW = 0x40000;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
        public const uint LWA_ALPHA = 2, GW_OWNER = 4;

        public static long GetLong(IntPtr h, int i) { return IntPtr.Size == 8 ? GetWindowLongPtr64(h, i).ToInt64() : GetWindowLong32(h, i); }
        public static void SetLong(IntPtr h, int i, long v)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, i, new IntPtr(v)); else SetWindowLong32(h, i, (int)v);
        }

        /// 窗口在屏幕上实际占的矩形（去掉 Windows 10/11 看不见的阴影边框），物理像素
        public static bool Bounds(IntPtr h, out RECT r)
        {
            if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Right > r.Left) return true;
            return GetWindowRect(h, out r);
        }
        public static bool Cloaked(IntPtr h)
        {
            int v; return DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out v, 4) == 0 && v != 0;
        }
        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
    }

    // ───────────────────────── 找 ChatGPT 的主窗口 ─────────────────────────

    public struct Target { public IntPtr Hwnd; public uint Pid; public bool Valid { get { return Hwnd != IntPtr.Zero; } } }

    public static class Finder
    {
        /// 测试用：按窗口标题找（比如记事本），不找 ChatGPT
        public static string TestTitle;
        static readonly Dictionary<uint, string> NameCache = new Dictionary<uint, string>();

        static string ProcName(uint pid)
        {
            string n;
            if (NameCache.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = ""; }
            if (NameCache.Count > 500) NameCache.Clear();
            NameCache[pid] = n;
            return n;
        }

        /// 找 ChatGPT 最大的那个可见主窗口。ChatGPT 是 Electron 程序，会有好几个进程和隐藏窗口，只挑看得见、够大的顶层窗口
        public static Target Find()
        {
            Target best = new Target(); long bestArea = 0;
            uint self = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.Cloaked(h)) return true;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;
                if (TestTitle != null)
                {
                    if (Native.TitleOf(h).IndexOf(TestTitle, StringComparison.OrdinalIgnoreCase) < 0) return true;
                }
                else
                {
                    // ChatGPT 桌面端（Windows 上和 Codex 是同一个应用，包名 OpenAI.Codex，窗口进程 ChatGPT.exe）；单独装的 Codex 窗口进程可能叫 Codex.exe
                    string pn = ProcName(pid);
                    if (!string.Equals(pn, "chatgpt", StringComparison.OrdinalIgnoreCase) && !string.Equals(pn, "codex", StringComparison.OrdinalIgnoreCase)) return true;
                    if (Native.GetWindowTextLength(h) == 0) return true;
                }
                Native.RECT r; if (!Native.Bounds(h, out r)) return true;
                int w = r.Right - r.Left, ht = r.Bottom - r.Top;
                if (w < 400 || ht < 300) return true;
                long area = (long)w * ht;
                if (area > bestArea) { bestArea = area; best.Hwnd = h; best.Pid = pid; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        public static bool ProcessAlive(uint pid)
        {
            try { return !Process.GetProcessById((int)pid).HasExited; } catch { return false; }
        }
    }

    // ───────────────────────── 字幕 ─────────────────────────

    /// 英文花体字幕：Libre Bodoni 斜体，白字 + 黑色描边 + 柔和阴影
    public class SubtitleElement : FrameworkElement
    {
        public static FontFamily Font = new FontFamily("Georgia");
        string text;
        public double WidthRatio = 0.6;
        public string RefText = "Under the moonlight... the blade awakens.";

        public SubtitleElement() { IsHitTestVisible = false; Opacity = 0; }
        public void SetText(string t) { text = t; InvalidateVisual(); }

        FormattedText Make(string s, double size)
        {
#pragma warning disable 618
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal), size, Brushes.White);
#pragma warning restore 618
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(text) || ActualWidth < 10) return;
            double W = ActualWidth, H = ActualHeight;
            // 字号：让第一句约占窗口宽度的 WidthRatio；窗口特别扁时按高度限一下
            double refW = Make(RefText, 100).WidthIncludingTrailingWhitespace;
            double size = refW > 0 ? W * WidthRatio * 100 / refW : 24;
            size = Math.Max(18, Math.Min(size, H * 0.09));
            var ft = Make(text, size);
            double x = (W - ft.Width) / 2, y = H * 0.85 - ft.Height / 2; // 字的中线在离底边 15% 处
            var geo = ft.BuildGeometry(new Point(x, y));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)), size * 0.1) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
            dc.DrawGeometry(Brushes.White, null, geo);
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = size * 0.25, ShadowDepth = 0, Opacity = 0.75 };
        }
    }

    // ───────────────────────── 开发用参数（排查循环背景卡住）─────────────────────────

    public static class Dev
    {
        public static double LoopCutSec = 0;    // --loop-cut 秒：循环段放到这里就提前换播放器，接缝次数成倍增加，几分钟跑完平时几小时的接缝
        public static double StallTestSec = 0;  // --stall-test 秒：每隔这么久故意把正在放的播放器暂停，看自愈能不能接回来
        public static int StallMs = 3000;       // 画面多久没动算卡住
        public static int StandbyFailEvery = 0; // --standby-fail-test N：每第 N 次换待命播放器时故意给个不存在的文件，测换失败时画面不受影响（#7）
        public static int RecycleEvery = 10;    // --recycle-every 圈：每放这么多圈，把后台待命的播放器换成新的（0 = 不换）
        public static double HeartbeatSec = 600; // --heartbeat 秒：循环背景每隔多久往日志里报一次平安
        public static double PerfSec = 0;       // --perf-log 秒：每隔这么久往日志里记一次 CPU / 内存（测性能用）
        // --overlay-mode：浮层怎么做。hosted = 系统分层外壳窗口 + 里面嵌 WPF 画面（默认）；
        // perpixel = 老的 WPF 逐像素透明窗口（只为对比性能）；opaque = 完全不透明（只为测性能下限，画面不对）
        public static string OverlayMode = "hosted";
        public static bool PerPixel { get { return OverlayMode == "perpixel"; } }
        public static bool Hosted { get { return OverlayMode == "hosted"; } }
    }

    /// 本进程 CPU 占用：两次采样之间用掉的 CPU 时间 ÷ 经过的时间 ÷ 核数，跟任务管理器「CPU」那一列同一个算法
    public static class Perf
    {
        static TimeSpan lastCpu; static DateTime lastWall;
        public static string Sample()
        {
            try
            {
                using (var p = Process.GetCurrentProcess())
                {
                    var cpu = p.TotalProcessorTime; var now = DateTime.UtcNow;
                    string pct = "";
                    if (lastWall != DateTime.MinValue)
                    {
                        double wall = (now - lastWall).TotalMilliseconds;
                        if (wall > 0) pct = (100.0 * (cpu - lastCpu).TotalMilliseconds / wall / Environment.ProcessorCount).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    }
                    lastCpu = cpu; lastWall = now;
                    return "CPU " + (pct == "" ? "（首次采样）" : pct) + "，内存 " + (p.WorkingSet64 / 1048576) + "MB（私有 " + (p.PrivateMemorySize64 / 1048576) + "MB），句柄 " + p.HandleCount;
                }
            }
            catch (Exception ex) { return "性能采样失败：" + ex.Message; }
        }
    }

    // ───────────────────────── 一次播放 ─────────────────────────

    /// 浮层的外壳：系统自己的分层窗口，整窗透明度用 SetLayeredWindowAttributes 交给桌面合成器在显卡上混合；
    /// WPF 画面嵌在里面当子窗口，照常用显卡画。
    /// 不直接用 WPF 窗口：WPF 的窗口要半透明只能开逐像素透明（每帧把整块画面拷回内存、CPU 混合，#9 的 CPU 20%+ 就是它），
    /// 自己给 WPF 窗口加分层样式又会被 WPF 当场拿掉
    public class HostForm : WF.Form
    {
        public bool ClickThrough;
        public delegate IntPtr MsgHook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled);
        public MsgHook OnMsg;
        public HostForm(string title)
        {
            FormBorderStyle = WF.FormBorderStyle.None; ShowInTaskbar = false; BackColor = SD.Color.Black;
            StartPosition = WF.FormStartPosition.Manual; Location = new SD.Point(-32000, -32000); Size = new SD.Size(10, 10); Text = title;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override WF.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000) | 0x02000000;   // WS_POPUP | WS_CLIPCHILDREN
                cp.ExStyle = (cp.ExStyle | 0x80000 /*LAYERED*/ | 0x80 /*TOOLWINDOW*/ | 0x8000000 /*NOACTIVATE*/ | (ClickThrough ? 0x20 : 0)) & ~0x40000 /*APPWINDOW*/;
                return cp;
            }
        }
        protected override void WndProc(ref WF.Message m)
        {
            if (OnMsg != null)
            {
                bool handled = false;
                try { OnMsg(m.HWnd, m.Msg, m.WParam, m.LParam, ref handled); } catch (Exception ex) { Log.W("浮层消息处理出错：" + ex.Message); }
            }
            base.WndProc(ref m);
        }
    }

    public class Show
    {
        enum Phase { Intro, Revealed, Ending, Done }
        Phase phase = Phase.Intro;

        public readonly Target Target;
        Config config;
        readonly bool withIntro;
        readonly Action onDone;

        Window overlay, top;
        IntPtr overlayH, topH;
        HostForm host; HwndSource childSrc; IntPtr childH;   // hosted 模式：外壳窗口和里面的 WPF 画面
        Grid grid, root;
        int styleFixes;
        MediaElement intro, loopA, loopB, activeLoop;
        // 换播放器时：新的那个真正出帧之前，旧的一直盖在上面（不然机器忙的时候会闪黑）
        MediaElement swapFrom, swapTo; DateTime swapT0; bool swapKicked;
        // 重叠交接：loop-x2.mp4 是循环段连放两遍，前后两半画面一模一样。
        // 正在放的播放器走到中点（第二遍开头）时，另一个从头开始放，两个同时放着同样的画面；
        // 新的稳稳走起来后再换到上面、旧的倒回开头待命。谁都放不到文件末尾，也就没有「放完再接」那一下闪。
        public static bool ForceLegacyLoop;   // 测试用：--legacy-loop 强制老办法
        bool overlapLoop;
        string loopFile = "loop.mp4";
        MediaElement handFrom, handTo; DateTime handT0;
        double handLead;                      // 新播放器起步慢多少秒，下次提前这么多开播（自动学）
        int handoffs;

        // ── 循环背景自愈 ──
        // 循环靠「放完 → 换另一个播放器接着放」一环扣一环，任何一环断了（没收到放完通知、新播放器没动起来、
        // 锁屏 / 睡眠 / 显示器休眠后画面冻住）以前都会永远停在一帧。现在每 0.25 秒看一眼进度，卡住就重建播放器。
        readonly Stopwatch clock = Stopwatch.StartNew();
        long nextSampleMs, lastSampleMs, lastMoveMs, lastRecoverMs = -1, nextBeatMs, nextStallTestMs, pendingAt;
        double lastPos = -1;
        int laps, recoveries, failStreak, missedEnds;
        bool verifying;
        string pendingWhy;
        MediaElement retiring; long retireT0;   // 重建时旧画面先盖着，新播放器动起来再撤
        Dispatcher disp;
        SessionSwitchEventHandler onSession;
        EventHandler onDisplay;
        IntPtr powerNotify;
        int displayState = -1;

        SubtitleElement sub;
        Border skipBtn;
        DispatcherTimer tick;
        int currentSub = -1;
        bool introGone, hidden;
        Native.RECT lastRect;

        // 整窗透明度的渐变
        double alpha, alphaFrom, alphaTo; DateTime alphaT0; double alphaDur = -1; Action alphaThen;
        bool clickThrough;
        // 开屏音量渐变
        double volFrom, volTo; DateTime volT0; double volDur = -1;

        static string Asset(string name) { return Path.Combine(App.AssetsDir, name); }

        public Show(Target t, Config c, bool withIntro, Action onDone)
        {
            Target = t; config = c; this.withIntro = withIntro; this.onDone = onDone;

            grid = new Grid { Background = withIntro ? Brushes.Black : Brushes.Transparent, ClipToBounds = true };
            // 有 loop-x2.mp4（循环段连放两遍）就用「重叠交接」；
            // 用户放了自己的 loop.mp4（比 loop-x2.mp4 新）或只有 loop.mp4，就用老办法，免得换的视频被无视
            string x2 = Asset("loop-x2.mp4"), single = Asset("loop.mp4");
            overlapLoop = !ForceLegacyLoop && File.Exists(x2)
                && !(File.Exists(single) && File.GetLastWriteTimeUtc(single) > File.GetLastWriteTimeUtc(x2).AddSeconds(2));
            loopFile = overlapLoop ? "loop-x2.mp4" : "loop.mp4";
            Log.W(overlapLoop ? "循环段：重叠交接（loop-x2.mp4）" : "循环段：放完再接（loop.mp4）");
            loopA = MakeLoopPlayer();
            loopB = MakeLoopPlayer();
            Panel.SetZIndex(loopA, 1); Panel.SetZIndex(loopB, 0);
            grid.Children.Add(loopA); grid.Children.Add(loopB);
            activeLoop = loopA;

            if (withIntro)
            {
                intro = MakePlayer("intro.mp4", false);
                intro.Volume = config.IntroVolume;
                Panel.SetZIndex(intro, 2);
                grid.Children.Add(intro);
                intro.MediaEnded += delegate { IntroEnded(); };
                intro.MediaFailed += delegate (object s, ExceptionRoutedEventArgs e) { Log.W("开屏视频播不了：" + e.ErrorException.Message); };
            }
            else introGone = true;

            // 一个窗口三层：最下面视频（整体调透明度做半透明背景），上面字幕和「跳过」（不跟着变淡）
            root = new Grid { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; // 位置大小由 PlaceContent 按 ChatGPT 窗口定
            root.Children.Add(grid);
            clickThrough = !withIntro;
            alpha = 0; // 开屏也从透明淡入（0.3 秒），不再一出现就整块黑屏盖上去
            grid.Opacity = Dev.PerPixel ? alpha : 1;
            waitFrame = !withIntro && !Dev.PerPixel; // 直接开循环背景时，等视频出了第一帧再淡入（外壳窗口是黑底，没出帧时是黑的）
            disp = Dispatcher.CurrentDispatcher;
            if (Dev.Hosted)
            {
                host = new HostForm("GPT Boot Animation") { ClickThrough = clickThrough };
                host.OnMsg = WndProc;
                overlayH = host.Handle;          // 这一步建出窗口
                ApplyAlpha(true);                // 分层窗口建好后先设成全透明，不然在设之前是看不见也点不到的状态
                InitOverlayHandle();
                childSrc = new HwndSource(new HwndSourceParameters("GPT Boot Animation")
                {
                    ParentWindow = overlayH, WindowStyle = 0x50000000 /*WS_CHILD | WS_VISIBLE*/, PositionX = 0, PositionY = 0, Width = 10, Height = 10
                });
                childSrc.CompositionTarget.BackgroundColor = Colors.Black;
                childSrc.RootVisual = root;
                childH = childSrc.Handle;
            }
            else
            {
                overlay = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = Dev.PerPixel, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                    ShowActivated = false, Background = Dev.PerPixel ? Brushes.Transparent : Brushes.Black, Content = root,
                    Left = -32000, Top = -32000, Width = 10, Height = 10, Title = "GPT Boot Animation"
                };
                overlay.SourceInitialized += delegate
                {
                    overlayH = new WindowInteropHelper(overlay).Handle;
                    EnsureOverlayStyle(true);
                    var hs = HwndSource.FromHwnd(overlayH);
                    if (hs != null) hs.AddHook(WndProc);
                    InitOverlayHandle();
                };
            }

            if (withIntro)
            {
                var topGrid = root;
                sub = new SubtitleElement { WidthRatio = config.SubtitleWidthRatio };
                if (config.Subtitles.Count > 0) sub.RefText = config.Subtitles[0].Text;
                topGrid.Children.Add(sub);
                skipBtn = new Border
                {
                    Width = 72, Height = 28, CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 44, 18, 0), Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "跳过 ›", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Medium,
                        FontFamily = new FontFamily("Microsoft YaHei UI"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                skipBtn.MouseLeftButtonUp += delegate { SkipIntro(); };
                topGrid.Children.Add(skipBtn);
            }

            if (host != null) host.Show(); else overlay.Show();
            EnsureOverlayStyle(true); // WPF 显示窗口时会重写扩展样式，显示后再补一次
            if (top != null) top.Show();
            Follow(true);
            long st = Native.GetLong(overlayH, Native.GWL_EXSTYLE);
            Log.W("浮层样式 0x" + st.ToString("X") + " layered=" + ((st & Native.WS_EX_LAYERED) != 0 ? 1 : 0) + " mode=" + Dev.OverlayMode);

            if (withIntro)
            {
                intro.Play();
                RampAlpha(1, 0.3, null);
                Log.W("开始播放开屏动画，窗口 " + Target.Hwnd + " " + RectStr(lastRect));
            }
            else
            {
                loopA.Play();
                phase = Phase.Revealed;
                RampAlpha(config.ResidualAlpha, config.FadeDurationSeconds, null);
                Log.W("直接开启循环背景，窗口 " + Target.Hwnd);
            }

            // 解锁、远程桌面切回、换显示器 / 改分辨率之后，重建一次播放器
            disp = Dispatcher.CurrentDispatcher;
            onSession = delegate (object s, SessionSwitchEventArgs e)
            {
                var r = e.Reason;
                if (r == SessionSwitchReason.SessionUnlock || r == SessionSwitchReason.ConsoleConnect || r == SessionSwitchReason.RemoteConnect)
                    disp.BeginInvoke(new Action(delegate { LaterRebuild("解锁 / 切回桌面（" + r + "）"); }));
            };
            onDisplay = delegate { disp.BeginInvoke(new Action(delegate { LaterRebuild("显示器设置变了"); })); };
            try { SystemEvents.SessionSwitch += onSession; SystemEvents.DisplaySettingsChanged += onDisplay; }
            catch (Exception ex) { Log.W("系统事件订阅失败：" + ex.Message); }
            nextBeatMs = (long)(Dev.HeartbeatSec * 1000);
            nextStallTestMs = (long)(Dev.StallTestSec * 1000);

            // 跟窗口：不再每 15 毫秒去问一次窗口在哪，改成让系统在目标窗口移动、缩放、最小化、隐藏时通知我们，马上跟上
            winEvent = OnWinEvent;
            foreach (var rng in new[] { new uint[] { 0x0016, 0x0017 } /*最小化开始/结束*/, new uint[] { 0x8002, 0x8003 } /*显示/隐藏*/,
                                      new uint[] { 0x800B, 0x800B } /*位置变了*/, new uint[] { 0x8017, 0x8018 } /*被系统藏起/放出*/ })
            {
                IntPtr hk = Native.SetWinEventHook(rng[0], rng[1], IntPtr.Zero, winEvent, Target.Pid, 0, 0 /*WINEVENT_OUTOFCONTEXT*/);
                if (hk != IntPtr.Zero) hooks.Add(hk);
            }
            if (hooks.Count < 4) Log.W("窗口事件通知只挂上 " + hooks.Count + "/4 个，靠定时检查兜底");

            // 定时器只做杂事（交接、自愈、渐变、字幕）：要掐时间的时候 15 毫秒一次，平时 100 毫秒一次
            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += delegate { Tick(); };
            tick.Start();
        }

        readonly List<IntPtr> hooks = new List<IntPtr>();
        Native.WinEventProc winEvent;   // 留着引用，不然被回收后系统回调会崩
        bool waitFrame;

        void OnWinEvent(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
        {
            if (h != Target.Hwnd || idObject != 0 /*OBJID_WINDOW*/ || idChild != 0 || phase == Phase.Done) return;
            try { Follow(false); } catch (Exception ex) { Log.W("跟随窗口出错：" + ex.Message); }
        }

        /// 现在是不是要掐时间：开屏、渐变、接缝、交接快到点、重建中——这些时候定时器跑快档
        bool Busy()
        {
            if (!introGone || alphaDur >= 0 || volDur >= 0 || swapFrom != null || handTo != null || retiring != null || pendingAt > 0 || waitFrame) return true;
            if (overlapLoop && activeLoop != null && !paused && activeLoop.NaturalDuration.HasTimeSpan)
            {
                double half = activeLoop.NaturalDuration.TimeSpan.TotalSeconds / 2, pos = activeLoop.Position.TotalSeconds;
                if (pos >= half - handLead - 0.3) return true;
            }
            if (!overlapLoop && activeLoop != null && activeLoop.NaturalDuration.HasTimeSpan
                && activeLoop.Position.TotalSeconds >= activeLoop.NaturalDuration.TimeSpan.TotalSeconds - 0.3) return true;
            return Dev.LoopCutSec > 0;
        }

        /// 浮层窗口建好后：收系统的睡眠唤醒、显示器亮灭通知（这两种之后视频画面最容易冻住），
        /// 并设成目标窗口的「附属窗口」：系统自动让它一直在目标窗口上面，目标窗口被别的窗口挡住时它也一起被挡住
        void InitOverlayHandle()
        {
            try { Guid g = Native.GUID_CONSOLE_DISPLAY_STATE; powerNotify = Native.RegisterPowerSettingNotification(overlayH, ref g, 0); }
            catch (Exception ex) { Log.W("显示器亮灭通知注册失败：" + ex.Message); }
            Native.SetLong(overlayH, Native.GWLP_HWNDPARENT, Target.Hwnd.ToInt64());
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x02E0 /*WM_DPICHANGED*/)
            {
                // 浮层换到另一块缩放不同的屏（#7：主屏 150%、副屏 125%）：WPF 可能按系统建议改掉浮层大小，
                // 等它处理完，再按新缩放把浮层铺回整块屏、把视频摆回窗口位置，免得背景变小或错位到窗口再动一下才好
                int dpi = (int)(wParam.ToInt64() & 0xFFFF);
                Log.W("浮层换到缩放 " + (dpi * 100 / 96) + "% 的屏，重新摆放");
                disp.BeginInvoke(new Action(delegate { if (phase != Phase.Done) Follow(true); }));
                return IntPtr.Zero;
            }
            if (msg != 0x218 /*WM_POWERBROADCAST*/) return IntPtr.Zero;
            try
            {
                long ev = wParam.ToInt64();
                if (ev == 0x7 /*PBT_APMRESUMESUSPEND*/ || ev == 0x12 /*PBT_APMRESUMEAUTOMATIC*/) LaterRebuild("电脑从睡眠中唤醒");
                else if (ev == 0x8013 /*PBT_POWERSETTINGCHANGE*/ && lParam != IntPtr.Zero)
                {
                    // POWERBROADCAST_SETTING：16 字节 GUID + 4 字节长度 + 数据（0 灭、1 亮、2 变暗）
                    var g = (Guid)Marshal.PtrToStructure(lParam, typeof(Guid));
                    if (g == Native.GUID_CONSOLE_DISPLAY_STATE)
                    {
                        int state = Marshal.ReadInt32(lParam, 20);
                        // 刚注册时系统会先报一次当前状态，那次不算；只有从灭 / 暗变成亮才重建
                        if (state == 1 && displayState >= 0 && displayState != 1) LaterRebuild("显示器重新亮起");
                        if (state != displayState && displayState >= 0) Log.W("显示器状态 " + displayState + " → " + state);
                        displayState = state;
                    }
                }
            }
            catch (Exception ex) { Log.W("电源通知处理出错：" + ex.Message); }
            return IntPtr.Zero;
        }

        static string RectStr(Native.RECT r) { return "(" + r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + ")"; }

        MediaElement MakePlayer(string file, bool muted)
        {
            var m = new MediaElement
            {
                LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill, IsMuted = muted, ScrubbingEnabled = muted,
                Source = new Uri(Asset(file), UriKind.Absolute)
            };
            if (muted)
            {
                // 预先停在第一帧，轮到它时立刻接上，不会黑一下
                bool primed = false;
                m.MediaOpened += delegate
                {
                    if (primed) return; primed = true;
                    if ((m != activeLoop && m != handTo) || phase == Phase.Intro) { m.Pause(); m.Position = TimeSpan.Zero; }
                };
                m.Play();
            }
            return m;
        }

        MediaElement MakeLoopPlayer()
        {
            var m = MakePlayer(loopFile, true);
            m.MediaEnded += OnLoopEnded;
            m.MediaFailed += OnLoopFailed;
            return m;
        }

        MediaElement OtherLoop(MediaElement m) { return m == loopA ? loopB : loopA; }

        void OnLoopEnded(object s, RoutedEventArgs e)
        {
            var m = s as MediaElement;
            if (m == null || m != activeLoop || phase == Phase.Done) return; // 已经换下去的、或已经重建掉的旧播放器，不理它
            SwapLoop(m, OtherLoop(m));
        }

        void OnLoopFailed(object s, ExceptionRoutedEventArgs e)
        {
            if (s != loopA && s != loopB) return;
            var m = (MediaElement)s;
            bool onStage = m == activeLoop || m == handTo || m == swapTo || m == swapFrom;
            Log.W("循环背景播放器报错（" + (onStage ? "正在放的" : "待命的") + "）：" + ErrStr(e));
            // 正在放的坏了才整个重建；待命的坏了只换待命的，正在放的不受影响（#7：以前待命的一报错就重建，画面会停一下或消失几秒）
            if (onStage) LaterRebuild("播放器报错", 500);
            else disp.BeginInvoke(new Action(delegate { if (phase != Phase.Done && (m == loopA || m == loopB) && m != activeLoop && m != handTo) ReplaceStandby(m); }));
        }

        static string ErrStr(ExceptionRoutedEventArgs e)
        {
            var x = e.ErrorException;
            if (x == null) return "未知";
            return x.GetType().Name + " 0x" + x.HResult.ToString("X8") + " " + x.Message;
        }

        /// 关掉一个播放器并从画面里拿走
        void DisposePlayer(MediaElement m)
        {
            if (m == null) return;
            m.MediaEnded -= OnLoopEnded;
            m.MediaFailed -= OnLoopFailed;
            try { m.Stop(); m.Close(); } catch { }
            grid.Children.Remove(m);
        }

        /// 两个播放器轮流放循环段：一个放完，另一个已经停在第一帧，马上接上
        void SwapLoop(MediaElement done, MediaElement next)
        {
            if (phase == Phase.Done) return;
            handFrom = null; handTo = null; // 重叠交接没赶上、退回老办法时，交接状态作废
            next.Play();
            activeLoop = next;
            BeginSwap(done, next); // done 停在最后一帧盖着，等 next 动起来再换上去
        }

        void BeginSwap(MediaElement from, MediaElement to)
        {
            if (swapFrom != null && swapFrom != from) FinishSwap();
            swapFrom = from; swapTo = to; swapT0 = DateTime.Now; swapKicked = false;
        }

        // 每帧检查：新播放器的进度走起来了（画面已经出来），才把旧的撤掉。
        // 以前等 1.5 秒没动也照样换上去——换上去的是个没在放的播放器，它永远不会「放完」，循环就此停住。
        // 现在没动就再催一次 Play，旧画面继续盖着；还不动交给自愈去重建。
        void CheckSwap()
        {
            if (swapFrom == null) return;
            if (swapTo.Position.TotalSeconds > (swapFrom == intro ? 0.15 : 0.06)) { FinishSwap(); return; }
            if (!swapKicked && (DateTime.Now - swapT0).TotalSeconds > 1.5)
            {
                swapKicked = true;
                Log.W("接缝：新播放器 1.5 秒还没动，再催一次（第 " + laps + " 圈）");
                swapTo.Play();
            }
        }

        void FinishSwap()
        {
            var from = swapFrom; var to = swapTo;
            swapFrom = null; swapTo = null;
            if (from == intro)
            {
                // 开屏 0.25 秒淡出再关（跟重建时撤旧画面一样）。以前一下子收掉，云端录屏偶尔在这一刻漏出一帧黑
                var iv = intro;
                var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.25));
                fade.Completed += delegate { iv.Visibility = Visibility.Collapsed; iv.Stop(); iv.Close(); };
                iv.BeginAnimation(UIElement.OpacityProperty, fade);
                return;
            }
            Promote(from, to);
        }

        /// to 换到上面成为正在放的，from 退回第一帧待命（每隔几圈换成新的）
        void Promote(MediaElement from, MediaElement to)
        {
            Panel.SetZIndex(to, 1); Panel.SetZIndex(from, 0);
            activeLoop = to;
            laps++;
            if (Dev.RecycleEvery > 0 && laps % Dev.RecycleEvery == 0) ReplaceStandby(from);
            else { from.Pause(); from.Position = TimeSpan.Zero; } // 退回第一帧备用（ScrubbingEnabled 让它暂停时也解出这一帧）
        }

        // 每帧检查重叠交接（只在有 loop-x2.mp4 时）
        void CheckHandoff()
        {
            if (paused || !overlapLoop || phase == Phase.Done || !introGone || swapFrom != null || activeLoop == null) return;
            if (handFrom != null && handFrom != activeLoop) { handFrom = null; handTo = null; } // 被重建 / 老办法接走了
            var cur = activeLoop;
            if (!cur.NaturalDuration.HasTimeSpan) return;
            double dur = cur.NaturalDuration.TimeSpan.TotalSeconds, half = dur / 2;
            double pos = cur.Position.TotalSeconds;

            if (handTo == null)
            {
                if (pos < 0.05 || pos < half - handLead) return;
                var next = OtherLoop(cur);
                next.Position = TimeSpan.Zero;
                next.Play();
                handFrom = cur; handTo = next; handT0 = DateTime.Now;
                return;
            }

            // 新的走过 0.8 秒（画面肯定已经稳了），旧的还在第二遍里，此时两边画面相同，换上去看不出来。
            // 新的一直没走起来就不换，旧的放到头会走「放完再接」的老路，那时新的已经在放了
            double toPos = handTo.Position.TotalSeconds;
            if (toPos < 0.8) return;
            double fromPos = handFrom.Position.TotalSeconds;
            double offset = (fromPos - half) - toPos;   // 正数 = 新的比旧的慢
            handLead = Math.Max(0, Math.Min(0.5, handLead + offset));
            var from = handFrom; var to = handTo;
            handFrom = null; handTo = null;
            Promote(from, to);
            handoffs++;
            if (handoffs <= 5 || handoffs % 200 == 0)
                Log.W("循环交接（第 " + handoffs + " 次）：错位 " + Math.Round(offset * 1000) + "ms，下次提前 " + Math.Round(handLead * 1000) + "ms");
        }

        /// 后台待命的那个播放器定期换成新的：同一个播放器暂停、倒回、再放成百上千次，不让它一直用下去。
        /// 它此刻垫在下面看不见，换掉不影响画面；新的自己会停在第一帧等着
        // #7：新播放器偶尔打不开（报「找不到媒体文件」）。以前是先换上去再说，它一报错就整个重建，画面停一下或消失几秒。
        // 现在旧的照常留着待命，新的打开成功才顶替它；打不开就扔掉新的，旧的继续用，画面完全不受影响
        MediaElement candidate, candidateFor;   // 正在打开、还没顶上去的新待命播放器，和它要顶替的那个
        int standbyReplaces, standbyFails;

        void ReplaceStandby(MediaElement old)
        {
            DropCandidate();
            standbyReplaces++;
            bool inject = Dev.StandbyFailEvery > 0 && standbyReplaces % Dev.StandbyFailEvery == 0;
            var fresh = MakePlayer(inject ? "__standby-fail-test__.mp4" : loopFile, true);
            fresh.MediaOpened += OnCandidateOpened;
            fresh.MediaFailed += OnCandidateFailed;
            Panel.SetZIndex(fresh, -1);
            grid.Children.Add(fresh);
            candidate = fresh; candidateFor = old;
            try { old.Pause(); old.Position = TimeSpan.Zero; } catch { } // 旧的先照常退回第一帧待命
        }

        void OnCandidateOpened(object s, RoutedEventArgs e)
        {
            var fresh = s as MediaElement;
            if (fresh == null || fresh != candidate) return;
            var old = candidateFor;
            candidate = null; candidateFor = null;
            fresh.MediaOpened -= OnCandidateOpened; fresh.MediaFailed -= OnCandidateFailed;
            // 旧的这会儿已经上场（正在放、正在交接）或已经被重建换掉：不顶替了，扔掉新的，下次再换
            if (phase == Phase.Done || (old != loopA && old != loopB) || old == activeLoop || old == handTo || old == swapTo || old == swapFrom || old == retiring)
            { DisposePlayer(fresh); return; }
            fresh.MediaEnded += OnLoopEnded; fresh.MediaFailed += OnLoopFailed;
            Panel.SetZIndex(fresh, 0);
            if (old == loopA) loopA = fresh; else loopB = fresh;
            DisposePlayer(old);
            standbyFails = 0;
        }

        void OnCandidateFailed(object s, ExceptionRoutedEventArgs e)
        {
            var fresh = s as MediaElement;
            if (fresh == null || fresh != candidate) return;
            candidate = null; candidateFor = null;
            fresh.MediaOpened -= OnCandidateOpened; fresh.MediaFailed -= OnCandidateFailed;
            standbyFails++;
            Log.W("待命播放器没换成（" + ErrStr(e) + "），旧的继续用，画面不受影响（连续第 " + standbyFails + " 次，已循环 " + laps + " 圈）");
            disp.BeginInvoke(new Action(delegate { DisposePlayer(fresh); }));
        }

        void DropCandidate()
        {
            if (candidate == null) return;
            var c = candidate; candidate = null; candidateFor = null;
            c.MediaOpened -= OnCandidateOpened; c.MediaFailed -= OnCandidateFailed;
            DisposePlayer(c);
        }

        // ── 自愈 ──

        /// 过一会儿再重建（系统刚唤醒、刚解锁时显卡可能还没缓过来）。连着来好几次只算最后一次
        void LaterRebuild(string why) { LaterRebuild(why, 2000); }
        void LaterRebuild(string why, int delayMs)
        {
            if (phase == Phase.Done) return;
            pendingWhy = why;
            pendingAt = clock.ElapsedMilliseconds + delayMs;
        }

        /// 把两个循环播放器都换成新的。旧的正在显示的那个先盖着，新的动起来再撤，不闪黑
        /// 不管是谁要求的重建（卡住、报错、解锁唤醒），都走这同一道门：连着修不好就放慢，
        /// 前三次隔 8 秒，之后隔 1 分钟（比如显示器关着、或者这台电脑根本解不了这个视频），返回 false 表示这次先不重建
        bool RebuildLoops(string why)
        {
            if (phase == Phase.Done || phase == Phase.Ending) return true;
            long now = clock.ElapsedMilliseconds;
            long gap = failStreak < 3 ? 8000 : 60000;
            if (lastRecoverMs >= 0 && now - lastRecoverMs < gap) return false;
            DropCandidate();
            failStreak++;
            recoveries++; lastRecoverMs = now;
            double p = -1;
            try { if (activeLoop != null) p = activeLoop.Position.TotalSeconds; } catch { }
            Log.W("循环背景自动恢复（" + why + "）：第 " + recoveries + " 次，已循环 " + laps + " 圈，当时进度 " + p.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
            if (swapFrom != null && swapFrom == intro) FinishSwap(); // 开屏还盖着就收掉
            // 正在接缝的话，屏幕上看到的是还盖着的旧播放器，留它盖着；
            // 上一次重建的新播放器还没动起来、旧画面还盖着的话，屏幕上看到的是那个旧画面，继续让它盖着
            var old = retiring != null ? retiring : swapFrom != null ? swapFrom : activeLoop;
            swapFrom = null; swapTo = null; handFrom = null; handTo = null;
            if (retiring != null && retiring != old) DisposePlayer(retiring);
            retiring = null;
            if (loopA != old) DisposePlayer(loopA);
            if (loopB != old) DisposePlayer(loopB);
            if (old != null)
            {
                old.MediaEnded -= OnLoopEnded; old.MediaFailed -= OnLoopFailed;
                Panel.SetZIndex(old, 3);
                retiring = old; retireT0 = now;
            }
            loopA = MakeLoopPlayer();
            loopB = MakeLoopPlayer();
            Panel.SetZIndex(loopA, 1); Panel.SetZIndex(loopB, 0);
            grid.Children.Add(loopA); grid.Children.Add(loopB);
            activeLoop = loopA; // MakePlayer 里已经 Play 了；activeLoop 是它，打开后不会被停在第一帧
            verifying = true;
            lastMoveMs = now; lastPos = -1;
            return true;
        }

        void CheckRetire(long now)
        {
            if (retiring == null || paused) return;
            // 新播放器稳稳走过 1 秒才撤旧画面，而且 0.25 秒淡出、不是一下拿掉。
            // 云端实测：新播放器进度刚过 0.3 秒时画面还会黑一两帧，那时撤掉旧画面就闪黑；以前 2.5 秒没动也照撤，撤完是一块空白
            if (activeLoop.Position.TotalSeconds < 1.0 && now - retireT0 < 20000) return;
            var r = retiring; retiring = null;
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.25));
            fade.Completed += delegate { DisposePlayer(r); };
            r.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// 每 0.25 秒看一眼正在放的循环段进度有没有往前走
        void Watchdog(long now)
        {
            if (now < nextSampleMs) return;
            nextSampleMs = now + 250;
            bool timerGap = lastSampleMs > 0 && now - lastSampleMs > 2000; // 电脑睡眠、整个程序卡住时计时器没跑，那段不算画面卡住
            lastSampleMs = now;
            if (timerGap || paused || phase != Phase.Revealed || !introGone || hidden || activeLoop == null)
            {
                lastMoveMs = now; lastPos = -1; // 藏着、暂停着、还在开屏时不算；回来后重新计时
                return;
            }
            double p = activeLoop.Position.TotalSeconds;
            if (now >= nextBeatMs)
            {
                nextBeatMs = now + (long)(Math.Max(1, Dev.HeartbeatSec) * 1000);
                Log.W("循环背景运行中：已循环 " + laps + " 圈，自动恢复 " + recoveries + " 次，进度 " + p.ToString("0.00", CultureInfo.InvariantCulture) + " 秒，" + Perf.Sample());
            }
            if (Math.Abs(p - lastPos) > 0.0005)
            {
                lastPos = p; lastMoveMs = now;
                if (verifying && p > 0.3) { verifying = false; failStreak = 0; Log.W("恢复成功：循环背景又动起来了"); }
                return;
            }
            long stuck = now - lastMoveMs;
            // 停在最后一帧却没收到「放完」通知：直接手动接到另一个播放器，跟正常接缝一样无缝
            if (stuck >= 400 && swapFrom == null && activeLoop.NaturalDuration.HasTimeSpan
                && p >= activeLoop.NaturalDuration.TimeSpan.TotalSeconds - 0.08)
            {
                missedEnds++;
                if (missedEnds <= 20 || missedEnds % 100 == 0) Log.W("循环段放到头没收到放完通知，手动接上（第 " + missedEnds + " 次）");
                SwapLoop(activeLoop, OtherLoop(activeLoop));
                lastMoveMs = now; lastPos = -1;
                return;
            }
            if (stuck < Dev.StallMs) return;
            RebuildLoops("画面 " + (stuck / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒没动");
        }

        /// 开发用：提前换播放器（--loop-cut）、故意制造卡住（--stall-test）
        void DevHooks(long now)
        {
            if (paused || phase != Phase.Revealed || !introGone || activeLoop == null) return;
            if (Dev.LoopCutSec > 0 && !overlapLoop && swapFrom == null && activeLoop.Position.TotalSeconds >= Dev.LoopCutSec)
                SwapLoop(activeLoop, OtherLoop(activeLoop));
            if (Dev.StallTestSec > 0 && now >= nextStallTestMs)
            {
                nextStallTestMs = now + (long)(Dev.StallTestSec * 1000);
                Log.W("测试：故意把正在放的循环播放器暂停，等自愈接回来");
                activeLoop.Pause();
            }
        }

        void IntroEnded()
        {
            if (introGone) return;
            introGone = true;
            Log.W("开屏放完，接循环段");
            loopA.Play();
            activeLoop = loopA;
            BeginSwap(intro, loopA); // 开屏停在最后一帧盖着，循环段动起来再撤
            if (phase == Phase.Intro) Reveal(false);
            CloseTop();
        }

        void CloseTop()
        {
            if (skipBtn != null) skipBtn.Visibility = Visibility.Collapsed;
        }

        // ── 每 15ms：跟随窗口、字幕、渐变 ──
        void Tick()
        {
            if (phase == Phase.Done) return;
            Follow(false);
            if (phase == Phase.Done) return;
            EnsureOverlayStyle(false);
            CheckSwap();
            CheckHandoff();
            long now = clock.ElapsedMilliseconds;
            CheckRetire(now);
            DevHooks(now);
            if (pendingAt > 0 && now >= pendingAt && !hidden && !paused && phase == Phase.Revealed && introGone) // 暂停时先不重建，继续播放后再补
            {
                pendingAt = RebuildLoops(pendingWhy) ? 0 : now + 1000; // 被退避挡住就过一秒再问
            }
            Watchdog(now);

            if (!introGone && intro != null)
            {
                double t = intro.Position.TotalSeconds;
                if (phase == Phase.Intro && t >= config.FadeStartSeconds) Reveal(false);
                UpdateSubtitle(t);
            }
            if (waitFrame)
            {
                // 视频还没出第一帧：保持全透明、渐变先不开始计时
                if (activeLoop != null && activeLoop.Position.TotalSeconds > 0.05) { waitFrame = false; alphaT0 = DateTime.Now; }
                else alphaT0 = DateTime.Now;
            }
            if (alphaDur >= 0 && !waitFrame)
            {
                double k = alphaDur <= 0 ? 1 : Math.Min(1, (DateTime.Now - alphaT0).TotalSeconds / alphaDur);
                k = k * k * (3 - 2 * k);
                SetAlpha(alphaFrom + (alphaTo - alphaFrom) * k);
                if (k >= 1) { alphaDur = -1; var then = alphaThen; alphaThen = null; if (then != null) then(); }
            }
            if (volDur >= 0 && intro != null)
            {
                double k = volDur <= 0 ? 1 : Math.Min(1, (DateTime.Now - volT0).TotalSeconds / volDur);
                intro.Volume = volFrom + (volTo - volFrom) * k;
                if (k >= 1) volDur = -1;
            }
            if (phase == Phase.Done) return;
            var want = TimeSpan.FromMilliseconds(Busy() ? 15 : 100);
            if (tick.Interval != want) tick.Interval = want;
        }

        /// 跟着 ChatGPT 窗口移动、缩放；ChatGPT 最小化或藏到托盘时一起藏起来；ChatGPT 关了就收工
        void Follow(bool force)
        {
            if (!Native.IsWindow(Target.Hwnd))
            {
                Log.W("ChatGPT 窗口没了，收工");
                Teardown(); return;
            }
            bool vis = Native.IsWindowVisible(Target.Hwnd) && !Native.IsIconic(Target.Hwnd) && !Native.Cloaked(Target.Hwnd);
            if (!vis)
            {
                if (!hidden) { hidden = true; ShowAll(false); }
                return;
            }
            if (hidden)
            {
                hidden = false; ShowAll(true); force = true;
                // 循环背景阶段：先透明再 0.35 秒淡回去，盖住 Claude 还原动画和视频层刚恢复时的那一下
                if (phase == Phase.Revealed && alphaDur < 0) { SetAlpha(0); RampAlpha(config.ResidualAlpha, 0.35, null); }
            }
            Native.RECT r;
            if (!Native.Bounds(Target.Hwnd, out r)) return;
            // 浮层被改了大小（换到缩放不同的屏时 WPF 可能按系统建议改它），或浮层所在屏的缩放变了：强制重摆一次（#7）。
            // 不只靠 WM_DPICHANGED：实测单屏改缩放时那条消息没到我们这里
            if (!force && overlayH != IntPtr.Zero && overlayBox.Right > overlayBox.Left && SameRect(r, lastRect))
            {
                Native.RECT cur;
                double sc = CurrentScale();
                bool moved = Native.GetWindowRect(overlayH, out cur) && !SameRect(cur, overlayBox);
                if (moved || Math.Abs(sc - placedScale) > 0.001)
                {
                    Log.W("浮层" + (moved ? "大小被改动" : "所在屏缩放变了") + "，按 " + Math.Round(100 / sc) + "% 重新摆放");
                    force = true;
                }
            }
            if (!force && SameRect(r, lastRect)) return;
            lastRect = r;
            // 浮层就是目标窗口那么大、摆在同一个位置。
            // 测试版曾让浮层铺满整块屏来躲还原时的闪黑（#6），但一块屏那么大的窗口会被 Windows 当成「有程序在全屏」：
            // 任务栏被压到下面、通知区图标一闪一闪，拖到另一块屏时还要整块重建，背景会停住或消失几秒（#7）。
            // 现在浮层不是逐像素透明窗口了，改大小不再整块重画，跟着窗口一起改就行
            Native.RECT box = r;
            if (force || !SameRect(box, overlayBox))
            {
                overlayBox = box;
                uint f = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
                if (overlayH != IntPtr.Zero) Native.SetWindowPos(overlayH, IntPtr.Zero, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top, f);
                if (childH != IntPtr.Zero) Native.SetWindowPos(childH, IntPtr.Zero, 0, 0, box.Right - box.Left, box.Bottom - box.Top, f);
                if (topH != IntPtr.Zero) Native.SetWindowPos(topH, IntPtr.Zero, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top, f);
            }
            PlaceContent(r, box);
        }

        Native.RECT overlayBox;
        PresentationSource Src() { return childSrc != null ? (PresentationSource)childSrc : PresentationSource.FromVisual(overlay); }
        double placedScale = 1;
        double CurrentScale()
        {
            var src = Src();
            return src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.M11 : 1;
        }
        static bool SameRect(Native.RECT a, Native.RECT b) { return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom; }

        /// 把视频、字幕、「跳过」那一层摆到 ChatGPT 窗口在浮层里的位置（物理像素换成 WPF 的逻辑单位）
        void PlaceContent(Native.RECT r, Native.RECT box)
        {
            var src = Src();
            Matrix m = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            placedScale = m.M11;
            root.Margin = new Thickness((r.Left - box.Left) * m.M11, (r.Top - box.Top) * m.M22, 0, 0);
            root.Width = Math.Max(1, (r.Right - r.Left) * m.M11);
            root.Height = Math.Max(1, (r.Bottom - r.Top) * m.M22);
        }

        void ShowAll(bool show)
        {
            int cmd = show ? 4 /*SW_SHOWNOACTIVATE*/ : 0 /*SW_HIDE*/;
            if (overlayH != IntPtr.Zero) Native.ShowWindow(overlayH, cmd);
            if (topH != IntPtr.Zero) Native.ShowWindow(topH, cmd);
        }

        /// 分层（整窗半透明）+ 不出现在任务栏 + 不抢焦点；透出后再加点击穿透。被冲掉就补回来
        void EnsureOverlayStyle(bool force)
        {
            if (overlayH == IntPtr.Zero) return;
            long ex = Native.GetLong(overlayH, Native.GWL_EXSTYLE);
            long want = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
            if (Dev.Hosted) want |= Native.WS_EX_LAYERED;
            if (clickThrough) want |= Native.WS_EX_TRANSPARENT;
            if (want != ex && (styleFixes < 20 || (Dev.Hosted && (ex & Native.WS_EX_LAYERED) == 0)))
            {
                if (!force) { styleFixes++; if (styleFixes <= 20) Log.W("浮层样式被改过，已补回 0x" + ex.ToString("X") + " → 0x" + want.ToString("X")); }
                Native.SetLong(overlayH, Native.GWL_EXSTYLE, want);
                if (Dev.Hosted && (ex & Native.WS_EX_LAYERED) == 0) ApplyAlpha(true); // 分层样式被拿掉又补上后，透明度要重新告诉系统，不然整个窗口看不见
            }
        }

        void SetAlpha(double a)
        {
            alpha = Math.Max(0, Math.Min(1, a));
            ApplyAlpha();
        }

        int appliedAlpha = -1; // -1 = 还没设过
        void ApplyAlpha() { ApplyAlpha(false); }
        void ApplyAlpha(bool always)
        {
            if (Dev.PerPixel) { grid.Opacity = alpha; return; }
            if (!Dev.Hosted || overlayH == IntPtr.Zero) return;   // opaque 模式只测性能，不做透明
            int b = (int)Math.Round((waitFrame ? 0 : alpha) * 255);
            if (b == appliedAlpha && !always) return;
            appliedAlpha = b;
            Native.SetLayeredWindowAttributes(overlayH, 0, (byte)b, Native.LWA_ALPHA);
        }

        void RampAlpha(double to, double seconds, Action then)
        {
            alphaFrom = alpha; alphaTo = to; alphaT0 = DateTime.Now; alphaDur = Math.Max(0, seconds); alphaThen = then;
        }

        void RampVolume(double to, double seconds)
        {
            if (intro == null) return;
            volFrom = intro.Volume; volTo = to; volT0 = DateTime.Now; volDur = seconds;
        }

        void SetClickThrough(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            Native.SetLong(h, Native.GWL_EXSTYLE, Native.GetLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TRANSPARENT);
        }

        // 字幕：按播放时间算透明度，入场、出场各渐变 SubtitleFadeSeconds
        void UpdateSubtitle(double t)
        {
            if (sub == null || !config.ShowSubtitles) return;
            double fade = Math.Max(0.01, config.SubtitleFadeSeconds);
            int idx = -1;
            for (int i = 0; i < config.Subtitles.Count; i++)
                if (t >= config.Subtitles[i].Start && t <= config.Subtitles[i].End) { idx = i; break; }
            if (idx < 0)
            {
                if (currentSub >= 0 && sub.Opacity > 0) Log.W("字幕 " + (currentSub + 1) + " 消失，视频时间 " + t.ToString("0.000"));
                sub.Opacity = 0; currentSub = idx >= 0 ? idx : currentSub;
                if (currentSub == config.Subtitles.Count - 1 && phase != Phase.Intro) CloseTop(); // 最后一句放完，字幕层收掉
                return;
            }
            var s = config.Subtitles[idx];
            if (idx != currentSub) { currentSub = idx; sub.SetText(s.Text); Log.W("字幕 " + (idx + 1) + " 出现，视频时间 " + t.ToString("0.000")); }
            sub.Opacity = Math.Max(0, Math.Min(1, Math.Min((t - s.Start) / fade, (s.End - t) / fade)));
        }

        void HideSubtitles()
        {
            if (sub == null) return;
            sub.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
            sub = null;
        }

        /// 进入循环帧：透出 ChatGPT 界面，点击穿透
        void Reveal(bool quick)
        {
            if (phase != Phase.Intro) return;
            phase = Phase.Revealed;
            clickThrough = true; EnsureOverlayStyle(true);
            SetClickThrough(topH);
            grid.Background = Brushes.Transparent;
            if (skipBtn != null) skipBtn.Visibility = Visibility.Collapsed; // 进入循环背景后不再显示「跳过」
            if (!config.LoopBackground)
            {
                Log.W("进入循环帧，循环背景已关，淡出");
                Finish(false); return;
            }
            Log.W("进入循环帧，透出界面");
            RampAlpha(config.ResidualAlpha, quick ? 0.9 : config.FadeDurationSeconds, null);
        }

        /// 点「跳过」：开着循环背景就淡出开屏、接到循环段，否则整个关掉
        public void SkipIntro()
        {
            if (phase != Phase.Intro) return;
            Log.W("点了跳过开屏");
            if (!config.LoopBackground) { Finish(true); return; }
            HideSubtitles();
            loopA.Play(); activeLoop = loopA;
            introGone = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.9)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            var iv = intro;
            fade.Completed += delegate { iv.Visibility = Visibility.Collapsed; iv.Stop(); iv.Close(); };
            intro.BeginAnimation(UIElement.OpacityProperty, fade);
            RampVolume(0, 0.9); // 开屏音效随画面一起淡出
            Reveal(true);
            var closeTop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
            closeTop.Tick += delegate { closeTop.Stop(); CloseTop(); };
            closeTop.Start();
        }

        /// 菜单、设置窗口里拖滑块时实时生效
        public void Apply(Config c)
        {
            config = c;
            if (phase == Phase.Ending || phase == Phase.Done) return;
            if (phase == Phase.Revealed && alphaDur < 0) SetAlpha(c.ResidualAlpha);
            if (intro != null && !introGone) { volDur = -1; intro.Volume = c.IntroVolume; }
        }

        public void Finish(bool fast)
        {
            if (phase == Phase.Ending || phase == Phase.Done) return;
            phase = Phase.Ending;
            clickThrough = true; EnsureOverlayStyle(true);
            double d = fast ? 0.4 : 1.2;
            Log.W(fast ? "关闭（跳过）" : "淡出");
            if (skipBtn != null) skipBtn.Visibility = Visibility.Collapsed;
            HideSubtitles();
            RampVolume(0, d);
            RampAlpha(0, d, Teardown);
        }

        public void Teardown()
        {
            if (phase == Phase.Done) return;
            phase = Phase.Done;
            if (tick != null) tick.Stop();
            foreach (var hk in hooks) { try { Native.UnhookWinEvent(hk); } catch { } }
            hooks.Clear();
            try { SystemEvents.SessionSwitch -= onSession; SystemEvents.DisplaySettingsChanged -= onDisplay; } catch { }
            if (powerNotify != IntPtr.Zero) { try { Native.UnregisterPowerSettingNotification(powerNotify); } catch { } powerNotify = IntPtr.Zero; }
            foreach (var m in new[] { intro, loopA, loopB, retiring, candidate })
                if (m != null) { try { m.Stop(); m.Close(); } catch { } }
            CloseTop();
            try
            {
                if (childSrc != null) { childSrc.RootVisual = null; childSrc.Dispose(); childSrc = null; }
                if (host != null) { host.Close(); host.Dispose(); }
                if (overlay != null) overlay.Close();
            }
            catch (Exception ex) { Log.W("关浮层出错：" + ex.Message); }
            Log.W("播放结束");
            if (onDone != null) onDone();
        }

        public bool Active { get { return phase != Phase.Done; } }
        /// 正在放开屏或循环背景（不算正在淡出关掉）
        public bool Playing { get { return phase == Phase.Intro || phase == Phase.Revealed; } }
        public bool InIntro { get { return phase == Phase.Intro; } }
        public bool CanPause { get { return phase == Phase.Revealed; } }

        // ── 暂停循环背景：画面停在当前这一帧，背景照样盖着；再点继续 ──
        bool paused;
        public bool Paused { get { return paused; } }
        public void SetPaused(bool p)
        {
            if (phase != Phase.Revealed || p == paused) return;
            if (p)
            {
                if (swapFrom != null) FinishSwap();   // 正在接缝：先接完
                if (handTo != null) { handTo.Pause(); handTo.Position = TimeSpan.Zero; handFrom = null; handTo = null; } // 正在重叠交接：新的退回待命
                foreach (var m in new[] { intro, loopA, loopB })
                    if (m != null) { try { m.Pause(); } catch { } }
                paused = true;
            }
            else
            {
                paused = false;
                if (!introGone && intro != null) intro.Play();
                else if (activeLoop != null) activeLoop.Play();
                lastMoveMs = clock.ElapsedMilliseconds; lastPos = -1; // 卡住检测从现在重新算
            }
            Log.W(p ? "暂停循环背景" : "继续播放循环背景");
        }

        /// 测试用：把 ChatGPT 窗口那块屏幕截下来
        public void Snapshot(string path)
        {
            Native.RECT r = lastRect;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return;
            using (var bmp = new SD.Bitmap(w, h))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                {
                    IntPtr dst = g.GetHdc(), src = Native.GetDC(IntPtr.Zero);
                    Native.BitBlt(dst, 0, 0, w, h, src, r.Left, r.Top, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT：分层窗口也拍进去
                    Native.ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst);
                }
                bmp.Save(path, SD.Imaging.ImageFormat.Png);
            }
            // 点击穿透检查：窗口中心这一点，鼠标点下去会落到哪个窗口
            IntPtr hit = Native.WindowFromPoint(new Native.POINT { X = r.Left + w / 2, Y = r.Top + h / 2 });
            IntPtr root = Native.GetAncestor(hit, 2 /*GA_ROOT*/);
            string who = root == overlayH ? "浮层" : root == Target.Hwnd ? "目标窗口" : root == topH ? "字幕层" : ("其他 " + Native.ClassOf(root));
            Log.W("截图 " + path + "  播放阶段=" + phase + " 透明度=" + alpha.ToString("0.00") + " 样式=0x" + Native.GetLong(overlayH, Native.GWL_EXSTYLE).ToString("X") + " 中心点击落到=" + who + " hit=" + (root == Target.Hwnd ? "target" : root == overlayH ? "overlay" : "other"));
        }
    }

    // ───────────────────────── 设置窗口 ─────────────────────────

    public class SettingsWindow : Window
    {
        readonly App app;
        CheckBox introBox, loopBox;
        Slider alphaSlider, volSlider;
        TextBlock alphaVal, volVal;
        bool loading;

        public SettingsWindow(App app)
        {
            this.app = app;
            Title = "GPT 开机动画 · 设置";
            Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };

            sp.Children.Add(Header("打开 ChatGPT 时"));
            introBox = new CheckBox { Content = "播放开屏动画", Margin = new Thickness(0, 6, 0, 4) };
            loopBox = new CheckBox { Content = "保留循环背景（半透明，点击穿透）", Margin = new Thickness(0, 4, 0, 4) };
            introBox.Click += delegate { Changed(); };
            loopBox.Click += delegate { Changed(); };
            sp.Children.Add(introBox); sp.Children.Add(loopBox);
            sp.Children.Add(new TextBlock { Text = "两个都不勾 = 原装 ChatGPT", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });

            alphaSlider = MakeSlider(sp, "背景不透明度", 0.8, out alphaVal);
            volSlider = MakeSlider(sp, "开屏音量（再乘系统音量，系统静音就没声）", 1, out volVal);

            sp.Children.Add(Header("现在，在 ChatGPT 窗口上"));
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Btn("播放一次开屏动画", delegate { app.PreviewIntro(); }));
            row.Children.Add(Btn("开启循环背景", delegate { app.PreviewLoop(); }));
            row.Children.Add(Btn("关掉", delegate { app.StopShow(); }));
            sp.Children.Add(row);
            var row2 = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            row2.Children.Add(Btn("打开设置文件…", delegate { app.OpenConfig(); }));
            row2.Children.Add(Btn("卸载…", delegate { app.Uninstall(); }));
            sp.Children.Add(row2);
            Content = sp;
            Refresh();
        }

        static TextBlock Header(string t) { return new TextBlock { Text = t, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }; }
        static Button Btn(string t, RoutedEventHandler h)
        {
            var b = new Button { Content = t, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 6) };
            b.Click += h; return b;
        }
        Slider MakeSlider(StackPanel sp, string title, double max, out TextBlock val)
        {
            var g = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var v = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(v, Dock.Right);
            g.Children.Add(v);
            g.Children.Add(new TextBlock { Text = title });
            sp.Children.Add(g);
            var s = new Slider { Minimum = 0, Maximum = max, Margin = new Thickness(0, 4, 0, 8), IsMoveToPointEnabled = true };
            s.ValueChanged += delegate { v.Text = Math.Round(s.Value * 100) + "%"; if (!loading) Changed(); };
            sp.Children.Add(s);
            val = v;
            return s;
        }

        public void Refresh()
        {
            loading = true;
            var c = app.Config;
            introBox.IsChecked = c.PlayIntro; loopBox.IsChecked = c.LoopBackground;
            alphaSlider.Value = c.ResidualAlpha; volSlider.Value = c.IntroVolume;
            alphaVal.Text = Math.Round(c.ResidualAlpha * 100) + "%"; volVal.Text = Math.Round(c.IntroVolume * 100) + "%";
            loading = false;
        }

        void Changed()
        {
            if (loading) return;
            app.Update(delegate (Config c)
            {
                c.PlayIntro = introBox.IsChecked == true; c.LoopBackground = loopBox.IsChecked == true;
                c.ResidualAlpha = alphaSlider.Value; c.IntroVolume = volSlider.Value;
            }, this);
        }
    }

    // ───────────────────────── 主程序 ─────────────────────────

    public class App : Application
    {
        public static string AssetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        public Config Config;
        Show show;
        WF.NotifyIcon tray;
        WF.ToolStripMenuItem introItem, loopItem, loopNowItem, pauseItem;
        WF.ToolStripLabel alphaLabel, volLabel;
        WF.TrackBar alphaBar, volBar;
        SettingsWindow settings;
        readonly HashSet<uint> seenPids = new HashSet<uint>();
        DispatcherTimer watch;
        readonly string[] args;
        bool testMode;
        bool menuLoading;

        public App(string[] args) { this.args = args; ShutdownMode = ShutdownMode.OnExplicitShutdown; }

        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) { return Array.IndexOf(args, name) >= 0; }
        double ArgD(string name, double def)
        {
            double v; var s = Arg(name);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Show.ForceLegacyLoop = Has("--legacy-loop");
            Config = Config.Load();
            LoadFont();
            Finder.TestTitle = Arg("--target-title");
            testMode = Finder.TestTitle != null || Has("--bench"); // --bench：对着真正的目标程序跑一遍测试流程（测性能用），不找记事本
            Dev.LoopCutSec = ArgD("--loop-cut", Dev.LoopCutSec);
            Dev.StallTestSec = ArgD("--stall-test", Dev.StallTestSec);
            Dev.RecycleEvery = (int)ArgD("--recycle-every", Dev.RecycleEvery);
            Dev.StandbyFailEvery = (int)ArgD("--standby-fail-test", Dev.StandbyFailEvery);
            Dev.HeartbeatSec = ArgD("--heartbeat", Dev.HeartbeatSec);
            Dev.PerfSec = ArgD("--perf-log", Dev.PerfSec);
            var om = Arg("--overlay-mode");
            if (om == "perpixel" || om == "hosted" || om == "opaque") Dev.OverlayMode = om;
            Log.W("启动 " + string.Join(" ", args) + "  程序目录 " + AppDomain.CurrentDomain.BaseDirectory + "  缩放模式 " + Program.DpiMode
                + "  显卡渲染等级 " + (RenderCapability.Tier >> 16) + "  浮层 " + Dev.OverlayMode);
            if (Dev.PerfSec > 0)
            {
                Perf.Sample();
                var perf = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Dev.PerfSec) };
                perf.Tick += delegate { Log.W("性能：" + Perf.Sample()); };
                perf.Start();
            }

            if (Arg("--render-subtitle") != null) { RenderSubtitleTest(); return; }

            SetupTray();
            if (Has("--settings")) OpenSettings();

            // 开机时 ChatGPT 已经开着：不补播
            var t0 = Finder.Find();
            if (t0.Valid && !testMode) seenPids.Add(t0.Pid);

            watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            watch.Tick += delegate { Watch(); };
            watch.Start();

            if (testMode) RunTest();
            else
            {
                var promote = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                promote.Tick += delegate { promote.Stop(); PromoteTrayIcon(); };
                promote.Start();
            }
        }

        void LoadFont()
        {
            try
            {
                if (File.Exists(Path.Combine(AssetsDir, "LibreBodoni-Italic.ttf")))
                    SubtitleElement.Font = new FontFamily(new Uri(AssetsDir.TrimEnd('\\') + "\\"), "./#Libre Bodoni");
            }
            catch (Exception ex) { Log.W("字体载入失败，用 Georgia：" + ex.Message); }
        }

        // 每 0.4 秒看一眼：ChatGPT 有没有新开（新的进程号 = 新启动）
        void Watch()
        {
            var t = Finder.Find();
            if (!t.Valid || seenPids.Contains(t.Pid)) return;
            seenPids.Add(t.Pid);
            if (testMode) return;
            Config = Config.Load();
            Log.W("检测到 ChatGPT 启动 pid=" + t.Pid + "，开屏=" + Config.PlayIntro + " 循环背景=" + Config.LoopBackground);
            if (!Config.PlayIntro && !Config.LoopBackground) return;
            Start(t, Config.PlayIntro);
        }

        void Start(Target t, bool withIntro)
        {
            if (show != null) show.Teardown();
            Show s = null;
            s = new Show(t, Config, withIntro, delegate { if (show == s) show = null; RefreshMenu(); });
            show = s;
            RefreshMenu();
        }

        bool StartOnCurrent(bool withIntro)
        {
            var t = Finder.Find();
            if (!t.Valid)
            {
                if (tray != null) tray.ShowBalloonTip(3000, "GPT 开机动画", "没找到 ChatGPT 窗口，先把 ChatGPT 打开吧。", WF.ToolTipIcon.Info);
                return false;
            }
            seenPids.Add(t.Pid);
            Start(t, withIntro);
            return true;
        }

        public void PreviewIntro() { StartOnCurrent(true); }
        public void PreviewLoop() { StartOnCurrent(false); }
        public void StopShow() { if (show != null) show.Finish(true); }
        public void ToggleLoopNow() { if (show != null && show.Playing) show.Finish(true); else PreviewLoop(); }
        public void OpenConfig() { Config.Save(); try { Process.Start("notepad.exe", "\"" + Config.FilePath + "\""); } catch { } }

        public void Update(Action<Config> change, object from)
        {
            change(Config);
            Config.Save();
            if (show != null) show.Apply(Config);
            if (from != settings && settings != null) settings.Refresh();
            if (from != tray) RefreshMenu();
        }

        public void OpenSettings()
        {
            Config = Config.Load();
            if (settings == null)
            {
                settings = new SettingsWindow(this);
                settings.Closed += delegate { settings = null; };
            }
            settings.Refresh();
            settings.Show();
            if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
            settings.Activate();
        }

        public void Uninstall()
        {
            var r = MessageBox.Show("会删除这个小程序、开机自启、设置和日志。ChatGPT 本身没有被改过，卸载后就是原装的样子。",
                "卸载 GPT 开机动画？", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
            if (show != null) show.Teardown();
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uninstall.ps1");
            if (File.Exists(script))
            {
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -Quiet")
                { UseShellExecute = false, CreateNoWindow = true });
            }
            Quit();
        }

        void Quit()
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Shutdown();
        }

        void SetupTray()
        {
            // 编译时将icon作为exe的图标，运行时托盘图标和菜单直接读取自身的icon加载
            tray = new WF.NotifyIcon { Icon = SD.Icon.ExtractAssociatedIcon(WF.Application.ExecutablePath), Text = "GPT 开机动画", Visible = true };
            var m = new WF.ContextMenuStrip();
            m.Items.Add(new WF.ToolStripLabel("打开 ChatGPT 时") { Enabled = false });
            introItem = new WF.ToolStripMenuItem("播放开屏动画");
            introItem.Click += delegate { Update(delegate (Config c) { c.PlayIntro = !c.PlayIntro; }, null); };
            loopItem = new WF.ToolStripMenuItem("保留循环背景");
            loopItem.Click += delegate { Update(delegate (Config c) { c.LoopBackground = !c.LoopBackground; }, null); };
            m.Items.Add(introItem); m.Items.Add(loopItem);
            m.Items.Add(new WF.ToolStripSeparator());

            alphaLabel = new WF.ToolStripLabel("背景不透明度");
            alphaBar = MakeBar(80);
            alphaBar.ValueChanged += delegate
            {
                alphaLabel.Text = "背景不透明度  " + alphaBar.Value + "%";
                if (!menuLoading) Update(delegate (Config c) { c.ResidualAlpha = alphaBar.Value / 100.0; }, tray);
            };
            volLabel = new WF.ToolStripLabel("开屏音量");
            volBar = MakeBar(100);
            volBar.ValueChanged += delegate
            {
                volLabel.Text = "开屏音量  " + volBar.Value + "%";
                if (!menuLoading) Update(delegate (Config c) { c.IntroVolume = volBar.Value / 100.0; }, tray);
            };
            m.Items.Add(alphaLabel); m.Items.Add(new WF.ToolStripControlHost(alphaBar));
            m.Items.Add(volLabel); m.Items.Add(new WF.ToolStripControlHost(volBar));
            m.Items.Add(new WF.ToolStripSeparator());

            m.Items.Add(new WF.ToolStripLabel("现在，在 ChatGPT 窗口上") { Enabled = false });
            m.Items.Add("播放一次开屏动画", null, delegate { PreviewIntro(); });
            // 一个键开关：开着背景时是「关掉循环背景」，没开时是「开启循环背景」（文字在 RefreshMenu 里换）
            loopNowItem = new WF.ToolStripMenuItem("开启循环背景");
            loopNowItem.Click += delegate { ToggleLoopNow(); };
            m.Items.Add(loopNowItem);
            pauseItem = new WF.ToolStripMenuItem("暂停循环背景");
            pauseItem.Click += delegate { if (show != null) show.SetPaused(!show.Paused); };
            m.Items.Add(pauseItem);
            m.Items.Add(new WF.ToolStripSeparator());
            m.Items.Add("设置窗口…", null, delegate { OpenSettings(); });
            m.Items.Add("打开设置文件…", null, delegate { OpenConfig(); });
            m.Items.Add("卸载 GPT 开机动画…", null, delegate { Uninstall(); });
            m.Opening += delegate { Config = Config.Load(); RefreshMenu(); };
            tray.ContextMenuStrip = m;
            tray.MouseClick += delegate (object s, WF.MouseEventArgs ev) { if (ev.Button == WF.MouseButtons.Left) OpenSettings(); };
            RefreshMenu();
        }

        static WF.TrackBar MakeBar(int max)
        {
            return new WF.TrackBar { Minimum = 0, Maximum = max, TickStyle = WF.TickStyle.None, Width = 200, AutoSize = false, Height = 28, SmallChange = 1, LargeChange = 5 };
        }

        void RefreshMenu()
        {
            if (tray == null) return;
            menuLoading = true;
            introItem.Checked = Config.PlayIntro;
            loopItem.Checked = Config.LoopBackground;
            bool playing = show != null && show.Playing;
            loopNowItem.Text = !playing ? "开启循环背景" : show.InIntro ? "关掉开屏动画" : "关掉循环背景";
            pauseItem.Text = playing && show.Paused ? "继续播放循环背景" : "暂停循环背景";
            pauseItem.Enabled = playing && show.CanPause;
            alphaBar.Value = Math.Max(0, Math.Min(80, (int)Math.Round(Config.ResidualAlpha * 100)));
            volBar.Value = Math.Max(0, Math.Min(100, (int)Math.Round(Config.IntroVolume * 100)));
            alphaLabel.Text = "背景不透明度  " + alphaBar.Value + "%";
            volLabel.Text = "开屏音量  " + volBar.Value + "%";
            menuLoading = false;
        }

        /// Windows 11 默认把新托盘图标收进「^」里。第一次运行时把自己的图标设为常显（用户以后手动藏起来就不再动它）
        void PromoteTrayIcon()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings\" + name, true))
                        {
                            if (k == null) continue;
                            var path = k.GetValue("ExecutablePath") as string;
                            if (path == null || !string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.GetValue("IsPromoted") == null) { k.SetValue("IsPromoted", 1, RegistryValueKind.DWord); Log.W("托盘图标设为常显"); }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.W("托盘图标常显设置失败：" + ex.Message); }
        }

        // ── 测试 ──
        void RunTest()
        {
            string mode = Arg("--play") ?? "intro";
            double exitAfter = double.Parse(Arg("--exit-after") ?? "30", CultureInfo.InvariantCulture);
            string shotDir = Arg("--shot-dir");
            string shots = Arg("--shot-at");
            string skip = Arg("--test-skip");
            var startAt = DateTime.Now;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            bool started = false, skipped = false;
            var pending = new List<double>();
            if (shots != null) foreach (var s in shots.Split(',')) pending.Add(double.Parse(s, CultureInfo.InvariantCulture));
            DateTime showStart = DateTime.Now;
            timer.Tick += delegate
            {
                if (!started)
                {
                    if (StartOnCurrent(mode != "loop")) { started = true; showStart = DateTime.Now; }
                    else if ((DateTime.Now - startAt).TotalSeconds > 15) { Log.W("测试：找不到目标窗口"); Quit(); }
                    return;
                }
                double el = (DateTime.Now - showStart).TotalSeconds;
                if (skip != null && !skipped && el >= double.Parse(skip, CultureInfo.InvariantCulture) && show != null) { skipped = true; show.SkipIntro(); }
                for (int i = pending.Count - 1; i >= 0; i--)
                    if (el >= pending[i] && show != null && shotDir != null)
                    {
                        double at = pending[i]; pending.RemoveAt(i);
                        try
                        {
                            Directory.CreateDirectory(shotDir);
                            show.Snapshot(Path.Combine(shotDir, "shot_" + at.ToString("00.0", CultureInfo.InvariantCulture) + ".png"));
                        }
                        catch (Exception ex) { Log.W("截图失败：" + ex.Message); }
                    }
                if (el >= exitAfter) { timer.Stop(); if (show != null) show.Teardown(); Log.W("测试结束"); Quit(); }
            };
            timer.Start();
        }

        /// 离屏渲染字幕到图片（不需要显示器），测字体、大小、描边
        void RenderSubtitleTest()
        {
            try
            {
                string bg = Arg("--render-subtitle");
                string outPath = args[Array.IndexOf(args, "--render-subtitle") + 2];
                int idx = args.Length > Array.IndexOf(args, "--render-subtitle") + 3 ? int.Parse(args[Array.IndexOf(args, "--render-subtitle") + 3]) : 1;
                var img = new BitmapImage(new Uri(Path.GetFullPath(bg)));
                int W = img.PixelWidth, H = img.PixelHeight;
                var g = new Grid { Width = W, Height = H };
                g.Children.Add(new Image { Source = img, Stretch = Stretch.Fill });
                var s = new SubtitleElement { WidthRatio = Config.SubtitleWidthRatio, Opacity = 1 };
                s.RefText = Config.Subtitles[0].Text;
                s.SetText(Config.Subtitles[Math.Max(0, Math.Min(Config.Subtitles.Count - 1, idx - 1))].Text);
                g.Children.Add(s);
                g.Measure(new Size(W, H)); g.Arrange(new Rect(0, 0, W, H)); g.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(g);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(outPath)) enc.Save(f);
                Log.W("字幕渲染到 " + outPath + "  字体=" + SubtitleElement.Font.Source);
            }
            catch (Exception ex) { Log.W("字幕渲染失败：" + ex); }
            Shutdown();
        }
    }

    public static class Program
    {
        public static string DpiMode = "";
        [STAThread]
        public static int Main(string[] args)
        {
            // 缩放模式必须在最前面、在任何 WPF 类型被碰到之前定下来：
            // 系统准备一个函数时会先把它用到的类型都加载好，WPF 一加载就把进程定成「只认登录时缩放」，之后再设就被拒了。
            // 所以这里只做这一件事，其余的放到单独的 Run 里
            DpiMode = Native.UseDpiAwareness();
            return Run(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            GpuPref.Ensure();   // 要在 WPF 建立显卡渲染之前设好
            bool test = Array.IndexOf(args, "--target-title") >= 0 || Array.IndexOf(args, "--render-subtitle") >= 0 || Array.IndexOf(args, "--bench") >= 0;
            Mutex mutex = null;
            EventWaitHandle showEvent = null;
            if (!test)
            {
                bool created;
                mutex = new Mutex(true, @"Local\io.github.celestewynne0506-pixel.gpt-boot-animation", out created);
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\io.github.celestewynne0506-pixel.gpt-boot-animation.show");
                if (!created)
                {
                    // 已经在运行：再打开一次 = 弹出设置窗口
                    showEvent.Set();
                    return 0;
                }
            }
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.W("崩溃：" + e.ExceptionObject); };
            var app = new App(args);
            app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e) { Log.W("出错：" + e.Exception); e.Handled = true; };
            if (showEvent != null)
            {
                var ev = showEvent;
                var th = new Thread(delegate ()
                {
                    while (true) { ev.WaitOne(); app.Dispatcher.BeginInvoke(new Action(delegate { app.OpenSettings(); })); }
                }) { IsBackground = true };
                th.Start();
            }
            int code = app.Run();
            GC.KeepAlive(mutex);
            return code;
        }
    }
}

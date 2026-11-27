// AI 开机动画 · 合并版（Claude / ChatGPT / Antigravity 三合一）· Windows —— 外置浮层小程序
// 一个托盘图标统一控制三个开机动画：检测 Claude、ChatGPT（含 Codex）、Antigravity 启动 → 盖在各自的主窗口上播放各自的开屏动画
// → 进入循环帧后半透明、点击穿透，作为循环背景一直播放。不修改这三个应用的任何文件。
// 用系统自带的 .NET Framework 4.x 编译器编译（C# 5 语法），不需要装任何 SDK。
//
// 原作：wynneceles 的 ai-boot-animations（https://github.com/wynneceles/ai-boot-animations）
//   Windows 版 ClaudeBootAnimation.cs（v1.2.6）、GPTBootAnimation.cs（gpt-v1.2.5）、AntigravityBootAnimation.cs（gemini-v1.1.0），
//   原始文件在原作者仓库的 claude/ codex/ antigravity/ 各自的 windows/src 里（单独发布时另存了一份在 upstream\）。
// Copyright (C) 2026 wynneceles
//
// 修改说明：
//   2026-11-27 把原来三个独立的小程序合并成一个。三个版本之间不同的地方（目标进程、素材、字幕、
//     进入循环帧的时间点、托盘图标、文字）收进 Profile；共用一个托盘图标、一个设置窗口、一个开机自启。
//     播放逻辑（Show 类）保持原样，只是改成按 Profile 取素材、日志加上应用名前缀。开屏音量默认改为 0（静音）。
//   2026-11-27 设置改成三个应用共用一份 settings.json，每个应用用勾选框开关；「启动设置」窗口重做（SettingsView.xaml）；
//     点窗口的关闭按钮时可选缩到托盘或完全退出；改为按每块屏幕各自的 DPI 定位（Per-Monitor V2），
//     并且每一帧校正浮层位置，换屏幕、改缩放后不再错位。
//   2026-11-27 循环背景新增「背景适配」：浅色 / 深色 / 原样（ContrastBackground.hlsl：在原样画面上按每段动画的星空亮度
//     去背景，再加饱和、加对比调色；只作用于循环背景，开屏动画始终原样播放）；循环背景预设不透明度 10%；
//     去掉右上角的「跳过」，开屏时点画面任意位置即跳过。
//   2026-11-27 背景适配只留浅色 / 原样；「关闭窗口时」移到左栏，次要功能收进右下角「设定」小菜单；
//     托盘右键菜单改成 WPF 小窗口（TrayMenu.xaml，只有「启动设置 / 退出」）；exe 加上程序图标（Icons.Logo → app.ico）。
//
// 本程序以 GNU GPL v3 发布，见 LICENSE。修改版同样以 GPL-3.0 授权，并保留原作者署名。
// This program is free software under the GNU General Public License v3.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace AIBootAnimations
{
    // ───────────────────────── 三个应用 ─────────────────────────

    public class Subtitle
    {
        public string Text; public double Start; public double End;
        public Subtitle(string t, double s, double e) { Text = t; Start = s; End = e; }
    }

    /// 一个目标应用。原来三个小程序之间不同的地方都收在这里，其余代码三个应用共用
    public class Profile
    {
        public string Key;               // 设置里的名字和素材子文件夹：claude / gpt / antigravity
        public string Title;             // 原小程序的名字：Claude 开机动画 / GPT 开机动画 / Gemini 开机动画
        public string AppName;           // 目标应用：Claude / ChatGPT / Antigravity
        public double KeyLo, KeyHi;      // 循环背景去背景的亮度阈值：低于 KeyLo 的星空透明，高于 KeyHi 的人物保留（按各自星空亮度量出来的）
        public string[] ProcessNames;    // 目标窗口所属的进程名（不带 .exe）
        public bool RequireChromeClass;  // Claude 版只认 Electron 主窗口类名 Chrome_WidgetWin_1
        public string OverlayTitle;      // 浮层窗口标题
        public double FadeStartSeconds;  // 成片里进入循环帧的时间点（各动画不同）
        public bool ShowSubtitles;       // Gemini 成片已经烧录了字幕，程序不再另画
        public Subtitle[] Subtitles;
        public Action<SD.Graphics> DrawIcon;

        // ── 运行时状态 ──
        public Show Current;                                   // 正在播放的那一场
        public readonly HashSet<uint> SeenPids = new HashSet<uint>();
        public SD.Bitmap IconBitmap;                           // 托盘菜单用
        public ImageSource IconImage;                          // 设置窗口用（高清）

        public string AssetsDir { get { return Path.Combine(App.AssetsRoot, Key); } }

        public List<Subtitle> MakeSubtitles()
        {
            var list = new List<Subtitle>();
            foreach (var s in Subtitles) list.Add(new Subtitle(s.Text, s.Start, s.End));
            return list;
        }

        public static List<Profile> All()
        {
            return new List<Profile>
            {
                new Profile
                {
                    Key = "claude", Title = "Claude 开机动画", AppName = "Claude",
                    KeyLo = 0.14, KeyHi = 0.26,   // 暖棕色星空，亮度大多在 0~0.16
                    ProcessNames = new[] { "claude" }, RequireChromeClass = true,
                    OverlayTitle = "Claude Boot Animation",
                    FadeStartSeconds = 10.767, ShowSubtitles = true,
                    Subtitles = new[] {
                        new Subtitle("Under the moonlight... the blade awakens.", 4.087, 7.189),
                        new Subtitle("I am Claude. Ask me anything.", 8.281, 11.3),
                    },
                    DrawIcon = Icons.Claude
                },
                new Profile
                {
                    // ChatGPT 桌面端（Windows 上和 Codex 是同一个应用，包名 OpenAI.Codex，窗口进程 ChatGPT.exe）；单独装的 Codex 窗口进程可能叫 Codex.exe
                    Key = "gpt", Title = "GPT 开机动画", AppName = "ChatGPT",
                    KeyLo = 0.06, KeyHi = 0.16,   // 近乎纯黑的星空，亮度大多在 0~0.04
                    ProcessNames = new[] { "chatgpt", "codex" },
                    OverlayTitle = "GPT Boot Animation",
                    FadeStartSeconds = 11.4, ShowSubtitles = true,  // 甩牌落定后第 342 帧
                    Subtitles = new[] {
                        new Subtitle("Every answer... is already in my hand.", 3.2, 7.2),
                        new Subtitle("All I lack... is your question.", 8.65, 11.6),
                    },
                    DrawIcon = Icons.Gpt
                },
                new Profile
                {
                    // Antigravity（VS Code 系的 Electron 程序），窗口进程 Antigravity.exe
                    Key = "antigravity", Title = "Gemini 开机动画", AppName = "Antigravity",
                    KeyLo = 0.07, KeyHi = 0.18,   // 深色星空，亮度大多在 0~0.04，带一点拖尾
                    ProcessNames = new[] { "antigravity" },
                    OverlayTitle = "Gemini Boot Animation",
                    FadeStartSeconds = 12.93, ShowSubtitles = false, // 成片第 388 帧 ≈ 12.93s 起已经是循环片段
                    Subtitles = new[] {
                        new Subtitle("Where twin stars align, my blades carve a new horizon.", 2.68, 6.86),
                        new Subtitle("Ready to rewrite the universe?", 8.00, 10.20),
                    },
                    DrawIcon = Icons.Gemini
                },
            };
        }
    }

    /// 托盘和菜单的小图标：沿用原版三个小程序各自的图标，合并版托盘图标把三个拼在一起
    public static class Icons
    {
        static void Star(SD.Graphics g, float cx, float cy, float r, SD.Color color)
        {
            float k = r * 0.28f;
            var pts = new[] {
                new SD.PointF(cx, cy - r), new SD.PointF(cx + k, cy - k), new SD.PointF(cx + r, cy), new SD.PointF(cx + k, cy + k),
                new SD.PointF(cx, cy + r), new SD.PointF(cx - k, cy + k), new SD.PointF(cx - r, cy), new SD.PointF(cx - k, cy - k) };
            using (var b = new SD.SolidBrush(color)) g.FillPolygon(b, pts);
        }

        /// Claude 版：三颗金色 ✨
        public static void Claude(SD.Graphics g)
        {
            var gold = SD.Color.FromArgb(255, 255, 196, 64);
            Star(g, 13, 18, 12, gold); Star(g, 25, 7, 6, gold); Star(g, 26, 25, 4, gold);
        }

        /// GPT 版：月牙 + 两颗小星
        public static void Gpt(SD.Graphics g)
        {
            Moon(g);
            var pale = SD.Color.FromArgb(255, 190, 210, 255);
            Star(g, 25, 8, 5, pale); Star(g, 27, 22, 3.5f, pale);
        }

        static void Moon(SD.Graphics g)
        {
            using (var moon = new SD.Drawing2D.GraphicsPath())
            {
                moon.AddEllipse(2, 4, 24, 24);
                using (var cut = new SD.Drawing2D.GraphicsPath())
                {
                    cut.AddEllipse(10, 1, 22, 22);
                    using (var region = new SD.Region(moon))
                    {
                        region.Exclude(cut);
                        using (var b = new SD.SolidBrush(SD.Color.FromArgb(255, 222, 230, 255))) g.FillRegion(b, region);
                    }
                }
            }
        }

        /// Gemini 版：一颗弧边四芒星，蓝→紫渐变
        public static void Gemini(SD.Graphics g)
        {
            using (var path = new SD.Drawing2D.GraphicsPath())
            {
                float c = 16, r = 15, q = 1.5f;   // 中心、尖端半径、弧线往中心收的程度
                path.StartFigure();
                path.AddBezier(c, c - r, c + q, c - q, c + q, c - q, c + r, c);
                path.AddBezier(c + r, c, c + q, c + q, c + q, c + q, c, c + r);
                path.AddBezier(c, c + r, c - q, c + q, c - q, c + q, c - r, c);
                path.AddBezier(c - r, c, c - q, c - q, c - q, c - q, c, c - r);
                path.CloseFigure();
                using (var b = new SD.Drawing2D.LinearGradientBrush(new SD.PointF(2, 30), new SD.PointF(30, 2),
                    SD.Color.FromArgb(255, 66, 133, 244), SD.Color.FromArgb(255, 180, 140, 255)))
                    g.FillPath(b, path);
            }
        }

        /// 合并版托盘图标：左下金色大星（Claude）、右上月牙（GPT）、右下蓝紫四芒星（Gemini）。托盘里只有 16–24 像素，每个应用只取一个元素
        public static void Combined(SD.Graphics g)
        {
            Star(g, 11, 19, 11, SD.Color.FromArgb(255, 255, 196, 64));
            Scaled(g, Moon, 15, -1, 0.55f);
            Scaled(g, Gemini, 17.5f, 17.5f, 0.45f);
        }

        static void Scaled(SD.Graphics g, Action<SD.Graphics> draw, float x, float y, float s)
        {
            var state = g.Save();
            g.TranslateTransform(x, y); g.ScaleTransform(s, s);
            draw(g);
            g.Restore(state);
        }

        /// 程序图标（exe、窗口、任务栏）：深靛蓝到紫色的圆角方块（夜空），上面是金色大星（Claude）、月牙（GPT）、蓝紫四芒星（Gemini）
        public static void Logo(SD.Graphics g)
        {
            using (var bg = RoundedRect(1, 1, 30, 30, 7.5f))
            using (var b = new SD.Drawing2D.LinearGradientBrush(new SD.PointF(1, 1), new SD.PointF(31, 31),
                SD.Color.FromArgb(255, 37, 35, 104), SD.Color.FromArgb(255, 109, 40, 217)))
                g.FillPath(b, bg);
            using (var dot = new SD.SolidBrush(SD.Color.FromArgb(150, 255, 255, 255)))
            {
                g.FillEllipse(dot, 6, 6.5f, 1.3f, 1.3f); g.FillEllipse(dot, 25.5f, 26, 1.2f, 1.2f); g.FillEllipse(dot, 7.5f, 26.5f, 1f, 1f);
            }
            Star(g, 11.5f, 18.5f, 8, SD.Color.FromArgb(255, 255, 200, 80));
            Scaled(g, Moon, 16.5f, 3.5f, 0.42f);
            Scaled(g, Gemini, 17.5f, 16.5f, 0.36f);
        }

        static SD.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var p = new SD.Drawing2D.GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// 写多尺寸 .ico（每个尺寸一张 PNG），build.ps1 用 /win32icon 把它编进 exe
        public static void WriteIco(string path, Action<SD.Graphics> draw, int[] sizes)
        {
            var pngs = new List<byte[]>();
            foreach (var s in sizes)
                using (var bmp = Render(draw, s))
                using (var ms = new MemoryStream()) { bmp.Save(ms, SD.Imaging.ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) w.Write(png);
            }
        }

        /// 图标按 32×32 设计；size 更大时整体放大重画（不是拉伸位图），高 DPI 屏幕和设置窗口里也清楚
        public static SD.Bitmap Render(Action<SD.Graphics> draw, int size)
        {
            var bmp = new SD.Bitmap(size, size);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 32f, size / 32f);
                draw(g);
            }
            return bmp;
        }

        public static ImageSource ToImageSource(SD.Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, SD.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
    }


    // ───────────────────────── 设置 ─────────────────────────

    /// 一场播放实际用到的参数：由三个应用共用的设置 + 这个应用自己的动画参数拼出来
    public class Config
    {
        public bool PlayIntro = true;
        public bool LoopBackground = true;
        public double FadeStartSeconds;
        public double FadeDurationSeconds = 1.2;
        public double ResidualAlpha = 0.1;
        public double IntroVolume = 0;
        public bool ShowSubtitles;
        public double SubtitleFadeSeconds = 0.5;
        public double SubtitleWidthRatio = 0.6;
        public List<Subtitle> Subtitles = new List<Subtitle>();
        public bool ContrastOn = true;        // 循环背景去背景 + 调色；false = 原版的普通半透明
        public bool ContrastLight = true;     // true 按浅色界面调色，false 按深色界面调色
    }

    /// 每个应用自己的部分：开关，以及和动画本身绑定的参数（字段名和原版 config.json 一样）
    public class AppSettings
    {
        public bool Enabled = true;
        public double FadeStartSeconds;
        public double FadeDurationSeconds = 1.2;
        public bool ShowSubtitles;
        public double SubtitleFadeSeconds = 0.5;
        public double SubtitleWidthRatio = 0.6;
        public List<Subtitle> Subtitles;

        public AppSettings(Profile p) { FadeStartSeconds = p.FadeStartSeconds; ShowSubtitles = p.ShowSubtitles; Subtitles = p.MakeSubtitles(); }
    }

    /// 三个应用共用的一份设置：%APPDATA%\AIBootAnimations\settings.json
    public class Settings
    {
        public bool PlayIntro = true;            // 应用启动时播放开屏动画
        public bool LoopBackground = true;       // 开屏后保留半透明循环背景
        public double ResidualAlpha = 0.10;      // 循环背景不透明度。原版默认 25%，压字太明显，预设 10%
        public double IntroVolume = 0;           // 开屏音量（再乘系统音量）。默认静音
        public string ContrastMode = "light";    // 背景适配：light 浅色（去背景 + 调色）/ off 原样
        public string CloseAction = "tray";      // 点「启动设置」的关闭按钮：tray 缩到托盘 / exit 完全退出
        public bool TrayTipShown;                // 第一次缩到托盘时提示过了
        public readonly Dictionary<string, AppSettings> Apps = new Dictionary<string, AppSettings>();
        readonly List<Profile> profiles;

        public Settings(List<Profile> profiles)
        {
            this.profiles = profiles;
            foreach (var p in profiles) Apps[p.Key] = new AppSettings(p);
        }

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIBootAnimations"); } }
        public static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public AppSettings Of(Profile p) { return Apps[p.Key]; }

        public Config ConfigFor(Profile p)
        {
            var a = Of(p);
            return new Config
            {
                PlayIntro = PlayIntro, LoopBackground = LoopBackground,
                ResidualAlpha = ResidualAlpha, IntroVolume = IntroVolume,
                FadeStartSeconds = a.FadeStartSeconds, FadeDurationSeconds = a.FadeDurationSeconds,
                ShowSubtitles = a.ShowSubtitles, SubtitleFadeSeconds = a.SubtitleFadeSeconds, SubtitleWidthRatio = a.SubtitleWidthRatio,
                Subtitles = a.Subtitles,
                ContrastOn = ContrastMode != "off",
                ContrastLight = true,
            };
        }

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
        static string S(Dictionary<string, object> d, string k, string def)
        {
            object v; return d.TryGetValue(k, out v) && v is string ? (string)v : def;
        }
        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
        static Dictionary<string, object> Parse(string json) { return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }

        public static Settings Load(List<Profile> profiles)
        {
            var s = new Settings(profiles);
            bool migrated = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (d != null) s.Read(d);
                }
                else migrated = s.MigrateFromPerAppFiles();
            }
            catch (Exception e) { Log.W("设置文件读不了，用默认值：" + e.Message); }
            if (s.Save() && migrated) s.DeletePerAppFiles(); // 合并成功后再删旧文件
            return s;
        }

        void Read(Dictionary<string, object> d)
        {
            PlayIntro = B(d, "playIntro", PlayIntro);
            LoopBackground = B(d, "loopBackground", LoopBackground);
            ResidualAlpha = Clamp(D(d, "residualAlpha", ResidualAlpha), 0, 0.8);
            IntroVolume = Clamp(D(d, "introVolume", IntroVolume), 0, 1);
            ContrastMode = S(d, "contrastMode", ContrastMode) == "off" ? "off" : "light";   // 上一版的「自动」「深色」都并入浅色
            CloseAction = S(d, "closeAction", CloseAction) == "exit" ? "exit" : "tray";
            TrayTipShown = B(d, "trayTipShown", TrayTipShown);
            object apps;
            if (d.TryGetValue("apps", out apps) && apps is Dictionary<string, object>)
            {
                var ad = (Dictionary<string, object>)apps;
                foreach (var p in profiles)
                {
                    object o;
                    if (ad.TryGetValue(p.Key, out o) && o is Dictionary<string, object>) ReadApp(Of(p), (Dictionary<string, object>)o);
                }
            }
        }

        static void ReadApp(AppSettings a, Dictionary<string, object> d)
        {
            a.Enabled = B(d, "enabled", a.Enabled);
            a.FadeStartSeconds = D(d, "fadeStartSeconds", a.FadeStartSeconds);
            a.FadeDurationSeconds = D(d, "fadeDurationSeconds", a.FadeDurationSeconds);
            a.ShowSubtitles = B(d, "showSubtitles", a.ShowSubtitles);
            a.SubtitleFadeSeconds = D(d, "subtitleFadeSeconds", a.SubtitleFadeSeconds);
            a.SubtitleWidthRatio = D(d, "subtitleWidthRatio", a.SubtitleWidthRatio);
            object subs;
            if (d.TryGetValue("subtitles", out subs) && subs is object[])
            {
                var list = new List<Subtitle>();
                foreach (var o in (object[])subs)
                {
                    var x = o as Dictionary<string, object>;
                    if (x == null || !x.ContainsKey("text")) continue;
                    list.Add(new Subtitle(Convert.ToString(x["text"]), D(x, "start", 0), D(x, "end", 0)));
                }
                a.Subtitles = list;
            }
        }

        /// 上一版每个应用一份 claude.json / gpt.json / antigravity.json（从原版三个小程序迁移过来的也是这样）：合并成一份
        bool MigrateFromPerAppFiles()
        {
            bool any = false;
            foreach (var p in profiles)
            {
                string f = Path.Combine(Dir, p.Key + ".json");
                if (!File.Exists(f)) continue;
                try
                {
                    var d = Parse(File.ReadAllText(f, Encoding.UTF8));
                    if (d == null) continue;
                    ReadApp(Of(p), d);
                    if (!any)
                    {
                        PlayIntro = B(d, "playIntro", PlayIntro);
                        LoopBackground = B(d, "loopBackground", LoopBackground);
                        IntroVolume = Clamp(D(d, "introVolume", IntroVolume), 0, 1);
                        // 不透明度不沿用旧值：旧默认 25% 压暗文字，统一换成新的默认 10%
                    }
                    any = true;
                }
                catch (Exception e) { Log.W("旧设置 " + f + " 读不了：" + e.Message); }
            }
            if (any) Log.W("已把分应用的旧设置合并进 settings.json");
            return any;
        }

        void DeletePerAppFiles()
        {
            foreach (var p in profiles)
            {
                try { File.Delete(Path.Combine(Dir, p.Key + ".json")); } catch { }
            }
        }

        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string L(bool v) { return v ? "true" : "false"; }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var js = new JavaScriptSerializer();
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"closeAction\" : " + js.Serialize(CloseAction) + ",\n");
                sb.Append("  \"contrastMode\" : " + js.Serialize(ContrastMode) + ",\n");
                sb.Append("  \"introVolume\" : " + N(IntroVolume) + ",\n");
                sb.Append("  \"loopBackground\" : " + L(LoopBackground) + ",\n");
                sb.Append("  \"playIntro\" : " + L(PlayIntro) + ",\n");
                sb.Append("  \"residualAlpha\" : " + N(ResidualAlpha) + ",\n");
                sb.Append("  \"trayTipShown\" : " + L(TrayTipShown) + ",\n");
                sb.Append("  \"apps\" : {\n");
                for (int i = 0; i < profiles.Count; i++)
                {
                    var p = profiles[i]; var a = Of(p);
                    sb.Append("    \"" + p.Key + "\" : {\n");
                    sb.Append("      \"enabled\" : " + L(a.Enabled) + ",\n");
                    sb.Append("      \"fadeDurationSeconds\" : " + N(a.FadeDurationSeconds) + ",\n");
                    sb.Append("      \"fadeStartSeconds\" : " + N(a.FadeStartSeconds) + ",\n");
                    sb.Append("      \"showSubtitles\" : " + L(a.ShowSubtitles) + ",\n");
                    sb.Append("      \"subtitleFadeSeconds\" : " + N(a.SubtitleFadeSeconds) + ",\n");
                    sb.Append("      \"subtitleWidthRatio\" : " + N(a.SubtitleWidthRatio) + ",\n");
                    sb.Append("      \"subtitles\" : [\n");
                    for (int j = 0; j < a.Subtitles.Count; j++)
                    {
                        var t = a.Subtitles[j];
                        sb.Append("        { \"start\" : " + N(t.Start) + ", \"end\" : " + N(t.End) + ", \"text\" : " + js.Serialize(t.Text) + " }");
                        sb.Append(j < a.Subtitles.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append("      ]\n    }");
                    sb.Append(i < profiles.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  }\n}\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception e) { Log.W("设置文件写不了：" + e.Message); return false; }
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
        public static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBootAnimations", "log.txt");
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
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        /// 按每块屏幕各自的 DPI 工作（PerMonitorV2）：取到的窗口位置和摆放浮层用的坐标都是真实像素，
        /// 缩放不同的几块屏幕之间切换时浮层不会错位。清单里已经声明过，这里是清单没生效时的保险
        public static void EnablePerMonitorDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }   // PerMonitorV2（Win10 1703+）
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }                   // PerMonitor（Win8.1+）
            try { SetProcessDPIAware(); } catch { }
        }

        public static string DpiModeName()
        {
            try
            {
                var ctx = GetThreadDpiAwarenessContext();
                if (AreDpiAwarenessContextsEqual(ctx, new IntPtr(-4))) return "PerMonitorV2";
                int a = GetAwarenessFromDpiAwarenessContext(ctx);
                return a == 2 ? "PerMonitor" : a == 1 ? "System" : "Unaware";
            }
            catch { return "未知"; }
        }

        /// Windows 11：窗口用圆角
        public static void RoundCorners(IntPtr h)
        {
            try { int v = 2 /*DWMWCP_ROUND*/; DwmSetWindowAttribute(h, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref v, 4); } catch { }
        }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);

        /// 某一点所在屏幕的工作区（去掉任务栏），真实像素
        public static RECT WorkAreaAt(POINT p)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/), ref mi);
            return mi.rcWork;
        }

        /// 某一点所在屏幕的缩放（1.0 = 100%）
        public static double ScaleAt(POINT p)
        {
            try { uint x, y; if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out x, out y) == 0) return x / 96.0; } catch { }
            return 1.0;
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

    // ───────────────────────── 找目标应用的主窗口 ─────────────────────────

    public struct Target { public IntPtr Hwnd; public uint Pid; public bool Valid { get { return Hwnd != IntPtr.Zero; } } }

    public static class Finder
    {
        /// 测试用：按窗口标题找（比如记事本），不找目标应用
        public static string TestTitle;
        static readonly Dictionary<uint, string> NameCache = new Dictionary<uint, string>();
        static readonly uint Self = (uint)Process.GetCurrentProcess().Id;

        static string ProcName(uint pid)
        {
            string n;
            if (NameCache.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = ""; }
            if (NameCache.Count > 500) NameCache.Clear();
            NameCache[pid] = n;
            return n;
        }

        /// 找某个应用最大的那个可见主窗口。三个都是 Electron 程序，会有好几个进程和隐藏窗口，只挑看得见、够大的顶层窗口
        public static Target Find(Profile p)
        {
            Target best = new Target(); long bestArea = 0;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.Cloaked(h)) return true;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == Self) return true;
                if (TestTitle != null)
                {
                    if (Native.TitleOf(h).IndexOf(TestTitle, StringComparison.OrdinalIgnoreCase) < 0) return true;
                }
                else
                {
                    if (p.RequireChromeClass && Native.ClassOf(h) != "Chrome_WidgetWin_1") return true;
                    string pn = ProcName(pid);
                    bool match = false;
                    foreach (var name in p.ProcessNames)
                        if (string.Equals(pn, name, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                    if (!match) return true;
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
        public static int RecycleEvery = 10;    // --recycle-every 圈：每放这么多圈，把后台待命的播放器换成新的（0 = 不换）
        public static double HeartbeatSec = 600; // --heartbeat 秒：循环背景每隔多久往日志里报一次平安
    }

    // ───────────────────────── 一次播放 ─────────────────────────

    public class Show
    {
        enum Phase { Intro, Revealed, Ending, Done }
        Phase phase = Phase.Intro;

        public readonly Target Target;
        readonly Profile profile;             // 合并版：这一场属于哪个应用（素材、日志前缀、浮层标题）
        Config config;
        readonly bool withIntro;
        readonly Action onDone;

        Window overlay, top;
        IntPtr overlayH, topH;
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
        // 循环背景的对比度适配（合并版新增，只在循环背景阶段挂上）
        ContrastEffect fx;

        string Asset(string name) { return Path.Combine(profile.AssetsDir, name); }

        /// 日志带上应用名，三个应用写同一份日志也分得清
        void L(string msg) { Log.W("[" + profile.AppName + "] " + msg); }

        public Show(Profile p, Target t, Config c, bool withIntro, Action onDone)
        {
            profile = p; Target = t; config = c; this.withIntro = withIntro; this.onDone = onDone;
            disp = Dispatcher.CurrentDispatcher;   // 窗口一建好就可能收到 DPI 变化通知，要先有它

            grid = new Grid { Background = withIntro ? Brushes.Black : Brushes.Transparent, ClipToBounds = true };
            // 有 loop-x2.mp4（循环段连放两遍）就用「重叠交接」；
            // 用户放了自己的 loop.mp4（比 loop-x2.mp4 新）或只有 loop.mp4，就用老办法，免得换的视频被无视
            string x2 = Asset("loop-x2.mp4"), single = Asset("loop.mp4");
            overlapLoop = !ForceLegacyLoop && File.Exists(x2)
                && !(File.Exists(single) && File.GetLastWriteTimeUtc(single) > File.GetLastWriteTimeUtc(x2).AddSeconds(2));
            loopFile = overlapLoop ? "loop-x2.mp4" : "loop.mp4";
            L(overlapLoop ? "循环段：重叠交接（loop-x2.mp4）" : "循环段：放完再接（loop.mp4）");
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
                intro.MediaFailed += delegate (object s, ExceptionRoutedEventArgs e) { L("开屏视频播不了：" + e.ErrorException.Message); };
            }
            else introGone = true;

            // 一个窗口三层：最下面视频（整体调透明度做半透明背景），上面字幕和「跳过」（不跟着变淡）
            root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(grid);
            overlay = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                ShowActivated = false, Background = Brushes.Transparent, Content = root,
                Left = -32000, Top = -32000, Width = 10, Height = 10, Title = profile.OverlayTitle
            };
            // 浮层窗口被外部关掉（比如别的程序发了关闭消息）：这一场直接收工，不再对着已经没了的窗口补样式
            overlay.Closed += delegate { if (phase != Phase.Done) { L("浮层窗口被关掉了，收工"); Teardown(); } };
            clickThrough = !withIntro;
            alpha = 0; // 开屏也从透明淡入（0.3 秒），不再一出现就整块黑屏盖上去
            grid.Opacity = alpha;
            overlay.SourceInitialized += delegate
            {
                overlayH = new WindowInteropHelper(overlay).Handle;
                EnsureOverlayStyle(true);
                // 收系统的睡眠唤醒、显示器亮灭通知（这两种之后视频画面最容易冻住）
                var hs = HwndSource.FromHwnd(overlayH);
                if (hs != null) hs.AddHook(WndProc);
                try { Guid g = Native.GUID_CONSOLE_DISPLAY_STATE; powerNotify = Native.RegisterPowerSettingNotification(overlayH, ref g, 0); }
                catch (Exception ex) { L("显示器亮灭通知注册失败：" + ex.Message); }
                // 设成目标窗口的「附属窗口」：系统自动让它一直在目标窗口上面，目标窗口被别的窗口挡住时它也一起被挡住
                Native.SetLong(overlayH, Native.GWLP_HWNDPARENT, Target.Hwnd.ToInt64());
            };

            if (withIntro)
            {
                var topGrid = root;
                sub = new SubtitleElement { WidthRatio = config.SubtitleWidthRatio };
                if (config.Subtitles.Count > 0) sub.RefText = config.Subtitles[0].Text;
                topGrid.Children.Add(sub);
                // 合并版：不再显示右上角的「跳过」，开屏时在画面上任意位置点一下就跳过
                //（进入循环背景后浮层是点击穿透的，点击直接落到应用上，不会误触）
                root.MouseLeftButtonUp += delegate { if (phase == Phase.Intro) SkipIntro(); };
            }

            overlay.Show();
            EnsureOverlayStyle(true); // WPF 显示窗口时会重写扩展样式，显示后再补一次
            if (top != null) top.Show();
            Follow(true);
            L("浮层样式 0x" + Native.GetLong(overlayH, Native.GWL_EXSTYLE).ToString("X"));

            if (withIntro)
            {
                intro.Play();
                RampAlpha(1, 0.3, null);
                L("开始播放开屏动画，窗口 " + Target.Hwnd + " " + RectStr(lastRect));
            }
            else
            {
                loopA.Play();
                phase = Phase.Revealed;
                UpdateEffect();
                RampAlpha(config.ResidualAlpha, config.FadeDurationSeconds, null);
                L("直接开启循环背景，窗口 " + Target.Hwnd);
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
            catch (Exception ex) { L("系统事件订阅失败：" + ex.Message); }
            nextBeatMs = (long)(Dev.HeartbeatSec * 1000);
            nextStallTestMs = (long)(Dev.StallTestSec * 1000);

            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += delegate { Tick(); };
            tick.Start();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x02E0 /*WM_DPICHANGED*/)
            {
                // 浮层跨到缩放不同的屏幕：WPF 会按系统建议改窗口大小，处理完以后马上按目标窗口重新摆一次
                if (disp != null) disp.BeginInvoke(new Action(delegate { if (phase != Phase.Done) Follow(true); }));
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
                        if (state != displayState && displayState >= 0) L("显示器状态 " + displayState + " → " + state);
                        displayState = state;
                    }
                }
            }
            catch (Exception ex) { L("电源通知处理出错：" + ex.Message); }
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
            L("循环背景播放器报错：" + (e.ErrorException != null ? e.ErrorException.Message : "未知"));
            LaterRebuild("播放器报错", 500);
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
                L("接缝：新播放器 1.5 秒还没动，再催一次（第 " + laps + " 圈）");
                swapTo.Play();
            }
        }

        void FinishSwap()
        {
            var from = swapFrom; var to = swapTo;
            swapFrom = null; swapTo = null;
            if (from == intro)
            {
                intro.Visibility = Visibility.Collapsed;
                intro.Stop(); intro.Close();
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
            if (!overlapLoop || phase == Phase.Done || !introGone || swapFrom != null || activeLoop == null) return;
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
                L("循环交接（第 " + handoffs + " 次）：错位 " + Math.Round(offset * 1000) + "ms，下次提前 " + Math.Round(handLead * 1000) + "ms");
        }

        /// 后台待命的那个播放器定期换成新的：同一个播放器暂停、倒回、再放成百上千次，不让它一直用下去。
        /// 它此刻垫在下面看不见，换掉不影响画面；新的自己会停在第一帧等着
        void ReplaceStandby(MediaElement old)
        {
            var fresh = MakeLoopPlayer();
            Panel.SetZIndex(fresh, 0);
            grid.Children.Add(fresh);
            if (old == loopA) loopA = fresh; else loopB = fresh;
            DisposePlayer(old);
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
            failStreak++;
            recoveries++; lastRecoverMs = now;
            double p = -1;
            try { if (activeLoop != null) p = activeLoop.Position.TotalSeconds; } catch { }
            L("循环背景自动恢复（" + why + "）：第 " + recoveries + " 次，已循环 " + laps + " 圈，当时进度 " + p.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
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
            if (retiring == null) return;
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
            if (timerGap || phase != Phase.Revealed || !introGone || hidden || activeLoop == null)
            {
                lastMoveMs = now; lastPos = -1; // 藏着、还在开屏时不算；回来后重新计时
                return;
            }
            double p = activeLoop.Position.TotalSeconds;
            if (now >= nextBeatMs)
            {
                nextBeatMs = now + (long)(Math.Max(1, Dev.HeartbeatSec) * 1000);
                string mem = "";
                try { using (var pr = Process.GetCurrentProcess()) mem = "，内存 " + (pr.WorkingSet64 / 1048576) + "MB，句柄 " + pr.HandleCount; } catch { }
                L("循环背景运行中：已循环 " + laps + " 圈，自动恢复 " + recoveries + " 次，进度 " + p.ToString("0.00", CultureInfo.InvariantCulture) + " 秒" + mem);
            }
            if (Math.Abs(p - lastPos) > 0.0005)
            {
                lastPos = p; lastMoveMs = now;
                if (verifying && p > 0.3) { verifying = false; failStreak = 0; L("恢复成功：循环背景又动起来了"); }
                return;
            }
            long stuck = now - lastMoveMs;
            // 停在最后一帧却没收到「放完」通知：直接手动接到另一个播放器，跟正常接缝一样无缝
            if (stuck >= 400 && swapFrom == null && activeLoop.NaturalDuration.HasTimeSpan
                && p >= activeLoop.NaturalDuration.TimeSpan.TotalSeconds - 0.08)
            {
                missedEnds++;
                if (missedEnds <= 20 || missedEnds % 100 == 0) L("循环段放到头没收到放完通知，手动接上（第 " + missedEnds + " 次）");
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
            if (phase != Phase.Revealed || !introGone || activeLoop == null) return;
            if (Dev.LoopCutSec > 0 && !overlapLoop && swapFrom == null && activeLoop.Position.TotalSeconds >= Dev.LoopCutSec)
                SwapLoop(activeLoop, OtherLoop(activeLoop));
            if (Dev.StallTestSec > 0 && now >= nextStallTestMs)
            {
                nextStallTestMs = now + (long)(Dev.StallTestSec * 1000);
                L("测试：故意把正在放的循环播放器暂停，等自愈接回来");
                activeLoop.Pause();
            }
        }

        void IntroEnded()
        {
            if (introGone) return;
            introGone = true;
            L("开屏放完，接循环段");
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
            if (pendingAt > 0 && now >= pendingAt && !hidden && phase == Phase.Revealed && introGone)
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
            if (alphaDur >= 0)
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
            if (fx != null) fx.Mix = MixFor();
        }

        /// 跟着目标窗口移动、缩放；目标应用最小化或藏到托盘时一起藏起来；目标应用关了就收工
        void Follow(bool force)
        {
            if (!Native.IsWindow(Target.Hwnd))
            {
                L("窗口没了，收工");
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
            if (!force && SameRect(r, lastRect))
            {
                // 目标窗口没动，再核对一下浮层自己的位置：跨屏幕时系统按 DPI 改过浮层大小、或者被别的原因挪开了，就拉回来
                Native.RECT o;
                if (overlayH == IntPtr.Zero || !Native.GetWindowRect(overlayH, out o) || SameRect(o, r)) return;
            }
            lastRect = r;
            uint f = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            if (overlayH != IntPtr.Zero) Native.SetWindowPos(overlayH, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, f);
            if (topH != IntPtr.Zero) Native.SetWindowPos(topH, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, f);
        }

        static bool SameRect(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
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
            if (clickThrough) want |= Native.WS_EX_TRANSPARENT;
            if (want != ex && styleFixes < 20)
            {
                if (!force) { styleFixes++; L("浮层样式被改过，已补回 0x" + ex.ToString("X") + " → 0x" + want.ToString("X")); }
                Native.SetLong(overlayH, Native.GWL_EXSTYLE, want);
            }
        }

        void SetAlpha(double a)
        {
            alpha = Math.Max(0, Math.Min(1, a));
            grid.Opacity = alpha;
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
                if (currentSub >= 0 && sub.Opacity > 0) L("字幕 " + (currentSub + 1) + " 消失，视频时间 " + t.ToString("0.000"));
                sub.Opacity = 0; currentSub = idx >= 0 ? idx : currentSub;
                if (currentSub == config.Subtitles.Count - 1 && phase != Phase.Intro) CloseTop(); // 最后一句放完，字幕层收掉
                return;
            }
            var s = config.Subtitles[idx];
            if (idx != currentSub) { currentSub = idx; sub.SetText(s.Text); L("字幕 " + (idx + 1) + " 出现，视频时间 " + t.ToString("0.000")); }
            sub.Opacity = Math.Max(0, Math.Min(1, Math.Min((t - s.Start) / fade, (s.End - t) / fade)));
        }

        void HideSubtitles()
        {
            if (sub == null) return;
            sub.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
            sub = null;
        }

        /// 进入循环帧：透出应用界面，点击穿透
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
                L("进入循环帧，循环背景已关，淡出");
                Finish(false); return;
            }
            L("进入循环帧，透出界面");
            UpdateEffect();   // 此刻整窗还是不透明，Mix = 0，画面不会跳；随着淡出过渡到对比度适配
            RampAlpha(config.ResidualAlpha, quick ? 0.9 : config.FadeDurationSeconds, null);
        }

        /// 点「跳过」：开着循环背景就淡出开屏、接到循环段，否则整个关掉
        public void SkipIntro()
        {
            if (phase != Phase.Intro) return;
            L("点了跳过开屏");
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
            UpdateEffect();
        }

        /// 循环背景阶段才挂「去背景 + 调色」着色器；开屏阶段不挂，开屏动画始终原样播放
        void UpdateEffect()
        {
            if (phase != Phase.Revealed) return;
            bool want = config.ContrastOn && ContrastEffect.Shader != null;
            if (!want)
            {
                if (fx != null) { grid.Effect = null; fx = null; L("循环背景：原样半透明"); }
                return;
            }
            bool fresh = fx == null;
            if (fresh) { fx = new ContrastEffect(); grid.Effect = fx; }
            bool light = config.ContrastLight;
            if (fresh || fx.Light != (light ? 1 : 0)) L("循环背景：去背景 + " + (light ? "浅色" : "深色") + "调色");
            fx.Light = light ? 1 : 0;
            fx.KeyLo = profile.KeyLo;
            fx.KeyHi = profile.KeyHi;
            fx.Mix = MixFor();
        }

        /// 从开屏过渡到循环背景时，跟着整窗透明度一起从「原样」渐变到「适配」
        double MixFor()
        {
            if (phase == Phase.Intro) return 0;
            double r = config.ResidualAlpha;
            if (r >= 0.999) return 1;
            return Math.Max(0, Math.Min(1, (1 - alpha) / (1 - r)));
        }

        public void Finish(bool fast)
        {
            if (phase == Phase.Ending || phase == Phase.Done) return;
            phase = Phase.Ending;
            clickThrough = true; EnsureOverlayStyle(true);
            double d = fast ? 0.4 : 1.2;
            L(fast ? "关闭（跳过）" : "淡出");
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
            try { SystemEvents.SessionSwitch -= onSession; SystemEvents.DisplaySettingsChanged -= onDisplay; } catch { }
            if (powerNotify != IntPtr.Zero) { try { Native.UnregisterPowerSettingNotification(powerNotify); } catch { } powerNotify = IntPtr.Zero; }
            foreach (var m in new[] { intro, loopA, loopB, retiring })
                if (m != null) { try { m.Stop(); m.Close(); } catch { } }
            CloseTop();
            try { overlay.Close(); } catch { }
            L("播放结束");
            if (onDone != null) onDone();
        }

        public bool Active { get { return phase != Phase.Done; } }

        /// 测试用：把目标窗口那块屏幕截下来
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
            L("截图 " + path + "  播放阶段=" + phase + " 透明度=" + alpha.ToString("0.00") + " 样式=0x" + Native.GetLong(overlayH, Native.GWL_EXSTYLE).ToString("X") + " 中心点击落到=" + who);
        }
    }


    // ───────────────────────── 背景适配：去背景 + 调色（只用于循环背景）─────────────────────────

    /// 循环背景的像素着色器：源码 src\ContrastBackground.hlsl，编译好的字节码作为资源嵌在程序里。
    /// 普通半透明会把整块星空也叠上去：浅色界面被压出一层灰、深色界面被提亮，文字对比度跟着下降。
    /// 这里在原样画面上按亮度把星空抠成透明，只留人物和光效，再加饱和、加对比；
    /// 浅色界面把中间调压深一点，深色界面提亮一点。Mix = 0 时原样输出，和原版完全一样。
    public class ContrastEffect : ShaderEffect
    {
        static PixelShader shader;
        static bool tried;

        public static PixelShader Shader
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("ContrastBackground.ps");
                        if (st != null) { shader = new PixelShader(); shader.SetStreamSource(st); }
                        else Log.W("没找到对比度着色器，循环背景用普通半透明");
                    }
                    catch (Exception ex) { shader = null; Log.W("对比度着色器载入失败，循环背景用普通半透明：" + ex.Message); }
                }
                return shader;
            }
        }

        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(ContrastEffect), 0);
        public static readonly DependencyProperty MixProperty = DependencyProperty.Register("Mix", typeof(double), typeof(ContrastEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty LightProperty = DependencyProperty.Register("Light", typeof(double), typeof(ContrastEffect), new UIPropertyMetadata(1.0, PixelShaderConstantCallback(1)));
        public static readonly DependencyProperty KeyLoProperty = DependencyProperty.Register("KeyLo", typeof(double), typeof(ContrastEffect), new UIPropertyMetadata(0.08, PixelShaderConstantCallback(2)));
        public static readonly DependencyProperty KeyHiProperty = DependencyProperty.Register("KeyHi", typeof(double), typeof(ContrastEffect), new UIPropertyMetadata(0.20, PixelShaderConstantCallback(3)));

        public ContrastEffect()
        {
            PixelShader = Shader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(MixProperty);
            UpdateShaderValue(LightProperty);
            UpdateShaderValue(KeyLoProperty);
            UpdateShaderValue(KeyHiProperty);
        }

        public Brush Input { get { return (Brush)GetValue(InputProperty); } set { SetValue(InputProperty, value); } }
        public double Mix { get { return (double)GetValue(MixProperty); } set { SetValue(MixProperty, value); } }
        public double Light { get { return (double)GetValue(LightProperty); } set { SetValue(LightProperty, value); } }
        public double KeyLo { get { return (double)GetValue(KeyLoProperty); } set { SetValue(KeyLoProperty, value); } }
        public double KeyHi { get { return (double)GetValue(KeyHiProperty); } set { SetValue(KeyHiProperty, value); } }
    }

    // ───────────────────────── 托盘右键菜单 ─────────────────────────

    /// 托盘图标右键的小菜单：界面在 src\TrayMenu.xaml（白底圆角、阴影，和「启动设置」一个风格），只有「启动设置」和「退出」。
    /// 出现在鼠标位置，往上展开；点别处或按 Esc 就收起
    public class TrayMenu : Window
    {
        readonly FrameworkElement view;
        Native.POINT anchor;

        public TrayMenu(App app)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
            Opacity = 0;   // 摆好位置再显示，不闪
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayMenu.xaml"))
                view = (FrameworkElement)XamlReader.Load(st);
            Content = view;
            var icon = (Image)view.FindName("OpenIcon");
            icon.Source = Icons.ToImageSource(Icons.Render(Icons.Logo, 64));
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            ((Button)view.FindName("OpenItem")).Click += delegate { Close(); app.OpenSettings(); };
            ((Button)view.FindName("QuitItem")).Click += delegate { Close(); app.Quit(); };
            Deactivated += delegate { Dispatcher.BeginInvoke(new Action(delegate { if (IsLoaded) Close(); })); };
            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            SizeChanged += delegate { if (IsVisible) Place(); };   // 换到缩放不同的屏幕时 WPF 会改大小，重新对位
        }

        public void ShowAtCursor()
        {
            Native.GetCursorPos(out anchor);
            double scale = Native.ScaleAt(anchor);
            Left = anchor.X / scale; Top = anchor.Y / scale;   // 先放到鼠标所在的屏幕上，下面再按真实像素对准
            Show();
            Place();
            var h = new WindowInteropHelper(this).Handle;
            Native.SetForegroundWindow(h);
            Activate();
            Opacity = 1;
        }

        /// 菜单左下角对准鼠标（扣掉外圈留给阴影的 12 像素），往上展开；碰到屏幕边就往里收
        void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return;
            int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            int pad = (int)Math.Round(12 * Native.ScaleAt(anchor));
            var work = Native.WorkAreaAt(anchor);
            int x = anchor.X - pad, y = anchor.Y - ht + pad;
            x = Math.Max(work.Left, Math.Min(x, work.Right - w));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - ht));
            if (x != r.Left || y != r.Top)
                Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }

    // ───────────────────────── 启动设置窗口 ─────────────────────────

    /// 「启动设置」：界面在 src\SettingsView.xaml（作为资源嵌在程序里），这里只负责接线
    public class SettingsWindow : Window
    {
        readonly App app;
        public readonly FrameworkElement View;
        readonly Dictionary<Profile, CheckBox> appBoxes = new Dictionary<Profile, CheckBox>();
        readonly Dictionary<Profile, Button> previewButtons = new Dictionary<Profile, Button>();
        CheckBox introBox, loopBox;
        Slider alphaSlider, volSlider;
        TextBox alphaBox, volBox;
        RadioButton modeLight, modeOff, closeTray, closeExit;
        System.Windows.Controls.Primitives.Popup morePopup;   // 右下角「设定」点开的小菜单
        Button moreButton;
        DateTime morePopupClosedAt;
        Border modeThumb;                 // 浅色 / 深色 / 原样 下面那块会滑动的白色滑块
        int modeIndex = -1;
        bool loading, typing;

        public SettingsWindow(App app)
        {
            this.app = app;
            Title = "启动设置";
            Width = 760; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;   // 两栏：左边应用和开关，右边背景、声音和关闭方式
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
            UseLayoutRounding = true;
            Icon = Icons.ToImageSource(Icons.Render(Icons.Logo, 64));
            // 自己画标题栏：顶部 58 像素可以拖动，右上角是自己的关闭按钮
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 58, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false
            });
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsView.xaml"))
                View = (FrameworkElement)XamlReader.Load(st);
            Content = View;
            Wire();
            SourceInitialized += delegate { Native.RoundCorners(new WindowInteropHelper(this).Handle); };
            Loaded += delegate
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                Log.W("启动设置窗口：缩放 " + Math.Round(dpi.DpiScaleX * 100) + "%，大小 " + Math.Round(ActualWidth) + "×" + Math.Round(ActualHeight));
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            Closing += delegate (object s, System.ComponentModel.CancelEventArgs e) { app.OnSettingsClosing(this, e); };
            Refresh();
        }

        T Find<T>(string name) where T : class
        {
            var o = View.FindName(name) as T;
            if (o == null) throw new InvalidOperationException("SettingsView.xaml 里缺少 " + name);
            return o;
        }

        void Wire()
        {
            var header = Find<Image>("HeaderIcon");
            header.Source = Icons.ToImageSource(Icons.Render(Icons.Combined, 96));
            RenderOptions.SetBitmapScalingMode(header, BitmapScalingMode.HighQuality);
            Find<Button>("CloseButton").Click += delegate { Close(); };

            // 三个应用放在一起：勾选框 + 图标 + 名字，右边「预览」
            var list = Find<StackPanel>("AppsPanel");
            var line = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF3));
            var previewStyle = (Style)View.FindResource("Preview");
            for (int i = 0; i < app.Profiles.Count; i++)
            {
                var p = app.Profiles[i];
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = line, Margin = new Thickness(12, 0, 12, 0) });
                var row = new Grid { Margin = new Thickness(10, 8, 6, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = new Image { Source = p.IconImage, Width = 26, Height = 26, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                content.Children.Add(icon);
                content.Children.Add(new TextBlock { Text = p.AppName, FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                var cb = new CheckBox { Content = content, VerticalAlignment = VerticalAlignment.Center };
                var pv = new Button { Content = "▶  预览", Style = previewStyle, VerticalAlignment = VerticalAlignment.Center, ToolTip = "在 " + p.AppName + " 当前的窗口上播放一次开屏动画" };
                cb.Click += delegate
                {
                    if (loading) return;
                    bool on = cb.IsChecked == true;
                    pv.IsEnabled = on;
                    app.Change(delegate (Settings s) { s.Of(p).Enabled = on; }, this);
                };
                pv.Click += delegate { app.PreviewIntro(p); };
                Grid.SetColumn(pv, 1);
                row.Children.Add(cb);
                row.Children.Add(pv);
                list.Children.Add(row);
                appBoxes[p] = cb; previewButtons[p] = pv;
            }

            introBox = Find<CheckBox>("IntroBox");
            loopBox = Find<CheckBox>("LoopBox");
            introBox.Click += delegate { if (!loading) app.Change(delegate (Settings s) { s.PlayIntro = introBox.IsChecked == true; }, this); };
            loopBox.Click += delegate { if (!loading) app.Change(delegate (Settings s) { s.LoopBackground = loopBox.IsChecked == true; }, this); };

            alphaSlider = Find<Slider>("AlphaSlider"); alphaBox = Find<TextBox>("AlphaBox");
            volSlider = Find<Slider>("VolSlider"); volBox = Find<TextBox>("VolBox");
            BindPercent(alphaSlider, alphaBox, 80, delegate (Settings s, double v) { s.ResidualAlpha = v; });
            BindPercent(volSlider, volBox, 100, delegate (Settings s, double v) { s.IntroVolume = v; });

            modeLight = Find<RadioButton>("ModeLight"); modeOff = Find<RadioButton>("ModeOff");
            modeThumb = Find<Border>("ModeThumb");
            modeThumb.RenderTransform = new TranslateTransform();   // 自己建一个，保证能做动画
            BindMode(modeLight, "light", 0); BindMode(modeOff, "off", 1);
            // 窗口换到缩放不同的屏幕、或第一次排好版时，滑块直接对准当前选项（不播动效）
            ((FrameworkElement)modeThumb.Parent).SizeChanged += delegate { if (modeIndex >= 0) MoveThumb(modeIndex, false); };

            closeTray = Find<RadioButton>("CloseTray"); closeExit = Find<RadioButton>("CloseExit");
            closeTray.Checked += delegate { if (!loading) app.Change(delegate (Settings s) { s.CloseAction = "tray"; }, this); };
            closeExit.Checked += delegate { if (!loading) app.Change(delegate (Settings s) { s.CloseAction = "exit"; }, this); };

            // 右下角「设定」：点开小菜单（关掉全部动画 / 打开设置文件夹 / 卸载），点外面自动收起
            moreButton = Find<Button>("MoreButton");
            morePopup = Find<System.Windows.Controls.Primitives.Popup>("MorePopup");
            morePopup.PlacementTarget = moreButton;
            morePopup.Opened += delegate
            {
                // 菜单右边和按钮右边对齐、底边贴着按钮上沿（菜单外圈留了 10 像素给阴影）
                var child = (FrameworkElement)morePopup.Child;
                morePopup.HorizontalOffset = moreButton.ActualWidth - child.ActualWidth + 10;
                morePopup.VerticalOffset = 6;
            };
            morePopup.Closed += delegate { morePopupClosedAt = DateTime.Now; };
            moreButton.Click += delegate
            {
                // 菜单开着时点按钮：点击先把菜单关了，这里就别再打开
                if ((DateTime.Now - morePopupClosedAt).TotalMilliseconds < 250) return;
                morePopup.IsOpen = !morePopup.IsOpen;
            };
            Find<Button>("StopAllButton").Click += delegate { morePopup.IsOpen = false; app.StopAll(); };
            Find<Button>("FolderButton").Click += delegate { morePopup.IsOpen = false; app.OpenConfigFolder(); };
            Find<Button>("UninstallButton").Click += delegate { morePopup.IsOpen = false; app.Uninstall(); };
        }

        /// 拉杆和数字框联动：拖拉杆改数字，直接输入数字改拉杆，都马上生效。数字框里也能用 ↑ ↓ 微调
        void BindPercent(Slider slider, TextBox box, int max, Action<Settings, double> apply)
        {
            slider.Minimum = 0; slider.Maximum = max;
            slider.ValueChanged += delegate
            {
                if (!typing) box.Text = ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture);
                if (!loading) app.Change(delegate (Settings s) { apply(s, Math.Round(slider.Value) / 100.0); }, this);
            };
            box.PreviewTextInput += delegate (object s, TextCompositionEventArgs e)
            {
                foreach (char ch in e.Text) if (ch < '0' || ch > '9') { e.Handled = true; break; }
            };
            DataObject.AddPastingHandler(box, delegate (object s, DataObjectPastingEventArgs e)
            {
                var t = e.DataObject.GetData(typeof(string)) as string;
                int v;
                if (t == null || !int.TryParse(t.Trim(), out v)) e.CancelCommand();
            });
            box.TextChanged += delegate
            {
                if (loading || !box.IsKeyboardFocusWithin) return;
                int v;
                if (!int.TryParse(box.Text, out v)) return;
                typing = true;
                slider.Value = Math.Max(0, Math.Min(max, v));
                typing = false;
            };
            box.LostKeyboardFocus += delegate { box.Text = ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture); };
            box.GotKeyboardFocus += delegate { box.SelectAll(); };
            box.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; }
                else if (e.Key == Key.Up) { slider.Value = Math.Min(max, Math.Round(slider.Value) + 1); box.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Down) { slider.Value = Math.Max(0, Math.Round(slider.Value) - 1); box.SelectAll(); e.Handled = true; }
            };
        }

        void BindMode(RadioButton rb, string mode, int index)
        {
            rb.Checked += delegate
            {
                if (!loading) app.Change(delegate (Settings s) { s.ContrastMode = mode; }, this);
                try { MoveThumb(index, !loading); }
                catch (Exception ex) { Log.W("背景适配滑块动画出错：" + ex.Message); }
            };
        }

        /// 白色滑块滑到第 index 个选项下面（0.25 秒，先快后慢）
        void MoveThumb(int index, bool animate)
        {
            modeIndex = index;
            var host = (FrameworkElement)modeThumb.Parent;
            double to = index * host.ActualWidth / 2;   // 两个选项：浅色 / 原样
            var tt = (TranslateTransform)modeThumb.RenderTransform;
            if (!animate || host.ActualWidth <= 0)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.X = to;
                return;
            }
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            tt.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        static void SetPercent(Slider slider, TextBox box, double v)
        {
            slider.Value = Math.Round(v * 100);
            box.Text = ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture);
        }

        public void Refresh()
        {
            loading = true;
            var s = app.Settings;
            foreach (var p in app.Profiles)
            {
                bool on = s.Of(p).Enabled;
                appBoxes[p].IsChecked = on;
                previewButtons[p].IsEnabled = on;
            }
            introBox.IsChecked = s.PlayIntro;
            loopBox.IsChecked = s.LoopBackground;
            SetPercent(alphaSlider, alphaBox, s.ResidualAlpha);
            SetPercent(volSlider, volBox, s.IntroVolume);
            modeLight.IsChecked = s.ContrastMode == "light";
            modeOff.IsChecked = s.ContrastMode == "off";
            MoveThumb(s.ContrastMode == "off" ? 1 : 0, false);
            closeTray.IsChecked = s.CloseAction != "exit";
            closeExit.IsChecked = s.CloseAction == "exit";
            loading = false;
        }
    }

    // ───────────────────────── 主程序 ─────────────────────────

    public class App : Application
    {
        public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static string AssetsRoot = Path.Combine(BaseDir, "assets");
        public readonly List<Profile> Profiles = Profile.All();
        public Settings Settings;
        WF.NotifyIcon tray;
        TrayMenu trayMenu;
        SettingsWindow win;
        DispatcherTimer watch;
        readonly string[] args;
        bool testMode, exiting;
        Profile testProfile;

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

        Profile FindProfile(string key)
        {
            if (key == null) return null;
            foreach (var p in Profiles) if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Show.ForceLegacyLoop = Has("--legacy-loop");
            Settings = Settings.Load(Profiles);
            foreach (var p in Profiles)
            {
                p.IconBitmap = Icons.Render(p.DrawIcon, 32);
                p.IconImage = Icons.ToImageSource(Icons.Render(p.DrawIcon, 96));
            }
            LoadFont();
            Finder.TestTitle = Arg("--target-title");
            testMode = Finder.TestTitle != null;
            testProfile = FindProfile(Arg("--profile")) ?? Profiles[0];
            Dev.LoopCutSec = ArgD("--loop-cut", Dev.LoopCutSec);
            Dev.StallTestSec = ArgD("--stall-test", Dev.StallTestSec);
            Dev.RecycleEvery = (int)ArgD("--recycle-every", Dev.RecycleEvery);
            Dev.HeartbeatSec = ArgD("--heartbeat", Dev.HeartbeatSec);
            bool noScale;
            AppContext.TryGetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", out noScale);
            Log.W("启动 " + string.Join(" ", args) + "  程序目录 " + BaseDir + "  DPI 模式：" + Native.DpiModeName() + "  WPF 按屏幕缩放：" + (noScale ? "关" : "开"));

            if (Arg("--render-subtitle") != null) { RenderSubtitleTest(); return; }
            if (Arg("--render-icons") != null) { RenderIcons(Arg("--render-icons")); return; }
            if (Arg("--render-settings") != null) { RenderSettings(Arg("--render-settings")); return; }
            if (Arg("--render-contrast") != null) { RenderContrast(); return; }
            if (Arg("--render-ico") != null) { RenderIco(Arg("--render-ico")); return; }
            if (Arg("--render-traymenu") != null) { RenderElement(new TrayMenu(this), Arg("--render-traymenu")); return; }

            SetupTray();
            if (Has("--settings")) OpenSettings();

            // 本程序启动时应用已经开着：不补播。
            // 不能只看「看得见的窗口」——应用当时要是最小化或缩在托盘里，窗口找不到，之后一还原就会被当成新启动、
            // 又播一遍开机动画。所以把这几个应用现有的所有进程都记下来，只有真正新开的进程才播
            if (!testMode)
                foreach (var p in Profiles)
                {
                    foreach (var name in p.ProcessNames)
                        foreach (var proc in Process.GetProcessesByName(name))
                        {
                            p.SeenPids.Add((uint)proc.Id);
                            proc.Dispose();
                        }
                    var t0 = Finder.Find(p);
                    if (t0.Valid) p.SeenPids.Add(t0.Pid);
                    if (p.SeenPids.Count > 0) Log.W(p.AppName + " 已经在运行（" + p.SeenPids.Count + " 个进程），不补播；之后最小化、还原也不会重播");
                }

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
                string fonts = Path.Combine(AssetsRoot, "fonts");
                if (File.Exists(Path.Combine(fonts, "LibreBodoni-Italic.ttf")))
                    SubtitleElement.Font = new FontFamily(new Uri(fonts.TrimEnd('\\') + "\\"), "./#Libre Bodoni");
            }
            catch (Exception ex) { Log.W("字体载入失败，用 Georgia：" + ex.Message); }
        }

        void ReloadSettings() { Settings = Settings.Load(Profiles); }

        // 每 0.4 秒看一眼：三个应用有没有新开（新的进程号 = 新启动）
        void Watch()
        {
            foreach (var p in Profiles)
            {
                var t = Finder.Find(p);
                if (!t.Valid || p.SeenPids.Contains(t.Pid)) continue;
                p.SeenPids.Add(t.Pid);
                if (testMode) continue;
                ReloadSettings();
                bool on = Settings.Of(p).Enabled;
                Log.W("检测到 " + p.AppName + " 启动 pid=" + t.Pid + "，启用=" + on + " 开屏=" + Settings.PlayIntro + " 循环背景=" + Settings.LoopBackground);
                if (!on || (!Settings.PlayIntro && !Settings.LoopBackground)) continue;
                Start(p, t, Settings.PlayIntro);
            }
        }

        void Start(Profile p, Target t, bool withIntro)
        {
            if (p.Current != null) p.Current.Teardown();
            Show s = null;
            s = new Show(p, t, Settings.ConfigFor(p), withIntro, delegate { if (p.Current == s) p.Current = null; });
            p.Current = s;
        }

        bool StartOnCurrent(Profile p, bool withIntro)
        {
            var t = Finder.Find(p);
            if (!t.Valid)
            {
                if (tray != null) tray.ShowBalloonTip(3000, "启动设置", "没找到 " + p.AppName + " 窗口，先把 " + p.AppName + " 打开吧。", WF.ToolTipIcon.Info);
                return false;
            }
            p.SeenPids.Add(t.Pid);
            Start(p, t, withIntro);
            return true;
        }

        public void PreviewIntro(Profile p) { StartOnCurrent(p, true); }
        public void PreviewLoop(Profile p) { StartOnCurrent(p, false); }
        public void StopShow(Profile p) { if (p.Current != null) p.Current.Finish(true); }
        public void StopAll() { foreach (var p in Profiles) StopShow(p); }

        public void OpenConfigFolder()
        {
            Settings.Save();
            try { Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); } catch { }
        }

        /// 改设置：存盘、马上作用到正在播放的动画、刷新设置窗口（from 是发起修改的那个，不用刷它自己）
        public void Change(Action<Settings> change, object from)
        {
            change(Settings);
            Settings.Save();
            ApplyToShows();
            if (win != null && from != win) win.Refresh();
        }

        void ApplyToShows()
        {
            foreach (var p in Profiles)
            {
                if (p.Current == null) continue;
                if (!Settings.Of(p).Enabled) { p.Current.Finish(false); continue; }   // 取消勾选：正在放的那一场淡出
                p.Current.Apply(Settings.ConfigFor(p));
            }
        }

        public void OpenSettings()
        {
            ReloadSettings();
            if (win == null)
            {
                win = new SettingsWindow(this);
                win.Closed += delegate { win = null; };
            }
            else win.Refresh();
            win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
        }

        /// 点「启动设置」的关闭按钮：按设置缩到托盘，或者完全退出
        public void OnSettingsClosing(SettingsWindow w, System.ComponentModel.CancelEventArgs e)
        {
            if (w != win || exiting) return;
            if (Settings.CloseAction == "exit")
            {
                Log.W("关闭启动设置：完全退出");
                Dispatcher.BeginInvoke(new Action(Quit));
                return;
            }
            if (!Settings.TrayTipShown && tray != null)
            {
                Settings.TrayTipShown = true;
                Settings.Save();
                tray.ShowBalloonTip(5000, "启动设置", "已缩小到右下角托盘，开机动画继续在后台运行。\n想完全退出：托盘图标右键 →「退出」。", WF.ToolTipIcon.Info);
            }
        }

        public void Uninstall()
        {
            var r = MessageBox.Show(
                "会停掉三个开机动画，删除开机自启、设置和日志。Claude、ChatGPT、Antigravity 本身都没被改过，卸载后就是原装的样子。\n\n" +
                "项目文件夹里的程序、源码和动画素材会保留（" + BaseDir.TrimEnd('\\') + "），不需要的话可以手动删掉。",
                "卸载开机动画？", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
            foreach (var p in Profiles) if (p.Current != null) p.Current.Teardown();
            string script = Path.Combine(BaseDir, "uninstall.ps1");
            if (File.Exists(script))
            {
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -Quiet")
                { UseShellExecute = false, CreateNoWindow = true });
            }
            Quit();
        }

        public void Quit()
        {
            if (exiting) return;
            exiting = true;
            foreach (var p in Profiles) if (p.Current != null) p.Current.Teardown();
            if (win != null) { try { win.Close(); } catch { } }
            if (trayMenu != null) { try { trayMenu.Close(); } catch { } }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Log.W("退出");
            Shutdown();
        }

        // ── 托盘：一个图标。左键打开「启动设置」，右键是一个简洁的小菜单（启动设置 / 退出）──
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Icon = SD.Icon.FromHandle(Icons.Render(Icons.Combined, 32).GetHicon()), Text = "开机动画 · Claude / ChatGPT / Antigravity", Visible = true };
            tray.MouseUp += delegate (object s, WF.MouseEventArgs ev)
            {
                if (ev.Button == WF.MouseButtons.Left) OpenSettings();
                else if (ev.Button == WF.MouseButtons.Right) ShowTrayMenu();
            };
        }

        void ShowTrayMenu()
        {
            if (trayMenu != null) trayMenu.Close();
            trayMenu = new TrayMenu(this);
            var m = trayMenu;
            m.Closed += delegate { if (trayMenu == m) trayMenu = null; };
            m.ShowAtCursor();
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

        // ── 测试：--target-title 窗口标题 [--profile claude|gpt|antigravity]，用指定应用的素材盖在那个窗口上 ──
        void RunTest()
        {
            var p = testProfile;
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
                    if (StartOnCurrent(p, mode != "loop")) { started = true; showStart = DateTime.Now; }
                    else if ((DateTime.Now - startAt).TotalSeconds > 15) { Log.W("测试：找不到目标窗口"); Quit(); }
                    return;
                }
                double el = (DateTime.Now - showStart).TotalSeconds;
                var show = p.Current;
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
                var c = Settings.ConfigFor(testProfile);
                string bg = Arg("--render-subtitle");
                string outPath = args[Array.IndexOf(args, "--render-subtitle") + 2];
                int idx = args.Length > Array.IndexOf(args, "--render-subtitle") + 3 ? int.Parse(args[Array.IndexOf(args, "--render-subtitle") + 3]) : 1;
                var img = new BitmapImage(new Uri(Path.GetFullPath(bg)));
                int W = img.PixelWidth, H = img.PixelHeight;
                var g = new Grid { Width = W, Height = H };
                g.Children.Add(new Image { Source = img, Stretch = Stretch.Fill });
                var s = new SubtitleElement { WidthRatio = c.SubtitleWidthRatio, Opacity = 1 };
                s.RefText = c.Subtitles[0].Text;
                s.SetText(c.Subtitles[Math.Max(0, Math.Min(c.Subtitles.Count - 1, idx - 1))].Text);
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

        /// 开发用：生成程序图标 src\app.ico（--render-ico 输出.ico），build.ps1 再把它编进 exe
        void RenderIco(string path)
        {
            try
            {
                Icons.WriteIco(path, Icons.Logo, new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 });
                Log.W("程序图标存到 " + path);
            }
            catch (Exception ex) { Log.W("程序图标生成失败：" + ex); }
            Shutdown();
        }

        /// 开发用：不弹窗，把一个窗口的内容画成 2 倍大小的 PNG（比如 --render-traymenu 输出.png）
        void RenderElement(Window w, string path)
        {
            try
            {
                var v = (FrameworkElement)w.Content;
                w.Content = null;
                v.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                v.Arrange(new Rect(0, 0, v.DesiredSize.Width, v.DesiredSize.Height));
                v.UpdateLayout();
                const int k = 2;
                var rtb = new RenderTargetBitmap((int)Math.Ceiling(v.DesiredSize.Width * k), (int)Math.Ceiling(v.DesiredSize.Height * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(path)) enc.Save(f);
                Log.W("渲染到 " + path);
            }
            catch (Exception ex) { Log.W("渲染失败：" + ex); }
            Shutdown();
        }

        /// 开发用：把托盘图标和三个小图标存成 PNG（--render-icons 目录）
        void RenderIcons(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                using (var b = Icons.Render(Icons.Combined, 32)) b.Save(Path.Combine(dir, "tray.png"), SD.Imaging.ImageFormat.Png);
                using (var b = Icons.Render(Icons.Logo, 256)) b.Save(Path.Combine(dir, "logo-256.png"), SD.Imaging.ImageFormat.Png);
                using (var b = Icons.Render(Icons.Logo, 32)) b.Save(Path.Combine(dir, "logo-32.png"), SD.Imaging.ImageFormat.Png);
                using (var b = Icons.Render(Icons.Logo, 16)) b.Save(Path.Combine(dir, "logo-16.png"), SD.Imaging.ImageFormat.Png);
                foreach (var p in Profiles) p.IconBitmap.Save(Path.Combine(dir, p.Key + ".png"), SD.Imaging.ImageFormat.Png);
                Log.W("图标存到 " + dir);
            }
            catch (Exception ex) { Log.W("图标渲染失败：" + ex); }
            Shutdown();
        }

        /// 开发用：不弹窗，看背景适配的效果（--render-contrast 循环帧.png 输出.png [不透明度] [--profile 哪段动画的去背景阈值]）。
        /// 两行：白底黑字 / 黑底白字；三列：原样 / 浅色 / 深色。和浮层一样，先过着色器再乘整体不透明度
        void RenderContrast()
        {
            try
            {
                int i = Array.IndexOf(args, "--render-contrast");
                string framePath = args[i + 1], outPath = args[i + 2];
                double alpha = args.Length > i + 3 ? double.Parse(args[i + 3], CultureInfo.InvariantCulture) : Settings.ResidualAlpha;
                var frame = new BitmapImage();
                frame.BeginInit(); frame.CacheOption = BitmapCacheOption.OnLoad; frame.UriSource = new Uri(Path.GetFullPath(framePath)); frame.EndInit();
                const int W = 640, H = 360, gap = 12;
                string sample = "帮我把这段代码重构一下，顺便加上单元测试。\n好的，我先看一下现有的结构，再把重复的逻辑抽成函数。\n\nfunction render(items) {\n    return items.map(i => format(i)).join('\\n');\n}\n\nThe quick brown fox jumps over the lazy dog. 0123456789";
                string[] modes = { "原样", "浅色", "深色" };
                var canvas = new Canvas { Width = W * 3 + gap * 4, Height = H * 2 + gap * 3, Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)) };
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        bool darkUi = row == 1;
                        var cell = new Grid { Width = W, Height = H, ClipToBounds = true, Background = darkUi ? new SolidColorBrush(Color.FromRgb(24, 24, 27)) : Brushes.White };
                        cell.Children.Add(new TextBlock
                        {
                            Text = sample, Margin = new Thickness(22), FontSize = 15, TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Microsoft YaHei UI"),
                            Foreground = darkUi ? new SolidColorBrush(Color.FromRgb(232, 232, 232)) : new SolidColorBrush(Color.FromRgb(24, 24, 24))
                        });
                        var layer = new Grid { Opacity = alpha };
                        layer.Children.Add(new Image { Source = frame, Stretch = Stretch.UniformToFill });
                        if (col > 0 && ContrastEffect.Shader != null)
                            layer.Effect = new ContrastEffect { Mix = 1, Light = col == 1 ? 1 : 0, KeyLo = testProfile.KeyLo, KeyHi = testProfile.KeyHi };
                        cell.Children.Add(layer);
                        cell.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(200, 99, 102, 241)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3),
                            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10),
                            Child = new TextBlock { Text = modes[col] + "  " + Math.Round(alpha * 100) + "%", Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Microsoft YaHei UI") }
                        });
                        Canvas.SetLeft(cell, gap + col * (W + gap)); Canvas.SetTop(cell, gap + row * (H + gap));
                        canvas.Children.Add(cell);
                    }
                canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
                var rtb = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(canvas);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(outPath)) enc.Save(f);
                Log.W("对比度效果渲染到 " + outPath);
            }
            catch (Exception ex) { Log.W("对比度效果渲染失败：" + ex); }
            Shutdown();
        }

        /// 开发用：不弹窗，把「启动设置」画成一张 2 倍大小的 PNG（--render-settings 文件），看排版
        void RenderSettings(string path)
        {
            try
            {
                var w = new SettingsWindow(this);
                var v = w.View;
                w.Content = null;
                double width = w.Width;
                v.Measure(new Size(width, double.PositiveInfinity));
                v.Arrange(new Rect(0, 0, width, v.DesiredSize.Height));
                v.UpdateLayout();
                const int k = 2;
                var rtb = new RenderTargetBitmap((int)Math.Ceiling(width * k), (int)Math.Ceiling(v.DesiredSize.Height * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(path)) enc.Save(f);
                Log.W("启动设置渲染到 " + path);
            }
            catch (Exception ex) { Log.W("启动设置渲染失败：" + ex); }
            Shutdown();
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.EnablePerMonitorDpi();   // 清单里已声明 PerMonitorV2，这里是保险
            GpuPref.Ensure();               // 要在 WPF 建立显卡渲染之前设好
            bool test = Array.IndexOf(args, "--target-title") >= 0 || Array.IndexOf(args, "--render-subtitle") >= 0
                || Array.IndexOf(args, "--render-icons") >= 0 || Array.IndexOf(args, "--render-settings") >= 0
                || Array.IndexOf(args, "--render-contrast") >= 0 || Array.IndexOf(args, "--render-ico") >= 0
                || Array.IndexOf(args, "--render-traymenu") >= 0;
            Mutex mutex = null;
            EventWaitHandle showEvent = null;
            if (!test)
            {
                bool created;
                mutex = new Mutex(true, @"Local\AIBootAnimations.merged", out created);
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIBootAnimations.merged.show");
                if (!created)
                {
                    // 已经在运行：再打开一次 = 弹出「启动设置」
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

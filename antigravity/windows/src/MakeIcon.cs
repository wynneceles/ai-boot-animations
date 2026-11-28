using System;
using System.IO;
using SD = System.Drawing;

namespace AntigravityBootAnimation
{
    public class IconGenerator
    {
        public static SD.Bitmap GetBitmap()
        {
            // 原本的Icon位图生成代码
            var bmp = new SD.Bitmap(32, 32);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                // 一颗弧边四芒星（像 Gemini 标志），蓝→紫渐变，和 Claude 的金色 ✨、GPT 的月牙区分开
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
            return bmp;
        }

        // 直接将位图封装为 ICO；位图由调用方释放。
        private static void Save(SD.Bitmap bitmap, string path)
        {
            using (var png = new MemoryStream())
            {
                bitmap.Save(png, SD.Imaging.ImageFormat.Png);
                byte[] data = png.ToArray();
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)1);
                    writer.Write((byte)bitmap.Width);
                    writer.Write((byte)bitmap.Height);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)data.Length);
                    writer.Write((uint)22);
                    writer.Write(data);
                }
            }
        }

        // 脚本调用时传一个参数，生成路径
        static int Main(string[] args)
        {
            using (var bmp = GetBitmap())
            {
                if (args.Length < 1) {
                    return 1;
                }
                Save(bmp, args[0]);
            }
            return 0;
        }
    }
}

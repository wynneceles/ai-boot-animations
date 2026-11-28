using System;
using System.IO;
using SD = System.Drawing;

namespace ClaudeBootAnimation
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
                Action<float, float, float> star = delegate (float cx, float cy, float r)
                {
                    float k = r * 0.28f;
                    var pts = new[] {
                        new SD.PointF(cx, cy - r), new SD.PointF(cx + k, cy - k), new SD.PointF(cx + r, cy), new SD.PointF(cx + k, cy + k),
                        new SD.PointF(cx, cy + r), new SD.PointF(cx - k, cy + k), new SD.PointF(cx - r, cy), new SD.PointF(cx - k, cy - k) };
                    using (var b = new SD.SolidBrush(SD.Color.FromArgb(255, 255, 196, 64))) g.FillPolygon(b, pts);
                };
                star(13, 18, 12); star(25, 7, 6); star(26, 25, 4);
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

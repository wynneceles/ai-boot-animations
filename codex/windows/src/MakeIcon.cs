using System;
using System.IO;
using SD = System.Drawing;

namespace GPTBootAnimation
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
                    using (var b = new SD.SolidBrush(SD.Color.FromArgb(255, 190, 210, 255))) g.FillPolygon(b, pts);
                };
                // 月牙 + 两颗小星（和 Claude 版的金色 ✨ 区分开）
                using (var moon = new SD.Drawing2D.GraphicsPath())
                {
                    moon.AddEllipse(2, 4, 24, 24);
                    using (var cut = new SD.Drawing2D.GraphicsPath())
                    {
                        cut.AddEllipse(10, 1, 22, 22);
                        var region = new SD.Region(moon); region.Exclude(cut);
                        using (var b = new SD.SolidBrush(SD.Color.FromArgb(255, 222, 230, 255))) g.FillRegion(b, region);
                        region.Dispose();
                    }
                }
                star(25, 8, 5); star(27, 22, 3.5f);
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

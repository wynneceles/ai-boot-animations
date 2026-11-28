using System;
using System.IO;
using SD = System.Drawing;

namespace DeepSeekBootAnimation
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
                // 一条小鱼（大肥鱼，和 Mac 版菜单栏的 fish 图标对应），深海蓝→青渐变（DeepSeek 鲸鱼蓝），眼睛镂空
                using (var path = new SD.Drawing2D.GraphicsPath(SD.Drawing2D.FillMode.Alternate))
                {
                    path.StartFigure();
                    path.AddBezier(3f, 16f, 8f, 6f, 18f, 6f, 23f, 15f);          // 背
                    path.AddLines(new[] { new SD.PointF(23f, 15f), new SD.PointF(30f, 8f), new SD.PointF(27f, 16f),
                                          new SD.PointF(30f, 24f), new SD.PointF(23f, 17f) });   // 鱼尾
                    path.AddBezier(23f, 17f, 18f, 26f, 8f, 26f, 3f, 16f);        // 肚子
                    path.CloseFigure();
                    path.AddEllipse(7.2f, 12.7f, 3.6f, 3.6f);                   // 眼睛（交替填充 = 镂空）
                    using (var b = new SD.Drawing2D.LinearGradientBrush(new SD.PointF(2, 30), new SD.PointF(30, 2),
                        SD.Color.FromArgb(255, 77, 107, 254), SD.Color.FromArgb(255, 90, 210, 255)))
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

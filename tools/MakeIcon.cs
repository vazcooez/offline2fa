using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the app icon (blue rounded square with a white padlock) into a multi-size .ico file.
static class MakeIcon
{
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: MakeIcon <output.ico>");
            return 2;
        }

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int size in sizes)
            using (Bitmap bmp = Render(size))
                images.Add(size >= 256 ? Png(bmp) : Dib(bmp));

        using (var stream = File.Create(args[0]))
        using (var w = new BinaryWriter(stream))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim);
                w.Write(dim);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) w.Write(image);
        }
        return 0;
    }

    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 256f, size / 256f);

            using (GraphicsPath tile = RoundRect(8, 8, 240, 240, 56))
            using (var brush = new LinearGradientBrush(new Point(0, 8), new Point(0, 248), Color.FromArgb(59, 130, 246), Color.FromArgb(29, 78, 216)))
                g.FillPath(brush, tile);

            using (var pen = new Pen(Color.White, 24f))
            {
                g.DrawArc(pen, 84, 48, 88, 88, 180, 180);
                g.DrawLine(pen, 84, 91, 84, 124);
                g.DrawLine(pen, 172, 91, 172, 124);
            }
            using (GraphicsPath body = RoundRect(60, 112, 136, 104, 18))
                g.FillPath(Brushes.White, body);
            using (var hole = new SolidBrush(Color.FromArgb(37, 99, 235)))
            {
                g.FillEllipse(hole, 113, 142, 30, 30);
                g.FillRectangle(hole, 121, 160, 14, 32);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        float d = r * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // Classic icon image: BITMAPINFOHEADER + bottom-up BGRA pixels + empty AND mask.
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40);
            w.Write(s);
            w.Write(s * 2);
            w.Write((short)1);
            w.Write((short)32);
            for (int i = 0; i < 6; i++) w.Write(0);
            for (int y = s - 1; y >= 0; y--)
            {
                for (int x = 0; x < s; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    w.Write(c.B);
                    w.Write(c.G);
                    w.Write(c.R);
                    w.Write(c.A);
                }
            }
            var maskRow = new byte[((s + 31) / 32) * 4];
            for (int y = 0; y < s; y++) w.Write(maskRow);
            w.Flush();
            return ms.ToArray();
        }
    }
}

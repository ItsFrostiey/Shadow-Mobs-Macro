using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StaminaDonator
{
    // A captured image as raw ARGB pixels, which is much faster to scan than Bitmap.GetPixel.
    public class Frame
    {
        public int W, H;
        public int[] P;

        public static Frame FromBitmap(Bitmap source)
        {
            Frame f = new Frame();
            f.W = source.Width;
            f.H = source.Height;
            f.P = new int[f.W * f.H];
            Rectangle r = new Rectangle(0, 0, f.W, f.H);
            using (Bitmap copy = source.Clone(r, PixelFormat.Format32bppArgb))
            {
                BitmapData d = copy.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < f.H; y++)
                    Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), f.P, y * f.W, f.W);
                copy.UnlockBits(d);
            }
            return f;
        }

        public static Frame Load(string path)
        {
            using (Bitmap b = new Bitmap(path)) return FromBitmap(b);
        }

        public void Save(string path)
        {
            using (Bitmap b = ToBitmap()) b.Save(path, ImageFormat.Png);
        }

        public Bitmap ToBitmap()
        {
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            Rectangle r = new Rectangle(0, 0, W, H);
            BitmapData d = b.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < H; y++)
                Marshal.Copy(P, y * W, IntPtr.Add(d.Scan0, y * d.Stride), W);
            b.UnlockBits(d);
            return b;
        }

        public Frame Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, W, H));
            Frame f = new Frame();
            f.W = Math.Max(0, r.Width);
            f.H = Math.Max(0, r.Height);
            f.P = new int[f.W * f.H];
            for (int y = 0; y < f.H; y++)
                Array.Copy(P, (r.Y + y) * W + r.X, f.P, y * f.W, f.W);
            return f;
        }

        // Used when a calibration from one screen size is borrowed for another.
        public Frame Resize(int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            using (Bitmap src = ToBitmap())
            using (Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);
                }
                return FromBitmap(dst);
            }
        }

        public int Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return 0;
            return P[y * W + x];
        }
    }

    public static class Px
    {
        public const int Tolerance = 14;

        public static bool Near(int pixel, int rgb) { return Near(pixel, rgb, Tolerance); }

        public static bool Near(int pixel, int rgb, int tolerance)
        {
            return Math.Abs(((pixel >> 16) & 255) - ((rgb >> 16) & 255)) <= tolerance
                && Math.Abs(((pixel >> 8) & 255) - ((rgb >> 8) & 255)) <= tolerance
                && Math.Abs((pixel & 255) - (rgb & 255)) <= tolerance;
        }

        // The most common colour in a box, such as a button's fill.
        public static int Modal(Frame f, Rectangle r)
        {
            System.Collections.Generic.Dictionary<int, int> counts = new System.Collections.Generic.Dictionary<int, int>();
            int best = 0, bestCount = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int c = f.Get(x, y) & 0xFFFFFF, n;
                    counts.TryGetValue(c, out n);
                    counts[c] = ++n;
                    if (n > bestCount) { bestCount = n; best = c; }
                }
            return best;
        }
    }

    // Image matching: a small reference picture compared against the screen.
    public static class Templates
    {
        public const double Limit = 16;

        // Mean per-channel difference at this position. Gives up once it can't beat "limit".
        public static double Score(Frame t, Frame f, int x, int y, double limit)
        {
            if (t == null || x < 0 || y < 0 || x + t.W > f.W || y + t.H > f.H) return 999;
            int total = (t.W + 1) / 2 * ((t.H + 1) / 2) * 3;
            long budget = (long)(limit * total) + 1;
            long sum = 0;
            for (int ty = 0; ty < t.H; ty += 2)
            {
                int fi = (y + ty) * f.W + x;
                int ti = ty * t.W;
                for (int tx = 0; tx < t.W; tx += 2)
                {
                    int a = f.P[fi + tx], b = t.P[ti + tx];
                    sum += Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255))
                         + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255))
                         + Math.Abs((a & 255) - (b & 255));
                    if (sum > budget) return 999;
                }
            }
            return (double)sum / total;
        }

        public static bool Matches(Frame t, Frame f, int x, int y)
        {
            return Score(t, f, x, y, Limit) < Limit;
        }

        // Best match within rx/ry pixels of (x, y). dx/dy is how far it had moved.
        public static bool Search(Frame t, Frame f, int x, int y, int rx, int ry, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            double best = Limit;
            bool found = false;
            for (int oy = -ry; oy <= ry; oy++)
                for (int ox = -rx; ox <= rx; ox++)
                {
                    double score = Score(t, f, x + ox, y + oy, best);
                    if (score < best) { best = score; dx = ox; dy = oy; found = true; }
                }
            return found;
        }
    }
}

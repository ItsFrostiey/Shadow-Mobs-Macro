using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    public class CalibrationException : Exception
    {
        public CalibrationException(string message) : base(message) { }
    }

    // Turns the rough box a user drags during calibration into an exact one. Each box only has to
    // be close: these tighten it onto the text, button or bar underneath.
    public static class Snap
    {
        public static Rectangle Check(Frame f, Rectangle raw)
        {
            raw.Intersect(new Rectangle(0, 0, f.W, f.H));
            if (raw.Width < 3 || raw.Height < 3) throw new CalibrationException("That box is too small. Drag a bigger one.");
            return raw;
        }

        // Shrinks a box to the content that differs from its top-left corner colour.
        public static Rectangle Tighten(Frame f, Rectangle r)
        {
            r = Check(f, r);
            int bg = f.Get(r.X, r.Y);
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (!Px.Near(f.Get(x, y), bg, 12))
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
            if (right < 0) throw new CalibrationException("Nothing found inside that box. Try again around the target.");
            Rectangle tight = Rectangle.FromLTRB(left - 2, top - 2, right + 3, bottom + 3);
            tight.Intersect(new Rectangle(0, 0, f.W, f.H));
            return tight;
        }

        // The button's colour: the most common strongly coloured pixel in the box, so a box drawn
        // around the button (mostly dark card) finds the button just as well as one drawn inside it.
        static int ButtonColor(Frame f, Rectangle r)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            int best = -1, bestCount = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int c = f.Get(x, y);
                    int red = (c >> 16) & 255, green = (c >> 8) & 255, blue = c & 255;
                    int max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
                    if (max < 110 || max - min < 60) continue;   // dark or grey: background, text, borders
                    int key = c & 0xF0F0F0;                       // groups near-identical shades together
                    int n;
                    counts.TryGetValue(key, out n);
                    counts[key] = ++n;
                    if (n > bestCount) { bestCount = n; best = key; }
                }
            if (best < 0 || bestCount < 30) return Px.Modal(f, r);
            // The exact shade most used within that group.
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if ((f.Get(x, y) & 0xF0F0F0) == best) return MostCommonNear(f, r, f.Get(x, y));
            return Px.Modal(f, r);
        }

        static int MostCommonNear(Frame f, Rectangle r, int color)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            int best = color & 0xFFFFFF, bestCount = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int c = f.Get(x, y) & 0xFFFFFF, n;
                    if (!Px.Near(c, color, 24)) continue;
                    counts.TryGetValue(c, out n);
                    counts[c] = ++n;
                    if (n > bestCount) { bestCount = n; best = c; }
                }
            return best;
        }

        // Fills outward from the button's colour. The gap between two buttons stops it, so the
        // result is exactly one button even if the box strays onto the neighbour.
        public static Rectangle Button(Frame f, Rectangle r)
        {
            r = Check(f, r);
            int color = ButtonColor(f, r);
            // Fills every patch of that colour in the box and keeps the biggest. (Starting from one
            // pixel can land inside a letter, like the hole in an O, and find only that.) The fill
            // never spreads far beyond the box, even if something nearby shares the colour.
            Rectangle limit = Rectangle.Inflate(r, r.Width, r.Height);
            bool[] seen = new bool[f.W * f.H];
            Stack<int> stack = new Stack<int>();
            Rectangle button = Rectangle.Empty;
            int bestCount = 0;
            for (int sy = r.Top; sy < r.Bottom; sy++)
                for (int sx = r.Left; sx < r.Right; sx++)
                {
                    int seed = sy * f.W + sx;
                    if (seen[seed] || !Px.Near(f.P[seed], color, 20)) continue;
                    seen[seed] = true;
                    stack.Push(seed);
                    int left = sx, right = sx, top = sy, bottom = sy, count = 0;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        int x = i % f.W, y = i / f.W;
                        count++;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                        foreach (int n in new[] { i - 1, i + 1, i - f.W, i + f.W })
                        {
                            if (n < 0 || n >= seen.Length || seen[n]) continue;
                            if (Math.Abs((n % f.W) - x) > 1) continue;
                            if (!Px.Near(f.P[n], color, 20) || !limit.Contains(n % f.W, n / f.W)) continue;
                            seen[n] = true;
                            stack.Push(n);
                        }
                    }
                    if (count > bestCount) { bestCount = count; button = Rectangle.FromLTRB(left, top, right + 1, bottom + 1); }
                }
            if (bestCount == 0) throw new CalibrationException("Couldn't find a button there. Drag a box over the button.");
            if (button.Width < 20 || button.Height < 10 || button.Width > f.W / 3 || button.Height > f.H / 4)
                throw new CalibrationException(string.Format("Couldn't find a button there (found a {0}x{1} area). Drag a box over the button (around it or inside it both work).", button.Width, button.Height));
            return button;
        }

        public class BarInfo
        {
            public Rectangle Track;
            public int Row, Fill, Empty;
        }

        // A bar such as stamina: where it is, its fill colour, and the colour of its empty part.
        public static BarInfo Bar(Frame f, Rectangle r)
        {
            r = Check(f, r);
            int bg = f.Get(r.X, r.Y);
            int y = r.Top + r.Height / 2;
            int left = -1;
            for (int x = r.Left; x < r.Right - 4; x++)
            {
                int c = f.Get(x, y);
                if (Px.Near(c, bg, 12)) continue;
                if (Px.Near(f.Get(x + 1, y), c, 6) && Px.Near(f.Get(x + 2, y), c, 6) && Px.Near(f.Get(x + 3, y), c, 6))
                {
                    left = x;
                    break;
                }
            }
            if (left < 0) throw new CalibrationException("Couldn't find the bar. You need at least 1 stamina for this step.");
            BarInfo info = new BarInfo();
            info.Fill = f.Get(left + 1, y) & 0xFFFFFF;
            int top = y;
            while (top > 0 && Px.Near(f.Get(left + 1, top - 1), info.Fill, 6)) top--;
            int bottom = y;
            while (bottom < f.H - 1 && Px.Near(f.Get(left + 1, bottom + 1), info.Fill, 6)) bottom++;
            info.Row = top + 1;

            int end = left;
            while (end < f.W && Px.Near(f.Get(end, info.Row), info.Fill)) end++;

            int limit = Math.Min(f.W, r.Right + Math.Max(8, r.Width / 10));
            int track = TrackEnd(f, left, top, end, limit, bg);
            if (track <= end) track = EmptyEnd(f, info.Row, end, limit, bg);
            // Better a rough right edge from the user's box than a bar measured as only its fill,
            // which would read every value as far too high.
            if (track - left < r.Width * 3 / 5) track = r.Right;

            // A completely full bar has no empty part to sample, so assume the game's usual dark.
            int inside = f.Get(track - Math.Max(3, (track - left) / 60), info.Row);
            info.Empty = Px.Near(inside, info.Fill) ? 0x0D0D0E : inside & 0xFFFFFF;
            while (track > end && !Px.Near(f.Get(track - 1, info.Row), info.Empty, 8)) track--;

            info.Track = Rectangle.FromLTRB(left, top, track, bottom + 1);
            if (info.Track.Width < 40) throw new CalibrationException("That doesn't look like the bar. Try again.");
            return info;
        }

        // The bar's frame runs the full width above both its filled and empty parts.
        static int TrackEnd(Frame f, int left, int top, int fillEnd, int limit, int bg)
        {
            int best = 0;
            for (int above = 1; above <= 4; above++)
            {
                int y = top - above;
                if (y < 0) break;
                int c = f.Get(left + 1, y);
                if (Px.Near(c, bg, 4)) continue;
                int x = left + 1;
                while (x < f.W && Px.Near(f.Get(x, y), c, 8)) x++;
                if (x > fillEnd && x <= limit && x > best) best = x;
            }
            return best;
        }

        // Without a frame: skips blended pixels where the fill stops, then follows the empty colour.
        static int EmptyEnd(Frame f, int row, int fillEnd, int limit, int bg)
        {
            for (int start = fillEnd; start < fillEnd + 12 && start + 3 < limit; start++)
            {
                int c = f.Get(start, row);
                if (!Px.Near(f.Get(start + 1, row), c, 3) || !Px.Near(f.Get(start + 2, row), c, 3)) continue;
                if (Px.Near(c, bg, 4)) return fillEnd;   // straight onto the background: the bar is full
                int x = start;
                while (x < limit && Px.Near(f.Get(x, row), c, 3)) x++;
                return x;
            }
            return fillEnd;
        }

        public class UnderlineInfo
        {
            public int Y, X1, X2, X3, Color;
        }

        // A selected tab's underline: the most saturated long straight run in or just below the box.
        public static UnderlineInfo Underline(Frame f, Rectangle r)
        {
            r = Check(f, r);
            int bg = f.Get(r.X, r.Y);
            UnderlineInfo best = null;
            int bestSaturation = 60;
            int bottom = Math.Min(f.H, r.Bottom + 12);
            for (int y = r.Top; y < bottom; y++)
            {
                int x = r.Left;
                while (x < r.Right)
                {
                    int c = f.Get(x, y);
                    int end = x + 1;
                    while (end < r.Right && Px.Near(f.Get(end, y), c, 8)) end++;
                    int length = end - x;
                    if (length >= r.Width / 2 && !Px.Near(c, bg, 20))
                    {
                        int red = (c >> 16) & 255, green = (c >> 8) & 255, blue = c & 255;
                        int saturation = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
                        if (saturation > bestSaturation)
                        {
                            bestSaturation = saturation;
                            best = new UnderlineInfo();
                            best.Y = y;
                            best.X1 = x + length / 4;
                            best.X2 = x + length / 2;
                            best.X3 = x + length * 3 / 4;
                            best.Color = c & 0xFFFFFF;
                        }
                    }
                    x = end;
                }
            }
            if (best == null) throw new CalibrationException("Couldn't find the coloured line under the tab. Make sure that tab is the selected one.");
            return best;
        }

        // A perk's level bar has 25 small segments between its LEVEL text and its buttons. This
        // finds their row and the last segment, which lights up at level 25.
        public static bool LastSegment(Frame f, Rectangle level, Rectangle button, out int x, out int y)
        {
            x = 0;
            y = 0;
            int plain = button.Top - 3;   // the card's own background, just above the buttons
            int bottom = -1, top = -1;
            for (int row = plain - 1; row > level.Bottom; row--)
            {
                int different = 0;
                for (int px = button.Left; px < button.Right; px++)
                    if (!Px.Near(f.Get(px, row), f.Get(px, plain), 10)) different++;
                bool isBar = different > button.Width / 2;
                if (isBar && bottom < 0) bottom = row;
                if (!isBar && bottom >= 0) { top = row + 1; break; }
            }
            if (bottom < 0 || top < 0) return false;
            y = (top + bottom) / 2;

            int right = -1, gap = 0;
            for (int px = button.Left + button.Width / 2; px < f.W && gap < 12; px++)
            {
                if (Px.Near(f.Get(px, y), f.Get(px, plain), 10)) gap++;
                else { right = px; gap = 0; }
            }
            if (right < 0) return false;
            int left = right;
            while (left > 0 && !Px.Near(f.Get(left - 1, y), f.Get(left - 1, plain), 10)) left--;
            x = (left + right) / 2;
            return true;
        }
    }
}

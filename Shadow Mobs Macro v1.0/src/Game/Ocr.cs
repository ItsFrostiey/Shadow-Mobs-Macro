using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Threading.Tasks;

namespace StaminaDonator
{
    public class OcrWord
    {
        public string Text;
        public Rectangle Box;   // in frame pixels
    }

    public class OcrLine
    {
        public List<OcrWord> Words = new List<OcrWord>();

        public string Text
        {
            get
            {
                List<string> parts = new List<string>();
                foreach (OcrWord w in Words) parts.Add(w.Text);
                return string.Join(" ", parts);
            }
        }

        public Rectangle Box
        {
            get
            {
                Rectangle r = Words[0].Box;
                foreach (OcrWord w in Words) r = Rectangle.Union(r, w.Box);
                return r;
            }
        }
    }

    public interface ITextReader
    {
        List<OcrLine> Read(Frame f, Rectangle region, int enlarge);

        // For white text on a coloured background, such as numbers inside bars.
        List<OcrLine> ReadWhite(Frame f, Rectangle region, int enlarge);
    }

    // The text recognition built into Windows 10 and 11, so there's nothing extra to install.
    // Reading text instead of matching pictures keeps working when the game's theme changes.
    // It's reached by name at runtime, because compiling against it directly needs the Windows SDK.
    public class WindowsText : ITextReader
    {
        const string WinRt = ", Windows.Foundation, ContentType=WindowsRuntime";
        const string Interop = ", System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        readonly object engine;
        readonly Type bitmapType, formatType, alphaType, resultType;
        readonly MethodInfo asBuffer, asTask;

        public WindowsText()
        {
            Type engineType = Type.GetType("Windows.Media.Ocr.OcrEngine" + WinRt, true);
            resultType = Type.GetType("Windows.Media.Ocr.OcrResult" + WinRt, true);
            bitmapType = Type.GetType("Windows.Graphics.Imaging.SoftwareBitmap" + WinRt, true);
            formatType = Type.GetType("Windows.Graphics.Imaging.BitmapPixelFormat" + WinRt, true);
            alphaType = Type.GetType("Windows.Graphics.Imaging.BitmapAlphaMode" + WinRt, true);
            engine = engineType.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
            if (engine == null)
                throw new InvalidOperationException("Windows has no text recognition language installed. Add English under Settings > Time & language > Language.");

            Type buffers = Type.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions" + Interop, true);
            asBuffer = buffers.GetMethod("AsBuffer", new[] { typeof(byte[]) });
            Type tasks = Type.GetType("System.WindowsRuntimeSystemExtensions" + Interop, true);
            foreach (MethodInfo m in tasks.GetMethods())
            {
                if (m.Name != "AsTask" || !m.IsGenericMethodDefinition || m.GetGenericArguments().Length != 1) continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType.Name == "IAsyncOperation`1") asTask = m.MakeGenericMethod(resultType);
            }
            if (asTask == null) throw new InvalidOperationException("Couldn't start Windows text recognition.");
        }

        // Dark text on white, from whichever colour channel is brightest. The game's coloured and
        // low-contrast text reads far more accurately this way.
        static Frame HighContrast(Frame c, bool white)
        {
            int low = 255, high = 0;
            foreach (int p in c.P)
            {
                int m = Channel(p, white);
                if (m < low) low = m;
                if (m > high) high = m;
            }
            Frame r = new Frame();
            r.W = c.W;
            r.H = c.H;
            r.P = new int[c.P.Length];
            for (int i = 0; i < c.P.Length; i++)
            {
                int m = Channel(c.P[i], white);
                int v = 255 - (high > low ? (m - low) * 255 / (high - low) : 0);
                r.P[i] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
            }
            return r;
        }

        static int Channel(int p, bool white)
        {
            int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
            return white ? Math.Min(r, Math.Min(g, b)) : Math.Max(r, Math.Max(g, b));
        }

        static object Get(object o, string property)
        {
            return o.GetType().GetProperty(property).GetValue(o, null);
        }

        public List<OcrLine> Read(Frame f, Rectangle region, int enlarge) { return Recognise(f, region, enlarge, false); }
        public List<OcrLine> ReadWhite(Frame f, Rectangle region, int enlarge) { return Recognise(f, region, enlarge, true); }

        List<OcrLine> Recognise(Frame f, Rectangle region, int enlarge, bool white)
        {
            region.Intersect(new Rectangle(0, 0, f.W, f.H));
            List<OcrLine> lines = new List<OcrLine>();
            if (region.Width <= 0 || region.Height <= 0) return lines;

            Frame enlarged;
            using (Bitmap crop = HighContrast(f.Crop(region), white).ToBitmap())
            using (Bitmap big = new Bitmap(region.Width * enlarge, region.Height * enlarge))
            {
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(crop, 0, 0, big.Width, big.Height);
                }
                enlarged = Frame.FromBitmap(big);
            }

            byte[] bytes = new byte[enlarged.P.Length * 4];
            Buffer.BlockCopy(enlarged.P, 0, bytes, 0, bytes.Length);
            object bitmap = Activator.CreateInstance(bitmapType,
                Enum.ToObject(formatType, 87), enlarged.W, enlarged.H, Enum.ToObject(alphaType, 0));   // Bgra8, premultiplied
            try
            {
                bitmapType.GetMethod("CopyFromBuffer").Invoke(bitmap, new[] { asBuffer.Invoke(null, new object[] { bytes }) });
                object operation = engine.GetType().GetMethod("RecognizeAsync").Invoke(engine, new[] { bitmap });
                Task task = (Task)asTask.Invoke(null, new[] { operation });
                task.Wait();
                object result = Get(task, "Result");
                foreach (object source in (IEnumerable)Get(result, "Lines"))
                {
                    OcrLine line = new OcrLine();
                    foreach (object word in (IEnumerable)Get(source, "Words"))
                    {
                        object r = Get(word, "BoundingRect");
                        OcrWord w = new OcrWord();
                        w.Text = (string)Get(word, "Text");
                        w.Box = new Rectangle(
                            region.X + (int)((double)Get(r, "X") / enlarge), region.Y + (int)((double)Get(r, "Y") / enlarge),
                            Math.Max(1, (int)((double)Get(r, "Width") / enlarge)), Math.Max(1, (int)((double)Get(r, "Height") / enlarge)));
                        line.Words.Add(w);
                    }
                    if (line.Words.Count > 0) lines.Add(line);
                }
            }
            finally
            {
                IDisposable d = bitmap as IDisposable;
                if (d != null) d.Dispose();
            }
            return lines;
        }
    }

    // Numbers read by OCR, which often mistakes digits for look-alike letters.
    public static class Numbers
    {
        public static string Fix(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c == 'O' || c == 'o' || c == 'D') b.Append('0');
                else if (c == 'I' || c == 'l' || c == '|' || c == 'i') b.Append('1');
                else if (c == 'S' || c == 's') b.Append('5');
                else if (c == 'B') b.Append('8');
                else b.Append(c);
            }
            return b.ToString();
        }

        static IEnumerable<string> Attempts(ITextReader reader, Frame f, Rectangle area)
        {
            foreach (int k in new[] { 3, 4, 2 })
                foreach (bool white in new[] { true, false })
                {
                    string text = "";
                    foreach (OcrLine l in white ? reader.ReadWhite(f, area, k) : reader.Read(f, area, k)) text += " " + l.Text;
                    yield return text;
                }
        }

        // The first whole number in the area, or -1.
        public static int ReadInt(ITextReader reader, Frame f, Rectangle area)
        {
            foreach (string text in Attempts(reader, f, area))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Fix(text), @"\d+");
                int n;
                if (m.Success && int.TryParse(m.Value, out n)) return n;
            }
            return -1;
        }

        // The first whole number from every way of reading the area (for voting on the answer).
        public static List<int> ReadInts(ITextReader reader, Frame f, Rectangle area)
        {
            List<int> all = new List<int>();
            foreach (string text in Attempts(reader, f, area))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Fix(text), @"\d+");
                int n;
                if (m.Success && m.Value.Length <= 6 && int.TryParse(m.Value, out n)) all.Add(n);
            }
            return all;
        }

        // "30 / 90" style text: current and maximum.
        public static bool ReadPair(ITextReader reader, Frame f, Rectangle area, out int a, out int b)
        {
            a = b = -1;
            foreach (string text in Attempts(reader, f, area))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Fix(text), @"(\d+)\s*/\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out a) && int.TryParse(m.Groups[2].Value, out b) && a <= b && b > 0) return true;
            }
            a = b = -1;
            return false;
        }
    }

    // How different two bits of text are, 0 (same) to 1, ignoring case, spaces and punctuation.
    // OCR often gets a letter or two wrong, so names are matched with this rather than exactly.
    public static class Fuzzy
    {
        static string Letters(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (char c in s.ToUpperInvariant()) if (char.IsLetterOrDigit(c)) b.Append(c);
            return b.ToString();
        }

        public static double Distance(string a, string b)
        {
            a = Letters(a);
            b = Letters(b);
            if (a.Length == 0 || b.Length == 0) return 1;
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] t = prev; prev = cur; cur = t;
            }
            return (double)prev[b.Length] / Math.Max(a.Length, b.Length);
        }

        // How much of the word appears in the text in one unbroken run, 0 to 1 ("RFAMIL" has 5 of
        // FAMILY's 6 letters together: 0.83). For labels with busy backgrounds that OCR reads only partly.
        public static double Overlap(string text, string word)
        {
            string t = Letters(text), w = Letters(word);
            if (t.Length == 0 || w.Length == 0) return 0;
            int best = 0;
            for (int i = 0; i < w.Length; i++)
                for (int j = 0; j < t.Length; j++)
                {
                    int k = 0;
                    while (i + k < w.Length && j + k < t.Length && w[i + k] == t[j + k]) k++;
                    if (k > best) best = k;
                }
            return (double)best / w.Length;
        }

        // True when the text is the name, or starts with it (OCR can join neighbouring words on).
        public static bool StartsLike(string text, string name)
        {
            if (Distance(text, name) < 0.3) return true;
            string t = Letters(text), n = Letters(name);
            return t.Length > n.Length && Distance(t.Substring(0, n.Length), n) < 0.25;
        }
    }
}

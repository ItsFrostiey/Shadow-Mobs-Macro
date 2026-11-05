using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace StaminaDonator
{
    // One feature's calibration for one game size: where things are (Boxes, in pixels), colours
    // and other numbers that don't change with size (Values), and small reference pictures (Images).
    // Saved as profiles/<W>x<H>/<feature>.txt plus <feature>_<name>.png, so each feature can be
    // recalibrated on its own, and each screen size keeps its own copy.
    public class Profile
    {
        public string Feature;
        public int W, H;
        public string ScaledFrom;   // null when calibrated for exactly this size
        public Dictionary<string, Rectangle> Boxes = new Dictionary<string, Rectangle>();
        public Dictionary<string, int> Values = new Dictionary<string, int>();
        public Dictionary<string, Frame> Images = new Dictionary<string, Frame>();

        public Profile(string feature, int w, int h)
        {
            Feature = feature;
            W = w;
            H = h;
        }

        public Rectangle Box(string key)
        {
            Rectangle r;
            if (!Boxes.TryGetValue(key, out r)) throw new InvalidOperationException("The " + Feature + " calibration is missing \"" + key + "\". Calibrate it again.");
            return r;
        }

        public int Value(string key)
        {
            int v;
            if (!Values.TryGetValue(key, out v)) throw new InvalidOperationException("The " + Feature + " calibration is missing \"" + key + "\". Calibrate it again.");
            return v;
        }

        public Frame Image(string key)
        {
            Frame f;
            if (!Images.TryGetValue(key, out f)) throw new InvalidOperationException("The " + Feature + " calibration is missing the " + key + " picture. Calibrate it again.");
            return f;
        }

        public static string Folder(int w, int h) { return App.PathOf("profiles", w + "x" + h); }

        public void Save()
        {
            string dir = Folder(W, H);
            Directory.CreateDirectory(dir);
            foreach (string old in Directory.GetFiles(dir, Feature + "_*.png")) File.Delete(old);
            StringBuilder b = new StringBuilder();
            b.AppendLine("# " + Feature + " calibration for a " + W + "x" + H + " game. Saved " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            foreach (KeyValuePair<string, Rectangle> k in Boxes)
                b.AppendLine("box." + k.Key + "=" + k.Value.X + "," + k.Value.Y + "," + k.Value.Width + "," + k.Value.Height);
            foreach (KeyValuePair<string, int> k in Values)
                b.AppendLine("val." + k.Key + "=" + k.Value);
            File.WriteAllText(Path.Combine(dir, Feature + ".txt"), b.ToString());
            foreach (KeyValuePair<string, Frame> k in Images)
                k.Value.Save(Path.Combine(dir, Feature + "_" + k.Key + ".png"));
        }

        static Profile LoadExact(string feature, int w, int h)
        {
            string dir = Folder(w, h);
            string file = Path.Combine(dir, feature + ".txt");
            if (!File.Exists(file)) return null;
            Profile p = new Profile(feature, w, h);
            foreach (string line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq < 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                if (key.StartsWith("box."))
                {
                    string[] n = value.Split(',');
                    int x, y, bw, bh;
                    if (n.Length == 4 && int.TryParse(n[0], out x) && int.TryParse(n[1], out y) && int.TryParse(n[2], out bw) && int.TryParse(n[3], out bh))
                        p.Boxes[key.Substring(4)] = new Rectangle(x, y, bw, bh);
                }
                else if (key.StartsWith("val."))
                {
                    int v;
                    if (int.TryParse(value, out v)) p.Values[key.Substring(4)] = v;
                }
            }
            foreach (string png in Directory.GetFiles(dir, feature + "_*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(png).Substring(feature.Length + 1);
                p.Images[name] = Frame.Load(png);
            }
            return p;
        }

        // The calibration for this exact size if there is one. Otherwise one from another screen
        // with the same shape (16:9 and so on), scaled to fit. That usually works, but the UI warns
        // that calibrating on this screen is more reliable. Null if nothing suitable exists.
        public static Profile Load(string feature, int w, int h)
        {
            Profile exact = LoadExact(feature, w, h);
            if (exact != null) return exact;

            string root = App.PathOf("profiles");
            if (!Directory.Exists(root)) return null;
            Profile best = null;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string[] size = Path.GetFileName(dir).Split('x');
                int pw, ph;
                if (size.Length != 2 || !int.TryParse(size[0], out pw) || !int.TryParse(size[1], out ph)) continue;
                if (!File.Exists(Path.Combine(dir, feature + ".txt"))) continue;
                if (Math.Abs((double)pw / ph - (double)w / h) > 0.02) continue;
                if (best == null || Math.Abs(ph - h) < Math.Abs(best.H - h)) best = LoadExact(feature, pw, ph);
            }
            return best == null ? null : best.ScaleTo(w, h);
        }

        Profile ScaleTo(int w, int h)
        {
            double sx = (double)w / W, sy = (double)h / H;
            Profile p = new Profile(Feature, w, h);
            p.ScaledFrom = W + "x" + H;
            foreach (KeyValuePair<string, Rectangle> k in Boxes)
            {
                Rectangle r = k.Value;
                p.Boxes[k.Key] = new Rectangle((int)Math.Round(r.X * sx), (int)Math.Round(r.Y * sy),
                    Math.Max(r.Width == 0 ? 0 : 1, (int)Math.Round(r.Width * sx)), Math.Max(r.Height == 0 ? 0 : 1, (int)Math.Round(r.Height * sy)));
            }
            foreach (KeyValuePair<string, int> k in Values) p.Values[k.Key] = k.Value;
            foreach (KeyValuePair<string, Frame> k in Images)
                p.Images[k.Key] = k.Value.Resize((int)Math.Round(k.Value.W * sx), (int)Math.Round(k.Value.H * sy));
            return p;
        }

        public enum State { GameClosed, Missing, Borrowed, Ready }

        // Quick check for the UI, from file names only. borrowedFrom is set for State.Borrowed.
        public static State Check(string feature, Size game, out string borrowedFrom)
        {
            borrowedFrom = null;
            if (game.IsEmpty) return State.GameClosed;
            if (File.Exists(Path.Combine(Folder(game.Width, game.Height), feature + ".txt"))) return State.Ready;
            string root = App.PathOf("profiles");
            if (!Directory.Exists(root)) return State.Missing;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string[] size = Path.GetFileName(dir).Split('x');
                int pw, ph;
                if (size.Length != 2 || !int.TryParse(size[0], out pw) || !int.TryParse(size[1], out ph)) continue;
                if (!File.Exists(Path.Combine(dir, feature + ".txt"))) continue;
                if (Math.Abs((double)pw / ph - (double)game.Width / game.Height) > 0.02) continue;
                borrowedFrom = pw + "x" + ph;
                return State.Borrowed;
            }
            return State.Missing;
        }
    }
}

using System;
using System.Drawing;
using System.Text.RegularExpressions;

namespace StaminaDonator
{
    // Reads the pet screens. The Animals window has three tabs (OWNED, ANIMAL SHOP, ANIMAL TRAINING) and
    // the tab row shows on all of them, so "the window is open" = the ANIMAL TRAINING tab can be read, and
    // "it's on the training tab" = the big button below reads TRAIN or RESTING, with the lines above it
    // ("Stat Boost" is resting / Train again in 19:58). Both are needed, because job names like
    // "Rob the Alpine Gold Train" can sit where the button is on the Jobs page.
    //
    // Feeding happens on the OWNED tab (where the window opens): HUNGER "88%", "FOOD 9  PREMIUM FOOD 1",
    // and the FEED 10% / FEED 50% buttons, with REMOVE right beside FEED 50%.
    public class PetVision
    {
        public readonly Rectangle SafehouseButton, AnimalsButton, Tab, Train, Close;
        public readonly Rectangle Owned, Hunger, Food, Feed10, Feed50;   // empty on calibrations from before feeding
        public readonly bool CanFeed;
        readonly int trainColor;
        readonly ITextReader reader;

        public PetVision(Profile p, ITextReader reader)
        {
            this.reader = reader;
            SafehouseButton = p.Box("safehousebtn");
            AnimalsButton = p.Box("animals");
            Tab = p.Box("tab");
            Train = p.Box("train");
            Close = p.Box("close");
            trainColor = p.Value("trainColor");
            Owned = Optional(p, "owned");
            Hunger = Optional(p, "hunger");
            Food = Optional(p, "food");
            Feed10 = Optional(p, "feed10");
            Feed50 = Optional(p, "feed50");
            CanFeed = !Owned.IsEmpty && !Hunger.IsEmpty && !Food.IsEmpty && !Feed10.IsEmpty && !Feed50.IsEmpty;
        }

        static Rectangle Optional(Profile p, string key)
        {
            Rectangle r;
            return p.Boxes.TryGetValue(key, out r) ? r : Rectangle.Empty;
        }

        string Read(Frame f, Rectangle r, int scale, bool white)
        {
            string text = "";
            foreach (OcrLine l in white ? reader.ReadWhite(f, r, scale) : reader.Read(f, r, scale)) text += " " + l.Text;
            return text.ToUpperInvariant();
        }

        // ---------- Feeding (OWNED tab) ----------

        public bool OwnedShowing(Frame f) { return CanFeed && Text(f, Feed10).Contains("FEED"); }

        // The FEED word in a FEED button's box, checked right before each click: exactly one FEED and no
        // REMOVE (which sits right beside FEED 50%). The click goes on the word, well inside the button.
        // (The "10%" / "50%" after it doesn't read reliably, so which button is which comes from calibration:
        // FEED 10% is the left one.)
        public bool FindFeed(Frame f, Rectangle box, out Point at)
        {
            at = Point.Empty;
            OcrWord feed;
            int count;
            bool remove;
            FeedWords(reader, f, box, out feed, out count, out remove);
            if (feed == null || count != 1 || remove) return false;
            at = new Point(feed.Box.X + feed.Box.Width / 2, feed.Box.Y + feed.Box.Height / 2);
            return true;
        }

        public static void FeedWords(ITextReader reader, Frame f, Rectangle box, out OcrWord feed, out int count, out bool remove)
        {
            feed = null;
            count = 0;
            remove = false;
            for (int attempt = 0; attempt < 3 && feed == null; attempt++)
            {
                int found = 0;
                foreach (OcrLine l in attempt == 1 ? reader.ReadWhite(f, box, 2) : reader.Read(f, box, attempt == 0 ? 2 : 3))
                    foreach (OcrWord w in l.Words)
                    {
                        string t = w.Text.ToUpperInvariant();
                        if (t.Contains("REM")) remove = true;
                        if (t.Contains("FEED")) { feed = w; found++; }
                    }
                count = Math.Max(count, found);
            }
        }

        // Somewhere empty between the window's heading and its tabs, away from the FEED buttons.
        public Point FeedPark { get { return new Point(Owned.X + Owned.Width / 2, Owned.Top - Owned.Height / 2); } }

        public int ReadHunger(Frame f) { return ReadHungerIn(reader, f, Hunger); }

        // The hunger percentage. Two reads must agree, since a misread could waste food. -1 if unsure.
        // Small text reads best at 2x (bigger zooms sometimes turn the % into "0/0"). The box is read as
        // drawn first; a little padding helps a tight box, but reaching the card's teal edge spoils it.
        public static int ReadHungerIn(ITextReader reader, Frame f, Rectangle box)
        {
            Rectangle padded = Rectangle.Inflate(box, box.Width < box.Height * 4 ? box.Height : 0, 2);
            padded.Intersect(new Rectangle(0, 0, f.W, f.H));
            Rectangle[] areas = { box, box, padded, padded, box, padded, box, padded };
            int[] scales = { 2, 2, 2, 2, 3, 3, 4, 4 };
            System.Collections.Generic.List<int> seen = new System.Collections.Generic.List<int>();
            for (int attempt = 0; attempt < areas.Length; attempt++)
            {
                string text = "";
                foreach (OcrLine l in attempt % 2 == 1 ? reader.ReadWhite(f, areas[attempt], scales[attempt]) : reader.Read(f, areas[attempt], scales[attempt])) text += " " + l.Text;
                int v = ParseHunger(text);
                if (v < 0) continue;
                if (seen.Contains(v)) return v;
                seen.Add(v);
            }
            return -1;
        }

        // "HUNGER 88%" -> 88 (the last "N%", in case a loose box takes in more). OCR often reads the %
        // sign as "0/0", so that counts as %. -1 if none.
        public static int ParseHunger(string text)
        {
            string t = Regex.Replace(Numbers.Fix(text.ToUpperInvariant()), @"0\s*/\s*[06]", "%");
            MatchCollection all = Regex.Matches(t, @"(\d{1,3})\s*%");
            int v;
            if (all.Count > 0 && int.TryParse(all[all.Count - 1].Groups[1].Value, out v) && v <= 100) return v;
            return -1;
        }

        // "FOOD 9  PREMIUM FOOD 1" -> 9 and 1, each -1 if it can't be read. (A lone "1" often isn't read
        // at all, so -1 means "unknown", not "none".)
        public void ReadFood(Frame f, out int food, out int premium)
        {
            food = premium = -1;
            for (int attempt = 0; attempt < 4 && (food < 0 || premium < 0); attempt++)
            {
                int a, b;
                ParseFood(Read(f, Food, attempt % 2 == 0 ? 3 : 2, attempt >= 2), out a, out b);
                if (food < 0) food = a;
                if (premium < 0) premium = b;
            }
        }

        public static void ParseFood(string text, out int food, out int premium)
        {
            text = text.ToUpperInvariant();
            int p = text.IndexOf("PREM");
            food = CountAfterFood(p >= 0 ? text.Substring(0, p) : text);
            premium = p >= 0 ? CountAfterFood(text.Substring(p)) : -1;
        }

        static int CountAfterFood(string s)
        {
            Match m = Regex.Match(s, @"F[O0]{2}D\s*([0-9OISBL|]{1,5})\b");
            int n;
            return m.Success && int.TryParse(Numbers.Fix(m.Groups[1].Value.Replace('L', '1')), out n) ? n : -1;
        }

        // ---------- Training (ANIMAL TRAINING tab) ----------

        public string Text(Frame f, Rectangle r)
        {
            string text = "";
            foreach (OcrLine l in reader.Read(f, r, 2)) text += " " + l.Text;
            return text.ToUpperInvariant();
        }

        public bool WindowOpen(Frame f) { return Text(f, Tab).Contains("TRAINING"); }

        public bool AnimalsShowing(Frame f) { return Text(f, AnimalsButton).Contains("ANIMAL"); }

        // The TRAIN button and the two lines just above it ("is resting", "Train again in 19:58").
        public Rectangle PanelArea
        {
            get { return Rectangle.FromLTRB(Train.Left - Train.Width / 2, Train.Top - Train.Height * 2, Train.Right + Train.Width / 2, Train.Bottom); }
        }

        public static bool IsTrainingPanel(string panel)
        {
            return panel.Contains("RESTING") || panel.Contains("AGAIN") || Regex.IsMatch(panel, @"\bTRA[I1L|]N\b");
        }

        // Somewhere empty below the button, inside the window: the mouse waits there so it isn't
        // hovering TRAIN (hovered buttons turn a darker yellow) when its colour is checked.
        public Point Park { get { return new Point(Train.X + Train.Width / 2, Train.Bottom + Train.Height); } }

        // TRAIN is lit (the DO JOB yellow) when it can be pressed; RESTING is grey. Counts the yellow
        // pixels across the whole box, so a loosely drawn box (some background around the button)
        // still works: lit is well over a third yellow, RESTING has none.
        public bool Lit(Frame f)
        {
            Rectangle r = Rectangle.Intersect(Train, new Rectangle(0, 0, f.W, f.H));
            int hits = 0, total = 0;
            for (int y = r.Top + 1; y < r.Bottom; y += 3)
                for (int x = r.Left + 1; x < r.Right; x += 3)
                {
                    total++;
                    if (Px.Near(f.Get(x, y), trainColor, 40)) hits++;
                }
            return total > 0 && hits * 3 >= total;
        }

        // "Train again in 19:58" -> 19:58. Null if it can't be read.
        public static TimeSpan? Timer(string panel)
        {
            int at = panel.IndexOf("AGAIN");
            string text = Numbers.Fix(at >= 0 ? panel.Substring(at) : panel);
            Match m = Regex.Match(text, @"(\d{1,2})\s*[:.]\s*(\d{2})");
            int mm, ss;
            if (m.Success && int.TryParse(m.Groups[1].Value, out mm) && int.TryParse(m.Groups[2].Value, out ss) && ss < 60 && mm <= 20)
                return new TimeSpan(0, mm, ss);
            return null;
        }

        // For Check screen.
        public string Describe(Frame f)
        {
            if (WindowOpen(f))
            {
                if (OwnedShowing(f))
                {
                    int food, premium, hunger = ReadHunger(f);
                    ReadFood(f, out food, out premium);
                    Point at;
                    return string.Format("Animals window, OWNED tab: hunger {0}, food {1}, premium food {2}; FEED 10% {3}, FEED 50% {4}.",
                        hunger >= 0 ? hunger + "%" : "unreadable", food >= 0 ? food.ToString() : "unreadable", premium >= 0 ? premium.ToString() : "unreadable",
                        FindFeed(f, Feed10, out at) ? "found" : "NOT found", FindFeed(f, Feed50, out at) ? "found" : "NOT found");
                }
                string panel = Text(f, PanelArea);
                if (!IsTrainingPanel(panel)) return "The Animals window is open, but not on ANIMAL TRAINING (the tool clicks the tab itself).";
                if (Lit(f)) return "Animal Training: TRAIN is ready to press.";
                TimeSpan? left = Timer(panel);
                return left != null
                    ? string.Format("Animal Training: the pet is resting, {0}:{1:00} to go.", (int)left.Value.TotalMinutes, left.Value.Seconds)
                    : "Animal Training: TRAIN isn't lit, and there's no timer (not enough energy, or the pet is hungry?).";
            }
            if (AnimalsShowing(f)) return "Safehouse page, with the ANIMALS button.";
            return "Neither the Safehouse page nor the Animals window is showing (the tool opens them itself).";
        }
    }
}

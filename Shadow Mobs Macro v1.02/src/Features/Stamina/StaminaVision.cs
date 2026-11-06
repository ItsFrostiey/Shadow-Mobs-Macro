using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace StaminaDonator
{
    public class PerkSighting
    {
        public int Index;
        public int Top;            // y of the top of the perk's name text, in frame pixels
        public bool ButtonsInView;
        public bool Give1Ready, Give5Ready;
        public bool Maxed;
    }

    public class StaminaSnapshot
    {
        public bool Hud;           // the top bar is where calibration says (HEALTH seen in this frame)
        public bool HudCovered;    // HEALTH hidden (notices can stack down over it): its last spot is used
        public int Ox, Oy;         // how far this frame is shifted from the calibrated positions
        public bool StaminaVisible;   // false while a notification covers the stamina row
        public int Stamina = -1;
        public bool FamilyHeader, PerksTab;
        public List<PerkSighting> Perks = new List<PerkSighting>();

        // The page's own FAMILY heading and PERKS underline prove the page, even with HEALTH covered.
        public bool OnPerksPage { get { return (Hud || HudCovered) && FamilyHeader && PerksTab; } }

        public PerkSighting Find(int index)
        {
            foreach (PerkSighting p in Perks) if (p.Index == index) return p;
            return null;
        }

        public string Describe()
        {
            StringBuilder b = new StringBuilder();
            b.Append("Top bar: " + (Hud ? "found" : "NOT found"));
            if (!Hud) return b.ToString();
            b.Append(". Stamina: " + (Stamina >= 0 ? Stamina.ToString() : "can't read"));
            b.Append(". Family page: " + (FamilyHeader ? "yes" : "no") + ". Perks tab: " + (PerksTab ? "yes" : "no"));
            foreach (PerkSighting p in Perks)
            {
                b.Append(". " + StaminaDonator.Perks.Names[p.Index]);
                if (!p.ButtonsInView) b.Append(" (buttons off screen)");
                else if (p.Maxed) b.Append(" (level 25)");
                else b.Append(" (Give 1 " + (p.Give1Ready ? "ready" : "grey") + ", Give 5 " + (p.Give5Ready ? "ready" : "grey") + ")");
            }
            return b.ToString();
        }
    }

    // Everything that knows what Family > Perks looks like, driven by the stamina calibration.
    public class StaminaVision
    {
        public readonly Profile P;
        readonly ITextReader reader;
        readonly Rectangle health, stamina, family, bar, underline, list, names, give1, give5, scroll;
        readonly Rectangle? segment;
        readonly int barFill, barEmpty, underlineColor, giveColor;

        public StaminaVision(Profile profile, ITextReader reader)
        {
            P = profile;
            this.reader = reader;
            health = P.Box("health");
            stamina = P.Box("stamina");
            family = P.Box("family");
            bar = P.Box("bar");
            underline = P.Box("underline");
            list = P.Box("list");
            names = P.Box("names");
            give1 = P.Box("give1");
            give5 = P.Box("give5");
            scroll = P.Box("scroll");
            Rectangle s;
            segment = P.Boxes.TryGetValue("segment", out s) ? (Rectangle?)s : null;
            barFill = P.Value("barFill");
            barEmpty = P.Value("barEmpty");
            underlineColor = P.Value("underlineColor");
            giveColor = P.Value("giveColor");
            P.Image("health");
            P.Image("stamina");
            P.Image("family");
        }

        // A perk's level from its "LEVEL 7 / 25" text, on the same row as its name, right of the name
        // column. -1 if it can't be read.
        public int ReadPerkLevel(Frame f, PerkSighting perk, StaminaSnapshot s)
        {
            int h = Math.Max(14, give1.Height * 3 / 4);
            // Out to the list's right edge: the text runs past the GIVE 5 button.
            Rectangle area = Rectangle.FromLTRB(names.Right + s.Ox, perk.Top - h / 4, list.Right + s.Ox, perk.Top + h);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int k = new[] { 2, 3, 4 }[attempt % 3];
                string text = "";
                foreach (OcrLine l in attempt < 3 ? reader.Read(f, area, k) : reader.ReadWhite(f, area, k)) text += " " + l.Text;
                // "LEVEL 24 / 25", or at least "LEVEL 24" if the end is cut off.
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Numbers.Fix(text), @"(\d{1,2})\s*/\s*25");
                if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(text.ToUpperInvariant(), @"LEVEL\s*([0-9OIl]{1,2})\b");
                int level;
                if (m.Success && int.TryParse(Numbers.Fix(m.Groups[1].Value), out level) && level >= 0 && level <= 25) return level;
            }
            return -1;
        }

        // The FAMILY button in the game's menu, or null on calibrations made before that step existed.
        public Rectangle? FamilyButton
        {
            get { Rectangle r; return P.Boxes.TryGetValue("familybtn", out r) ? (Rectangle?)r : null; }
        }

        // The whole stamina bar, with its "24 / 50" and "(+1 0:31)" text. Calibration keeps only the bar's
        // top row, so the height comes from the STAMINA label beside it (the bar is a bit taller).
        public Rectangle StaminaBar
        {
            get
            {
                int h = Math.Max(12, stamina.Height * 9 / 5);
                return new Rectangle(bar.X, bar.Y - 2, bar.Width, h);
            }
        }

        public Rectangle List { get { return list; } }
        public Rectangle Tab { get { return P.Box("tab"); } }
        public Point ScrollPoint { get { return scroll.Location; } }
        public Rectangle Give(int amount) { return amount == 5 ? give5 : give1; }

        int lastOx, lastOy;
        bool hudSeen;    // HEALTH has been found at least once (START always finds it first)

        public StaminaSnapshot Analyze(Frame f, int maxStamina)
        {
            StaminaSnapshot s = new StaminaSnapshot();
            int ox, oy;
            if (Templates.Search(P.Image("health"), f, health.X, health.Y, 12, 12, out ox, out oy))
            {
                s.Hud = hudSeen = true;
                lastOx = ox;
                lastOy = oy;
            }
            else if (hudSeen)
            {
                // A notice is over HEALTH; the top bar hasn't moved, so carry on from where it was.
                s.HudCovered = true;
                ox = lastOx;
                oy = lastOy;
            }
            else return s;
            s.Ox = ox;
            s.Oy = oy;

            s.StaminaVisible = Templates.Matches(P.Image("stamina"), f, stamina.X + ox, stamina.Y + oy);
            if (s.StaminaVisible) s.Stamina = ReadStamina(f, ox, oy, maxStamina);

            s.FamilyHeader = Templates.Matches(P.Image("family"), f, family.X + ox, family.Y + oy);
            int uy = underline.Y + oy;
            s.PerksTab = Px.Near(f.Get(underline.Left + ox, uy), underlineColor)
                      && Px.Near(f.Get(underline.Left + underline.Width / 2 + ox, uy), underlineColor)
                      && Px.Near(f.Get(underline.Right - 1 + ox, uy), underlineColor);
            if (s.FamilyHeader && s.PerksTab) FindPerks(f, s);
            return s;
        }

        // The bar is a solid colour with its text lower down, so its top row gives an exact fill
        // width. It never needs to read the digits.
        int ReadStamina(Frame f, int ox, int oy, int maxStamina)
        {
            int x0 = bar.X + ox, y = bar.Y + oy;
            int first = f.Get(x0 + 1, y);
            if (Px.Near(first, barEmpty)) return 0;
            if (!Px.Near(first, barFill)) return -1;
            int len = 1;
            while (len < bar.Width + 3 && Px.Near(f.Get(x0 + len, y), barFill)) len++;
            return Math.Min(maxStamina, (int)Math.Round(len * (double)maxStamina / bar.Width));
        }

        // Reads the perk names as text, then checks each card's buttons at the calibrated offsets.
        void FindPerks(Frame f, StaminaSnapshot s)
        {
            Rectangle area = new Rectangle(names.X + s.Ox, names.Y + s.Oy, names.Width, names.Height);
            foreach (OcrLine line in reader.Read(f, area, 2))
            {
                int index = Perks.Match(line.Text);
                if (index < 0 || s.Find(index) != null) continue;
                PerkSighting p = new PerkSighting();
                p.Index = index;
                p.Top = line.Box.Top;
                p.ButtonsInView = p.Top + give1.Top >= list.Top + 2 + s.Oy && p.Top + give1.Bottom <= list.Bottom - 2 + s.Oy;
                if (p.ButtonsInView)
                {
                    p.Give1Ready = ButtonLit(f, give1, s.Ox, p.Top);
                    p.Give5Ready = ButtonLit(f, give5, s.Ox, p.Top);
                    // The level bar has 25 segments, so a lit final segment means 25/25.
                    p.Maxed = segment != null && Px.Near(f.Get(segment.Value.X + s.Ox, p.Top + segment.Value.Y), giveColor);
                }
                s.Perks.Add(p);
            }
            s.Perks.Sort(delegate(PerkSighting a, PerkSighting b) { return a.Top.CompareTo(b.Top); });
        }

        // Samples the button's corners, away from its text. A greyed-out button or anything
        // covering it (such as a Roblox notification) reads as not lit.
        bool ButtonLit(Frame f, Rectangle button, int ox, int top)
        {
            int insetX = Math.Max(3, button.Width / 18), insetY = Math.Max(2, button.Height / 8);
            int left = button.Left + insetX + ox, right = button.Right - 1 - insetX + ox;
            int upper = top + button.Top + insetY, lower = top + button.Bottom - 1 - insetY;
            return Px.Near(f.Get(left, upper), giveColor) && Px.Near(f.Get(right, upper), giveColor)
                && Px.Near(f.Get(left, lower), giveColor) && Px.Near(f.Get(right, lower), giveColor);
        }
    }
}

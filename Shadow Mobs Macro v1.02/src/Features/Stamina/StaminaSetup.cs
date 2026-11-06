using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Calibration for the stamina donator, done on a screenshot of Family > Perks.
    // Perk names are read as text, so only the top bar labels and the FAMILY heading are pictures,
    // and those are re-taken whenever this is calibrated again (for a new theme, say).
    public static class StaminaSetup
    {
        public const string Feature = "stamina";
        public const string Instructions = "Open the game on Family > Perks with Enforcers at the top of the list and at least 5 stamina.";

        // Ticked off by the user before the screenshot is taken.
        public static readonly string[] Checklist =
        {
            "The game is open on the Family page, with the PERKS tab selected",
            "The stamina perks list is scrolled right to the top, with Enforcers fully showing",
            "I have at least 5 stamina, so both GIVE 1 and GIVE 5 on Enforcers are lit (not greyed out)",
            "No donation popup or other notification is covering the stamina bar"
        };

        static Rectangle Tight(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Tighten(f, r); }

        static readonly Dictionary<string, string> Why = new Dictionary<string, string>
        {
            { "health", "This is the tool's anchor. Before reading anything it looks for HEALTH to check the game's top bar is on screen, and to allow for the game shifting by a few pixels." },
            { "stamina", "Lets the tool check the stamina row is visible and not covered by a Roblox notification, so it never misreads your stamina." },
            { "bar", "Your stamina is measured from how full this bar is. That's how the tool knows when you have enough to donate, without reading any numbers." },
            { "family", "Tells the tool it's on the Family page. If it isn't, it opens Family from the sidebar by itself." },
            { "tab", "The coloured line under the selected tab shows Perks is open. This is also where the tool clicks to open the Perks tab." },
            { "name", "Every perk card is measured from its name. The names are read as text (so a new theme won't break it); this teaches the tool where the names sit." },
            { "level", "Marks where the name column ends, and helps find the 25-segment level bar so the tool knows when a perk is maxed at 25/25." },
            { "give1", "Where to click to give 1 stamina, and what a lit button looks like, so a greyed-out button is never clicked." },
            { "give5", "Where to click to give 5 stamina, and what a lit button looks like, so a greyed-out button is never clicked." },
            { "list", "The area the tool reads perk names from and scrolls in. For safety it will never click outside it, except the Perks tab." },
            { "familybtn", "Where the tool clicks to open the Family page, from any other page (such as Jobs, when running both)." }
        };

        public static CalibrationStep[] Steps(ITextReader reader)
        {
            return CalibrationStep.Describe(Feature, AllSteps(reader), Why);
        }

        static CalibrationStep[] AllSteps(ITextReader reader)
        {
            return new[]
            {
                new CalibrationStep("health", "HEALTH label", "Drag a box around the word HEALTH in the top bar.", Tight),
                new CalibrationStep("stamina", "STAMINA label", "Drag a box around the word STAMINA in the top bar.", Tight),
                new CalibrationStep("bar", "Stamina bar", "Drag a box around the whole stamina bar, including its empty part.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { Snap.Bar(f, r); return Snap.Check(f, r); }),
                new CalibrationStep("family", "FAMILY heading", "Drag a box around the FAMILY heading above the tabs.", Tight),
                new CalibrationStep("tab", "PERKS tab", "Drag a box around the PERKS tab, including the coloured line under it.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { Snap.Underline(f, r); return Snap.Check(f, r); }),
                new CalibrationStep("name", "First perk's name", "Drag a box around the name Enforcers.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { Rectangle t = Snap.Tighten(f, r); NameTop(f, t, reader); return t; }),
                new CalibrationStep("level", "First perk's level", "Drag a box around the level text at the top right of the Enforcers card.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        Rectangle t = Snap.Tighten(f, r);
                        if (t.Left <= m["name"].Right) throw new CalibrationException("The level text should be to the right of the name. Try again.");
                        return t;
                    }),
                new CalibrationStep("give1", "GIVE 1 button", "Drag a box inside the GIVE 1 button on Enforcers. It needs to be lit, not greyed out.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Button(f, r); }),
                new CalibrationStep("give5", "GIVE 5 button", "Drag a box inside the GIVE 5 button on Enforcers. It needs to be lit, not greyed out.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Button(f, r); }),
                new CalibrationStep("list", "The whole perks area", "Drag one big box over the whole perks list, from just under the tabs to the bottom of the game. A perk being cut off is fine.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (r.Height < f.H / 3) throw new CalibrationException("That box is too short. Cover the whole perks list, down to the bottom of the game.");
                        foreach (string k in new[] { "name", "give1", "give5" })
                            if (!r.Contains(m[k])) throw new CalibrationException("That box doesn't cover the Enforcers name and both buttons. Make it bigger.");
                        return r;
                    }),
                new CalibrationStep("familybtn", "Family Button", "Drag a box around the FAMILY button in the left-hand menu (the one with the picture behind it, below BOSSES; not CREW).", MenuButton(reader, "FAMILY"))
            };
        }

        // A button in the game's left-hand menu (FAMILY, JOBS, BANK). Kept as dragged, but its text is
        // read: if it clearly says something else (it's easy to box CREW instead of FAMILY), it's refused.
        public static Func<Frame, Rectangle, Dictionary<string, Rectangle>, Rectangle> MenuButton(ITextReader reader, string word)
        {
            return delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
            {
                r = Snap.Check(f, r);
                if (r.Right > f.W / 4) throw new CalibrationException("That should be in the menu down the left-hand side of the game. Try again.");
                if (r.Width < 20 || r.Height < 8) throw new CalibrationException("That box is too small. Cover the whole " + word + " button.");
                string text = MenuText(reader, f, r);
                string looksLike = MenuWordFor(text);
                if (looksLike != null && looksLike != word)
                    throw new CalibrationException("That looks like the " + looksLike + " button, not " + word + ". Box the " + word + " button.");
                return r;
            };
        }

        static readonly string[] MenuWords =
        {
            "SAFEHOUSE", "JOBS", "PROPERTIES", "INVENTORY", "COLLECTION", "SHOP", "FIGHT", "BANK", "OPERATIONS",
            "CONTRACTS", "CREW", "BOSSES", "FAMILY", "TERRITORY", "RANKINGS", "TRADE", "EVENTS"
        };

        // Which menu button a (possibly partly read) label is, or null if it can't tell.
        public static string MenuWordFor(string text)
        {
            string best = null;
            double bestScore = 0.6;   // below this, it's too unclear to say
            foreach (string w in MenuWords)
            {
                double s = Fuzzy.Overlap(text, w);
                if (s > bestScore) { bestScore = s; best = w; }
            }
            return best;
        }

        // The letters of a menu button's label, in capitals ("" if nothing readable).
        public static string MenuText(ITextReader reader, Frame f, Rectangle r)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (OcrLine l in reader.Read(f, r, 2))
                foreach (char c in l.Text.ToUpperInvariant())
                    if (c >= 'A' && c <= 'Z') b.Append(c);
            return b.ToString();
        }

        // Where OCR says the name's text starts. Each perk card is measured from this, because it's
        // what the bot will see when it reads the other names.
        static int NameTop(Frame f, Rectangle name, ITextReader reader)
        {
            Rectangle area = Rectangle.Inflate(name, name.Height, name.Height / 2);
            foreach (OcrLine line in reader.Read(f, area, 2))
                if (Perks.Match(line.Text) == 0) return line.Box.Top;
            throw new CalibrationException("Couldn't read the name Enforcers there. Drag the box around just that word.");
        }

        public static Profile Build(Frame f, Dictionary<string, Rectangle> m, ITextReader reader)
        {
            Profile p = new Profile(Feature, f.W, f.H);
            foreach (string k in new[] { "health", "stamina", "family" })
            {
                p.Boxes[k] = m[k];
                p.Images[k] = f.Crop(m[k]);
            }

            Snap.BarInfo bar = Snap.Bar(f, m["bar"]);
            p.Boxes["bar"] = new Rectangle(bar.Track.Left, bar.Row, bar.Track.Width, 1);
            p.Values["barFill"] = bar.Fill;
            p.Values["barEmpty"] = bar.Empty;

            Snap.UnderlineInfo line = Snap.Underline(f, m["tab"]);
            p.Boxes["underline"] = Rectangle.FromLTRB(line.X1, line.Y, line.X3 + 1, line.Y + 1);
            p.Values["underlineColor"] = line.Color;
            p.Boxes["tab"] = m["tab"];

            Rectangle list = m["list"], name = m["name"], level = m["level"], give1 = m["give1"], give5 = m["give5"];
            p.Boxes["list"] = list;
            // The strip OCR reads names from: from the name's left edge to the level text.
            p.Boxes["names"] = Rectangle.FromLTRB(Math.Max(list.Left, name.Left - 6), list.Top, level.Left - 4, list.Bottom);

            // Card parts are stored relative to the top of the name's text.
            int top = NameTop(f, name, reader);
            p.Boxes["give1"] = new Rectangle(give1.X, give1.Y - top, give1.Width, give1.Height);
            p.Boxes["give5"] = new Rectangle(give5.X, give5.Y - top, give5.Width, give5.Height);
            p.Values["giveColor"] = Px.Modal(f, give1);
            int sx, sy;
            if (Snap.LastSegment(f, level, give5, out sx, out sy)) p.Boxes["segment"] = new Rectangle(sx, sy - top, 1, 1);

            // A safe spot for the mouse and the scroll wheel: between the name and the buttons.
            p.Boxes["scroll"] = new Rectangle((name.Right + give1.Left) / 2, list.Top + list.Height / 2, 1, 1);
            p.Boxes["familybtn"] = m["familybtn"];
            return p;
        }
    }
}

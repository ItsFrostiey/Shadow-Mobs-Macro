using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Calibration for auto-train pet. It takes three screenshots, because each screen hides the last:
    //   1. the Safehouse page: the SAFEHOUSE menu button and the ANIMALS button;
    //   2. the Animals window as it opens (OWNED tab): the tab, HUNGER, the food counts, FEED 10% / 50%;
    //   3. its ANIMAL TRAINING tab: the tab, the TRAIN button and the window's close (X) button.
    public static class PetSetup
    {
        public const string Feature = "pet";
        public const string Instructions = "Calibration takes three screenshots: the Safehouse page, the Animals window as it opens (OWNED tab), then its ANIMAL TRAINING tab.";

        public static readonly string[] Checklist1 =
        {
            "The game is open on the Safehouse page (SAFEHOUSE selected in the left-hand menu)",
            "The ANIMALS button below the safehouse picture is showing, and no window is open over it"
        };

        public static readonly string[] Checklist2 =
        {
            "The Animals window is open on the OWNED tab (press ANIMALS on the Safehouse page; it opens there)",
            "Your active animal's card shows HUNGER, the FOOD and PREMIUM FOOD counts, and the FEED 10% and FEED 50% buttons"
        };

        public static readonly string[] Checklist3 =
        {
            "The Animals window is on the ANIMAL TRAINING tab (click it at the top of the window)",
            "The big TRAIN button is showing (it's fine if it says RESTING)"
        };

        static readonly Dictionary<string, string> Why = new Dictionary<string, string>
        {
            { "safehousebtn", "Where the tool clicks to open the Safehouse page from any other page." },
            { "animals", "Where the tool clicks to open the Animals window." },
            { "owned", "Where the tool clicks to get to the OWNED tab, where the hunger and the FEED buttons are." },
            { "hunger", "The tool reads this number to decide when to feed: FEED 10% at 90% or less, FEED 50% at 50% or less." },
            { "food", "How much food and premium food is left, so the tool doesn't press FEED with none left." },
            { "feed10", "The button the tool presses to feed 10% (it uses one FOOD)." },
            { "feed50", "The button the tool presses to feed 50% (it uses one PREMIUM FOOD)." },
            { "tab", "Where the tool clicks to switch the Animals window to Animal Training." },
            { "train", "The button the tool presses to train. Its text tells it whether the pet is resting, and the timer just above says for how long." },
            { "close", "Where the tool clicks to close the Animals window when it's done, so the menu can be used again." }
        };

        static string TextIn(ITextReader reader, Frame f, Rectangle r)
        {
            string text = "";
            foreach (OcrLine l in reader.Read(f, r, 2)) text += " " + l.Text.ToUpperInvariant();
            return text;
        }

        static string TextIn(ITextReader reader, Frame f, Rectangle r, int scale)
        {
            string text = "";
            foreach (OcrLine l in reader.Read(f, r, scale)) text += " " + l.Text.ToUpperInvariant();
            return text;
        }

        // Screenshot 1: the Safehouse page.
        public static CalibrationStep[] Steps1(ITextReader reader)
        {
            return CalibrationStep.Describe(Feature, new[]
            {
                new CalibrationStep("safehousebtn", "Safehouse Button", "Drag a box around the SAFEHOUSE button in the left-hand menu.", StaminaSetup.MenuButton(reader, "SAFEHOUSE")),
                new CalibrationStep("animals", "Animals Button", "Drag a box around the ANIMALS button below the safehouse picture.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (!TextIn(reader, f, r).Contains("ANIMAL")) throw new CalibrationException("Couldn't read ANIMALS there. Box just the ANIMALS button.");
                        return r;
                    })
            }, Why);
        }

        // Screenshot 2: the Animals window on the OWNED tab.
        public static CalibrationStep[] Steps2(ITextReader reader)
        {
            return CalibrationStep.Describe(Feature, new[]
            {
                new CalibrationStep("owned", "Owned Tab", "Drag a box around the OWNED tab at the top of the window.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (!TextIn(reader, f, r).Contains("OWNED")) throw new CalibrationException("Couldn't read OWNED there. Box just that tab.");
                        return r;
                    }),
                new CalibrationStep("hunger", "Hunger", "Drag a box around the hunger number at the right end of the HUNGER row (88% in the example).",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        string t = TextIn(reader, f, r);
                        if (t.Contains("ATTACK") || t.Contains("DEFENSE") || t.Contains("FOOD")) throw new CalibrationException("That box takes in other lines too. Box just the HUNGER row.");
                        if (PetVision.ReadHungerIn(reader, f, r) < 0) throw new CalibrationException("Couldn't read a percentage there. Box the hunger number (like 88%).");
                        return r;
                    }),
                new CalibrationStep("food", "Food Counts", "Drag a box around the line with FOOD and PREMIUM FOOD, under the hunger bar.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        int food, premium;
                        PetVision.ParseFood(TextIn(reader, f, r, 3), out food, out premium);
                        if (food < 0) throw new CalibrationException("Couldn't read FOOD and its number there. Box the whole FOOD / PREMIUM FOOD line.");
                        return r;
                    }),
                new CalibrationStep("feed10", "Feed 10% Button", "Drag a box around the FEED 10% button.", FeedButton(reader, "10")),
                new CalibrationStep("feed50", "Feed 50% Button", "Drag a box around the FEED 50% button.", FeedButton(reader, "50"))
                {
                    Warning = "REMOVE is right next to FEED 50%. Box only FEED 50%."
                }
            }, Why);
        }

        // The box must hold exactly one FEED and no REMOVE (which sits right beside FEED 50%). The "10%" /
        // "50%" doesn't read reliably, so FEED 50% must be the FEED right of FEED 10%; a number that does
        // read must be the right one.
        static Func<Frame, Rectangle, Dictionary<string, Rectangle>, Rectangle> FeedButton(ITextReader reader, string number)
        {
            return delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
            {
                r = Snap.Check(f, r);
                OcrWord feed;
                int count;
                bool remove;
                PetVision.FeedWords(reader, f, r, out feed, out count, out remove);
                if (remove) throw new CalibrationException("That takes in the REMOVE button. Box only FEED " + number + "%.");
                if (feed == null) throw new CalibrationException("Couldn't read FEED there. Box just the FEED " + number + "% button.");
                if (count > 1) throw new CalibrationException("That takes in both FEED buttons. Box only FEED " + number + "%.");
                string other = number == "10" ? "50" : "10";
                if (PetVision.ParseHunger(TextIn(reader, f, r)).ToString() == other) throw new CalibrationException("That's FEED " + other + "%. Box FEED " + number + "%.");
                Rectangle first;
                if (number == "50" && m.TryGetValue("feed10", out first) && feed.Box.X < first.Right)
                    throw new CalibrationException("That's FEED 10%. Box FEED 50%, the second button.");
                return r;
            };
        }

        // Screenshot 3: Animals > Animal Training.
        public static CalibrationStep[] Steps3(ITextReader reader)
        {
            return CalibrationStep.Describe(Feature, new[]
            {
                new CalibrationStep("tab", "Animal Training Tab", "Drag a box around the ANIMAL TRAINING tab at the top of the window.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (!TextIn(reader, f, r).Contains("TRAINING")) throw new CalibrationException("Couldn't read ANIMAL TRAINING there. Box just that tab.");
                        return r;
                    }),
                new CalibrationStep("train", "Train Button", "Drag a box around the big TRAIN button (it may say RESTING; that's fine).",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        string t = TextIn(reader, f, r);
                        if (!t.Contains("TRAIN") && !t.Contains("REST")) throw new CalibrationException("Couldn't read TRAIN or RESTING there. Box the big button.");
                        return r;
                    }),
                new CalibrationStep("close", "Close Button", "Drag a small box around the X in the top-right corner of the Animals window.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (r.Width > f.W / 12 || r.Height > f.H / 8) throw new CalibrationException("That's too big. Box just the X.");
                        return r;
                    })
            }, Why);
        }

        // f is the last screenshot (Animal Training). The TRAIN button is the same yellow as DO JOB. Its
        // colour comes from the box itself if it's lit (not RESTING), otherwise from the Auto Jobs
        // calibration's DO JOB colour, otherwise the game's usual gold (#D4AF37, measured on DO JOB).
        public static Profile Build(Frame f, Dictionary<string, Rectangle> m, int jobsButtonColor)
        {
            Profile p = new Profile(Feature, f.W, f.H);
            foreach (string k in new[] { "safehousebtn", "animals", "owned", "hunger", "food", "feed10", "feed50", "tab", "train", "close" }) p.Boxes[k] = m[k];
            int c = Px.Modal(f, m["train"]);
            int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            p.Values["trainColor"] = r > 150 && g > 120 && b < 120 ? c : jobsButtonColor >= 0 ? jobsButtonColor : 0xD4AF37;
            return p;
        }
    }
}

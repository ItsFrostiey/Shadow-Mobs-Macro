using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Calibration for auto deposit, done on a screenshot of the Bank page.
    public static class BankSetup
    {
        public const string Feature = "bank";
        public const string Instructions = "Open the game on the Bank page, with no popup covering the First National Bank panel.";

        public static readonly string[] Checklist =
        {
            "The game is open on the Bank page (BANK selected in the left-hand menu)",
            "The First National Bank panel is fully showing, with ON HAND, BANKED and the DEPOSIT ALL button",
            "No popup or notification is covering the panel or the top bar"
        };

        static readonly Dictionary<string, string> Why = new Dictionary<string, string>
        {
            { "heading", "Confirms the Bank page is open. If it isn't, the tool opens it with the Bank button." },
            { "onhand", "The tool watches this number to catch the exact moment your cash goes up each 5 seconds, so it can deposit right after every payout." },
            { "deposit", "Where the tool clicks to deposit all your cash. It's the only button on this page the tool ever presses." },
            { "amount", "For the auto-buy withdrawal: the tool clicks here and types the amount to take out before each shop restock." },
            { "withdraw", "For the auto-buy withdrawal: pressed after typing the amount. WITHDRAW ALL is never pressed." },
            { "bankbtn", "Where the tool clicks to open the Bank page from any other page." }
        };

        public static CalibrationStep[] Steps(ITextReader reader)
        {
            return CalibrationStep.Describe(Feature, new[]
            {
                new CalibrationStep("heading", "Bank Heading", "Drag a box around the BANK heading at the top of the page.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        Rectangle t = Snap.Tighten(f, r);
                        if (!HeadingSaysBank(reader, f, t)) throw new CalibrationException("Couldn't read BANK there. Drag the box around just that heading.");
                        return t;
                    }),
                new CalibrationStep("onhand", "Cash On Hand", "Drag a wide box over the ON HAND amount: from just right of the words ON HAND to the right-hand edge, so a longer or shorter number still fits inside.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (r.Width < r.Height * 3) throw new CalibrationException("Make the box wider: the number grows and shrinks, so cover the whole space it can use.");
                        return r;
                    }),
                new CalibrationStep("deposit", "Deposit All Button", "Drag a box around the DEPOSIT ALL button (not DEPOSIT, and not WITHDRAW ALL).",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        string text = "";
                        foreach (OcrLine l in reader.Read(f, r, 2)) text += " " + l.Text.ToUpperInvariant();
                        if (!text.Contains("DEPOSIT") || !text.Contains("ALL")) throw new CalibrationException("Couldn't read DEPOSIT ALL there. Box just that button.");
                        return r;
                    }),
                new CalibrationStep("amount", "Amount Box", "Drag a box around the Amount box (where you'd type how much to withdraw).",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Check(f, r); }),
                new CalibrationStep("withdraw", "Withdraw Button", "Drag a box around the WITHDRAW button (not WITHDRAW ALL).",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        string text = "";
                        foreach (OcrLine l in reader.Read(f, r, 2)) text += " " + l.Text.ToUpperInvariant();
                        if (!text.Contains("WITHDRAW")) throw new CalibrationException("Couldn't read WITHDRAW there. Box just the WITHDRAW button.");
                        if (text.Contains("ALL")) throw new CalibrationException("That's WITHDRAW ALL. Box the WITHDRAW button above it.");
                        return r;
                    }),
                new CalibrationStep("bankbtn", "Bank Button", "Drag a box around the BANK button in the left-hand menu.", StaminaSetup.MenuButton(reader, "BANK"))
            }, Why);
        }

        public static bool HeadingSaysBank(ITextReader reader, Frame f, Rectangle heading)
        {
            Rectangle area = Rectangle.Inflate(heading, heading.Height, heading.Height / 2);
            foreach (OcrLine line in reader.Read(f, area, 2))
                if (line.Text.ToUpperInvariant().Contains("BANK")) return true;
            return false;
        }

        public static Profile Build(Frame f, Dictionary<string, Rectangle> m)
        {
            Profile p = new Profile(Feature, f.W, f.H);
            foreach (string k in new[] { "heading", "onhand", "deposit", "amount", "withdraw", "bankbtn" }) p.Boxes[k] = m[k];
            return p;
        }
    }
}

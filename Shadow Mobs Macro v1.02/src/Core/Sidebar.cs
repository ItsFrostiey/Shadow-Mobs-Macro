using System;
using System.Drawing;

namespace StaminaDonator
{
    // Opens a page from the game's left sidebar by reading the words in it, so it needs no
    // calibration, survives theme changes and still works if the sidebar has been scrolled.
    public static class Sidebar
    {
        public const double Width = 0.16;   // share of the game's width the sidebar takes up

        public static bool Open(IGame game, ITextReader reader, string word, Func<Frame, bool> arrived)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Frame f = game.Capture();
                if (f == null) return false;
                if (arrived(f)) return true;

                int width = (int)(f.W * Width);
                Rectangle area = new Rectangle(0, f.H / 10, width, f.H * 8 / 10);
                OcrWord target = null;
                foreach (OcrLine l in reader.Read(f, area, 2))
                {
                    if (l.Text.ToUpperInvariant().Contains("EVENT")) continue;   // "Event Shop" is a different page
                    foreach (OcrWord w in l.Words)
                        if (w.Text.ToUpperInvariant() == word || StaminaSetup.MenuWordFor(w.Text) == word) target = w;
                }
                if (target == null)
                {
                    game.Wheel(width / 2, f.H / 2, -6);
                    game.Sleep(300);
                    continue;
                }
                if (target.Box.Right > width) throw new InvalidOperationException("Blocked a sidebar click outside the sidebar.");
                game.Click(target.Box.Left + target.Box.Width / 2, target.Box.Top + target.Box.Height / 2);
                game.Sleep(800);
            }
            Frame last = game.Capture();
            return last != null && arrived(last);
        }
    }
}

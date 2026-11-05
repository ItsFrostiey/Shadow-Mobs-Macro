using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StaminaDonator
{
    // A small status panel drawn over the game while auto jobs runs: what it's doing, the job, and
    // XP per hour. Clicks pass straight through it to the game, it never takes focus, and it's left
    // out of screenshots (Windows 10 2004+), so the tool's own screen reading never sees it.
    public class Overlay : Form
    {
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;

        static readonly FontFamily Family = new FontFamily("Segoe UI");
        static readonly Color Grey = Color.FromArgb(0x2E, 0x2E, 0x33);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        string state = "";
        Color stateColor = Theme.Good;

        public Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Grey;
            Opacity = 0.99;   // looks solid; must stay below 1 or Windows drops the layering that click-through needs
            DoubleBuffered = true;
            Bounds = new Rectangle(-2000, -2000, 10, 10);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                return p;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE); }
            catch (EntryPointNotFoundException) { }
        }

        // Places the panel over the game, at the calibrated spot (screen pixels).
        public void Place(Rectangle screen)
        {
            if (Bounds != screen) Bounds = screen;
            // Re-assert "always on top" in case the game window was brought above it.
            if (IsHandleCreated) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // TOPMOST; NOSIZE | NOMOVE | NOACTIVATE
        }

        string[] lines = new string[0];
        Color[] lineColors = new Color[0];
        int boldLine = -1;

        // A large status line ("Running", "Waiting for energy (40/104)"...) plus smaller lines (the job,
        // XP/hr, session XP, stamina donated, runtime: whichever apply to what's running).
        public void SetText(string newState, string[] newLines, Color[] newColors, int newBoldLine)
        {
            Color c = newState.StartsWith("Running") ? Theme.Good
                : newState.StartsWith("Paused") || newState.StartsWith("Waiting") ? Theme.Warn
                : newState.StartsWith("Can't") || newState.StartsWith("No job") || newState.Contains("didn't") ? Theme.Danger
                : Theme.AccentSoft;
            if (newState == state && c == stateColor && newBoldLine == boldLine && string.Join("\n", newLines) == string.Join("\n", lines)) return;
            state = newState;
            stateColor = c;
            lines = newLines;
            lineColors = newColors;
            boldLine = newBoldLine;
            Invalidate();
        }

        // The status line gets the top third of the panel in large text; the other lines share the rest
        // in one smaller size. A bigger box gives bigger text.
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Grey);
            using (Pen border = new Pen(Theme.Accent, Math.Max(1, Height / 40)))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            int pad = Math.Max(3, Height / 16);
            float usable = Height - 2 * pad, room = Width - 2 * pad;
            float bigH = usable / 3f, smallH = (usable - bigH) / Math.Max(4, lines.Length);

            using (Font big = Fit(g, new[] { state }, FontStyle.Bold, bigH, room))
                Draw(g, state, big, stateColor, pad, pad, bigH);

            using (Font f = Fit(g, lines, FontStyle.Regular, smallH, room))
            using (Font bold = new Font(Family, f.Size, FontStyle.Bold, GraphicsUnit.Pixel))
                for (int i = 0; i < lines.Length; i++)
                    Draw(g, lines[i], i == boldLine ? bold : f, i < lineColors.Length ? lineColors[i] : Theme.Text, pad, pad + bigH + i * smallH, smallH);
        }

        // The largest font for this line height in which every one of these lines fits the width.
        static Font Fit(Graphics g, string[] lines, FontStyle style, float lineH, float room)
        {
            float size = Math.Max(4f, lineH * 0.78f);
            using (Font probe = new Font(Family, size, style, GraphicsUnit.Pixel))
                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    float w = g.MeasureString(line, probe).Width;
                    if (w > room) size = Math.Min(size, Math.Max(4f, lineH * 0.78f * room / w));
                }
            return new Font(Family, size, style, GraphicsUnit.Pixel);
        }

        static void Draw(Graphics g, string text, Font font, Color color, int x, float y, float lineH)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (SolidBrush b = new SolidBrush(color))
            {
                float h = g.MeasureString(text, font).Height;
                g.DrawString(text, font, b, x, y + (lineH - h) / 2);
            }
        }
    }
}

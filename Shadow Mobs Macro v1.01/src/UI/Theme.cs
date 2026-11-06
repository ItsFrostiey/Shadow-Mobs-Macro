using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StaminaDonator
{
    // The Shadow Mobs look: dark greys with purple accents, matching the family logo.
    public static class Theme
    {
        public static readonly Color Window = Color.FromArgb(0x16, 0x14, 0x1B);
        public static readonly Color Sidebar = Color.FromArgb(0x0F, 0x0D, 0x13);
        public static readonly Color Card = Color.FromArgb(0x22, 0x1F, 0x2A);
        public static readonly Color CardAlt = Color.FromArgb(0x2C, 0x28, 0x36);
        public static readonly Color Border = Color.FromArgb(0x3B, 0x34, 0x48);
        public static readonly Color Text = Color.FromArgb(0xEE, 0xEA, 0xF4);
        public static readonly Color Muted = Color.FromArgb(0xA8, 0xA0, 0xB8);
        public static readonly Color Accent = Color.FromArgb(0x8E, 0x3F, 0xE0);
        public static readonly Color AccentHover = Color.FromArgb(0xA4, 0x5E, 0xF0);
        public static readonly Color AccentSoft = Color.FromArgb(0xC9, 0xA6, 0xFF);
        public static readonly Color NavSelected = Color.FromArgb(0x2A, 0x1C, 0x3E);
        public static readonly Color NavHover = Color.FromArgb(0x1E, 0x18, 0x29);
        public static readonly Color Warn = Color.FromArgb(0xF0, 0xA8, 0x30);
        public static readonly Color WarnFill = Color.FromArgb(0x3A, 0x2A, 0x12);
        public static readonly Color Danger = Color.FromArgb(0xE8, 0x55, 0x55);
        public static readonly Color DangerFill = Color.FromArgb(0x3D, 0x16, 0x1C);
        public static readonly Color Good = Color.FromArgb(0x6C, 0xD8, 0x8A);
        public static readonly Color GoodFill = Color.FromArgb(0x16, 0x30, 0x22);

        // Windows display scaling (1.25 at 125%). Layouts are written for 100% and scaled by this.
        public static readonly float Dpi = ReadDpi();

        static float ReadDpi()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
        }

        public static int S(int pixels) { return (int)Math.Round(pixels * Dpi); }

        // Fonts already follow Windows scaling; this scales every size and position to match.
        public static void ScaleForm(Form form)
        {
            form.AutoScaleMode = AutoScaleMode.None;
            if (Dpi == 1f) return;
            form.Scale(new SizeF(Dpi, Dpi));   // also scales MaximumSize, so wrap widths follow
        }

        public static Font Ui(float size) { return new Font("Segoe UI", size); }
        public static Font Bold(float size) { return new Font("Segoe UI Semibold", size); }
        public static Font Heading(float size) { return new Font("Segoe UI", size, FontStyle.Bold); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Windows' own dark scrollbars (Windows 10 1809+), instead of bright white ones.
        public static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
        }

        // Dark title bar on Windows 10 (1809+) and 11. Harmless elsewhere.
        public static void DarkTitleBar(Form f)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch (DllNotFoundException) { }
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Button Primary(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(width, 40);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = Bold(10.5f);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button Secondary(string text, int width)
        {
            Button b = Primary(text, width);
            b.BackColor = CardAlt;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Border;
            b.ForeColor = Text;
            return b;
        }

        public static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            return l;
        }

        public static Label Wrapped(string text, int width, Color color)
        {
            Label l = Label(text, Ui(9.75f), color);
            l.MaximumSize = new Size(width, 0);
            return l;
        }
    }

    // Pictures built into the exe (the logo and calibration examples), so it runs on its own.
    public static class Assets
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        public static Image Get(string name)
        {
            Image img;
            if (cache.TryGetValue(name, out img)) return img;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".png"))
                img = s == null ? null : Image.FromStream(new MemoryStream(ReadAll(s)));
            cache[name] = img;
            return img;
        }

        static byte[] ReadAll(Stream s)
        {
            MemoryStream m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }
    }

    // A rounded panel with a title, for grouping controls.
    public class Card : Panel
    {
        public string Title;

        public Card(string title, int width, int height)
        {
            Title = title;
            Size = new Size(width, height);
            BackColor = Theme.Card;
            Padding = new Padding(18, title == null ? 16 : 48, 18, 14);
            Margin = new Padding(0, 0, 14, 14);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);   // behind the rounded corners
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Theme.S(10)))
            using (SolidBrush fill = new SolidBrush(Theme.Card))
            using (Pen border = new Pen(Theme.Border))
            {
                g.FillPath(fill, p);
                g.DrawPath(border, p);
            }
            if (Title == null) return;
            using (Font f = Theme.Heading(11.5f))
                TextRenderer.DrawText(g, Title.ToUpperInvariant(), f, new Point(Theme.S(18), Theme.S(14)), Theme.AccentSoft);
            using (SolidBrush accent = new SolidBrush(Theme.Accent))
                g.FillRectangle(accent, Theme.S(18), Theme.S(38), Theme.S(36), Theme.S(3));
        }
    }

    // A large notice across the top of a page, such as "not calibrated".
    public class Banner : Control
    {
        public string Heading = "", Body = "";
        public Color Stripe = Theme.Warn, Fill = Theme.WarnFill;
        public string Icon = "!";

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 90;
            Margin = new Padding(0, 0, 0, 14);
        }

        public void Set(string icon, string heading, string body, Color stripe, Color fill)
        {
            Icon = icon;
            Heading = heading;
            Body = body;
            Stripe = stripe;
            Fill = fill;
            Visible = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Theme.S(10)))
            using (SolidBrush fill = new SolidBrush(Fill))
            using (Pen border = new Pen(Stripe, 2))
            {
                g.FillPath(fill, p);
                g.DrawPath(border, p);
            }
            int d = Height - Theme.S(36);
            Rectangle circle = new Rectangle(Theme.S(18), (Height - d) / 2, d, d);
            using (SolidBrush b = new SolidBrush(Stripe)) g.FillEllipse(b, circle);
            using (Font f = Theme.Heading(d * 0.42f))
                TextRenderer.DrawText(g, Icon, f, circle, Theme.Sidebar, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = circle.Right + Theme.S(16);
            using (Font h = Theme.Heading(13f))
                TextRenderer.DrawText(g, Heading, h, new Rectangle(x, Theme.S(12), Width - x - Theme.S(14), Theme.S(30)), Stripe, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (Font b = Theme.Ui(9.75f))
                TextRenderer.DrawText(g, Body, b, new Rectangle(x, Theme.S(42), Width - x - Theme.S(14), Height - Theme.S(46)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    // One entry in the left-hand menu. Shows a warning dot when its feature needs calibrating, and
    // optionally a tickbox (whether that feature runs when Start is pressed). Clicking the tickbox
    // toggles it; clicking anywhere else opens the page.
    public class NavButton : Control
    {
        public bool Selected, Warning;
        public bool HasTick, Ticked;
        public event EventHandler PageClicked, TickChanged;
        bool hover;

        Rectangle TickBox
        {
            get { int s = Theme.S(24); return new Rectangle(Width - Theme.S(20) - s, (Height - s) / 2, s, s); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            Rectangle hit = Rectangle.Inflate(TickBox, Theme.S(8), Theme.S(8));
            if (HasTick && hit.Contains(e.Location))
            {
                Ticked = !Ticked;
                Invalidate();
                if (TickChanged != null) TickChanged(this, EventArgs.Empty);
            }
            else if (PageClicked != null) PageClicked(this, EventArgs.Empty);
        }

        public NavButton(string text)
        {
            Text = text;
            Height = 44;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            Font = Theme.Bold(10.5f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Selected ? Theme.NavSelected : hover ? Theme.NavHover : Theme.Sidebar);
            if (Selected)
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, Theme.S(6), Theme.S(4), Height - Theme.S(12));
            TextRenderer.DrawText(g, Text.ToUpperInvariant(), Font, new Rectangle(Theme.S(24), 0, Width - Theme.S(HasTick ? 100 : 60), Height),
                Selected ? Color.White : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int right = Width - Theme.S(16);
            if (HasTick)
            {
                Rectangle box = TickBox;
                using (Pen p = new Pen(Ticked ? Theme.Good : Color.White, Theme.S(2)))
                    g.DrawRectangle(p, box);
                if (Ticked)
                    using (Pen p = new Pen(Theme.Good, Theme.S(3)))
                        g.DrawLines(p, new[] { new Point(box.Left + box.Width / 5, box.Top + box.Height / 2), new Point(box.Left + box.Width * 2 / 5, box.Bottom - box.Height / 4), new Point(box.Right - box.Width / 5, box.Top + box.Height / 4) });
                right = box.Left - Theme.S(8);
            }
            if (Warning)
            {
                Rectangle dot = new Rectangle(right - Theme.S(20), (Height - Theme.S(20)) / 2, Theme.S(20), Theme.S(20));
                using (SolidBrush b = new SolidBrush(Theme.Warn)) g.FillEllipse(b, dot);
                using (Font f = Theme.Heading(9f))
                    TextRenderer.DrawText(g, "!", f, dot, Theme.Sidebar, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}

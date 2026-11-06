using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StaminaDonator
{
    // One thing to mark during calibration. Snap tightens the user's rough box onto the real
    // thing, or throws CalibrationException with what to do instead. It also sees earlier marks.
    public class CalibrationStep
    {
        public string Key, Title, Help;
        public string Why = "";        // shown so the user knows what the tool uses it for
        public string Warning = "";    // shown in a big amber box when a mistake here is easy and costly
        public string Example;         // built-in picture name (see Assets), or null
        public Func<Frame, Rectangle, Dictionary<string, Rectangle>, Rectangle> Snap;

        // Fills in Why and Example for a feature's steps: pictures are named <feature>_<key>.
        public static CalibrationStep[] Describe(string feature, CalibrationStep[] steps, Dictionary<string, string> why)
        {
            foreach (CalibrationStep s in steps)
            {
                string w;
                if (why.TryGetValue(s.Key, out w)) s.Why = w;
                s.Example = feature + "_" + s.Key;
            }
            return steps;
        }

        public CalibrationStep(string key, string title, string help, Func<Frame, Rectangle, Dictionary<string, Rectangle>, Rectangle> snap)
        {
            Key = key;
            Title = title;
            Help = help;
            Snap = snap;
        }
    }

    // Shows a screenshot of the game and asks the user to drag a box around each thing in turn.
    // Scroll the mouse wheel to zoom in on small things.
    public class CalibrationForm : Form
    {
        readonly Frame frame;
        readonly Bitmap image;
        readonly CalibrationStep[] steps;
        readonly Label counter, title, help, why, error;
        readonly Banner warning;
        readonly Button back;
        readonly Canvas canvas;
        readonly PictureBox example;
        readonly Label[] checklist;
        public readonly Dictionary<string, Rectangle> Marks = new Dictionary<string, Rectangle>();
        int step;

        float zoom = 1;          // on top of fitting the screenshot to the window
        PointF pan;              // frame pixel shown at the canvas's top-left
        PointF? dragFrom;                // where the box started, in screenshot pixels
        readonly Timer edgeTimer = new Timer { Interval = 30 };
        Point dragTo;

        public CalibrationForm(string heading, Frame frame, CalibrationStep[] steps)
        {
            this.frame = frame;
            this.steps = steps;
            image = frame.ToBitmap();
            Text = heading;
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            
            
            Image logo = Assets.Get("logo");
            if (logo != null) Icon = Icon.FromHandle(new Bitmap(logo, 64, 64).GetHicon());
            HandleCreated += delegate { Theme.DarkTitleBar(this); };

            // Top: what to do now, and why.
            // Grows to fit: a step with a warning gets a big amber box under its instructions.
            Panel top = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 150), BackColor = Theme.Sidebar };
            PictureBox mark = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Location = new Point(18, 16), BackColor = Color.Transparent };
            FlowLayoutPanel text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(126, 12), Padding = new Padding(0, 0, 0, 12), BackColor = Theme.Sidebar };
            counter = Theme.Label("", Theme.Bold(10f), Theme.AccentSoft);
            title = Theme.Label("", Theme.Heading(18f), Color.White);
            help = Theme.Label("", Theme.Ui(11f), Theme.Text);
            help.Margin = new Padding(3, 4, 3, 4);
            why = Theme.Label("", Theme.Ui(9.75f), Theme.Muted);
            warning = new Banner { Height = 112, Width = 700 };
            warning.Margin = new Padding(3, 8, 3, 4);
            error = Theme.Label("", Theme.Bold(10.5f), Theme.Danger);
            error.Margin = new Padding(3, 6, 3, 0);
            text.Controls.AddRange(new Control[] { counter, title, help, why, warning, error });
            back = Theme.Secondary("Back", 110);
            Button cancel = Theme.Secondary("Cancel", 110);
            cancel.DialogResult = DialogResult.Cancel;
            back.Click += delegate { if (step > 0) { step--; Marks.Remove(steps[step].Key); ShowStep(); } };
            CancelButton = cancel;
            top.Controls.AddRange(new Control[] { mark, text, back, cancel });
            Resize += delegate
            {
                cancel.Location = new Point(ClientSize.Width - Theme.S(130), Theme.S(20));
                back.Location = new Point(ClientSize.Width - Theme.S(250), Theme.S(20));
                int width = Math.Max(Theme.S(300), ClientSize.Width - Theme.S(126 + 290));
                help.MaximumSize = new Size(width, 0);
                why.MaximumSize = new Size(width, 0);
                error.MaximumSize = new Size(width, 0);
                warning.Width = Math.Min(width, Theme.S(1100));
            };

            // Right: the example picture and the list of steps.
            Panel side = new Panel { Dock = DockStyle.Right, Width = 440, BackColor = Theme.Card, Padding = new Padding(18) };
            Label exampleHeading = Theme.Label("EXAMPLE", Theme.Heading(11.5f), Theme.AccentSoft);
            exampleHeading.Location = new Point(18, 16);
            Label exampleNote = Theme.Wrapped("Drag a box around the part in the green box. It only has to be roughly right: the tool tightens it.", 400, Theme.Muted);
            exampleNote.Location = new Point(18, 44);
            example = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(18, 92), Size = new Size(404, 260), BackColor = Theme.Window };
            Label stepsHeading = Theme.Label("STEPS", Theme.Heading(11.5f), Theme.AccentSoft);
            stepsHeading.Location = new Point(18, 370);
            side.Controls.AddRange(new Control[] { exampleHeading, exampleNote, example, stepsHeading });
            checklist = new Label[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                checklist[i] = Theme.Label("", Theme.Ui(10f), Theme.Muted);
                checklist[i].Location = new Point(20, 400 + i * 26);
                side.Controls.Add(checklist[i]);
            }
            Label tip = Theme.Wrapped("Mouse wheel zooms in. Right-drag or the arrow keys move the view. While drawing a box, hold the mouse at an edge to slide the view that way.", 400, Theme.Muted);
            tip.Location = new Point(18, 410 + steps.Length * 26);
            side.Controls.Add(tip);

            canvas = new Canvas();
            canvas.Dock = DockStyle.Fill;
            canvas.BackColor = Color.FromArgb(10, 9, 13);
            canvas.Paint += PaintCanvas;
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += CanvasUp;
            canvas.MouseWheel += CanvasWheel;
            canvas.Resize += delegate { canvas.Invalidate(); };
            edgeTimer.Tick += delegate { EdgeScroll(); };

            Controls.Add(canvas);
            Controls.Add(side);
            Controls.Add(top);
            Theme.ScaleForm(this);
            ShowStep();
        }

        class Canvas : Panel
        {
            public Canvas() { DoubleBuffered = true; }
        }

        float ViewScale
        {
            get
            {
                float fit = Math.Min((float)canvas.Width / frame.W, (float)canvas.Height / frame.H);
                return Math.Max(0.05f, fit) * zoom;
            }
        }

        PointF ToFrame(Point p) { return new PointF(pan.X + p.X / ViewScale, pan.Y + p.Y / ViewScale); }
        PointF ToCanvas(PointF p) { return new PointF((p.X - pan.X) * ViewScale, (p.Y - pan.Y) * ViewScale); }

        RectangleF ToCanvas(Rectangle r)
        {
            PointF a = ToCanvas(new PointF(r.Left, r.Top));
            return new RectangleF(a.X, a.Y, r.Width * ViewScale, r.Height * ViewScale);
        }

        void ShowStep()
        {
            back.Enabled = step > 0;
            error.Text = "";
            if (step >= steps.Length)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            CalibrationStep s = steps[step];
            counter.Text = string.Format("STEP {0} OF {1}", step + 1, steps.Length);
            title.Text = (step + 1) + ". " + s.Title;
            help.Text = s.Help;
            why.Text = s.Why.Length > 0 ? "Why: " + s.Why : "";
            if (s.Warning.Length > 0) warning.Set("!", "IMPORTANT", s.Warning, Theme.Warn, Theme.WarnFill);
            else warning.Visible = false;
            example.Image = s.Example == null ? null : Assets.Get(s.Example);
            for (int i = 0; i < steps.Length; i++)
            {
                checklist[i].Text = (i < step ? "✔  " : i == step ? "▶  " : "      ") + (i + 1) + ". " + steps[i].Title;
                checklist[i].ForeColor = i < step ? Theme.Good : i == step ? Color.White : Theme.Muted;
                checklist[i].Font = i == step ? Theme.Bold(10f) : Theme.Ui(10f);
            }
            canvas.Invalidate();
        }

        Point panFrom;
        PointF panStart;
        bool panning;

        void CanvasDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { panning = true; panFrom = e.Location; panStart = pan; return; }
            if (e.Button != MouseButtons.Left) return;
            dragFrom = ToFrame(e.Location);   // kept in screenshot pixels, so it stays put while the view moves
            dragTo = e.Location;
            edgeTimer.Start();
        }

        // While a box is being dragged, holding the mouse near (or past) an edge of the picture slides
        // the view that way, faster the further out it is. So a long thing like the scrollbar can be
        // boxed while zoomed in.
        void EdgeScroll()
        {
            if (dragFrom == null) { edgeTimer.Stop(); return; }
            Point m = canvas.PointToClient(Cursor.Position);
            int margin = Theme.S(48);
            float dx = 0, dy = 0;
            if (m.X < margin) dx = m.X - margin;
            else if (m.X > canvas.Width - margin) dx = m.X - (canvas.Width - margin);
            if (m.Y < margin) dy = m.Y - margin;
            else if (m.Y > canvas.Height - margin) dy = m.Y - (canvas.Height - margin);
            if (dx == 0 && dy == 0) return;
            float limit = margin * 3;
            dx = Math.Max(-limit, Math.Min(limit, dx));
            dy = Math.Max(-limit, Math.Min(limit, dy));
            PanBy(dx * 0.5f, dy * 0.5f);
            dragTo = m;
        }

        // Moves the view by this many screen pixels, without going far past the picture's edges.
        void PanBy(float canvasDx, float canvasDy)
        {
            float s = ViewScale;
            float maxX = Math.Max(0, frame.W - canvas.Width / s), maxY = Math.Max(0, frame.H - canvas.Height / s);
            pan = new PointF(Math.Max(0, Math.Min(maxX, pan.X + canvasDx / s)), Math.Max(0, Math.Min(maxY, pan.Y + canvasDy / s)));
            canvas.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            int step = Theme.S(120);
            switch (keys)
            {
                case Keys.Left: PanBy(-step, 0); return true;
                case Keys.Right: PanBy(step, 0); return true;
                case Keys.Up: PanBy(0, -step); return true;
                case Keys.Down: PanBy(0, step); return true;
            }
            return base.ProcessCmdKey(ref msg, keys);
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                pan = new PointF(panStart.X - (e.X - panFrom.X) / ViewScale, panStart.Y - (e.Y - panFrom.Y) / ViewScale);
                canvas.Invalidate();
                return;
            }
            if (dragFrom == null) return;
            dragTo = e.Location;
            canvas.Invalidate();
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            if (panning) { panning = false; return; }
            if (dragFrom == null || e.Button != MouseButtons.Left) return;
            PointF a = dragFrom.Value, b = ToFrame(e.Location);
            dragFrom = null;
            edgeTimer.Stop();
            Rectangle raw = Rectangle.FromLTRB((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y)));
            try
            {
                Marks[steps[step].Key] = steps[step].Snap(frame, raw, Marks);
                step++;
                ShowStep();
            }
            catch (CalibrationException ex)
            {
                error.Text = ex.Message;
                canvas.Invalidate();
            }
        }

        void CanvasWheel(object sender, MouseEventArgs e)
        {
            PointF under = ToFrame(e.Location);
            zoom = Math.Max(1f, Math.Min(12f, zoom * (e.Delta > 0 ? 1.25f : 0.8f)));
            pan = new PointF(under.X - e.X / ViewScale, under.Y - e.Y / ViewScale);
            if (zoom == 1f) pan = PointF.Empty;
            canvas.Invalidate();
        }

        void PaintCanvas(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = zoom > 2 ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, ToCanvas(new Rectangle(0, 0, frame.W, frame.H)));
            using (Pen done = new Pen(Theme.Good, 2))
                foreach (Rectangle r in Marks.Values) g.DrawRectangle(done, Rect(ToCanvas(r)));
            if (dragFrom != null)
            {
                PointF a = ToCanvas(dragFrom.Value);
                using (Pen p = new Pen(Theme.AccentSoft, 2))
                    g.DrawRectangle(p, Rectangle.FromLTRB((int)Math.Min(a.X, dragTo.X), (int)Math.Min(a.Y, dragTo.Y),
                        (int)Math.Max(a.X, dragTo.X), (int)Math.Max(a.Y, dragTo.Y)));
            }
        }

        static Rectangle Rect(RectangleF r) { return Rectangle.Round(r); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image.Dispose();
            base.Dispose(disposing);
        }
    }
}

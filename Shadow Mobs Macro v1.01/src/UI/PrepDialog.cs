using System;
using System.Drawing;
using System.Windows.Forms;

namespace StaminaDonator
{
    // A recommendation shown before starting, with "Don't show this again".
    public class TipDialog : Form
    {
        public bool DontShowAgain { get { return dont.Checked; } }
        readonly CheckBox dont;

        public TipDialog(string heading, string intro, string[] tips, string goText)
        {
            Text = "Shadow Mobs Macro";
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            HandleCreated += delegate { Theme.DarkTitleBar(this); };

            Banner banner = new Banner { Location = new Point(20, 18), Size = new Size(600, 90) };
            banner.Set("!", heading, intro, Theme.Warn, Theme.WarnFill);
            Controls.Add(banner);

            int y = 124;
            foreach (string tip in tips)
            {
                Label dot = Theme.Label("•", Theme.Bold(11f), Theme.AccentSoft);
                dot.Location = new Point(26, y);
                Label text = Theme.Wrapped(tip, 570, Theme.Text);
                text.Location = new Point(44, y + 1);
                Controls.Add(dot);
                Controls.Add(text);
                y += Math.Max(28, text.PreferredHeight + 10);
            }

            dont = new CheckBox { Text = "Don't show this again", AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Window, Location = new Point(24, y + 14), Cursor = Cursors.Hand };
            Button go = Theme.Primary(goText, 150);
            Button cancel = Theme.Secondary("Cancel", 110);
            go.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = go;
            CancelButton = cancel;
            go.Location = new Point(620 - 150, y + 6);
            cancel.Location = new Point(620 - 150 - 122, y + 6);
            Controls.AddRange(new Control[] { dont, go, cancel });
            ClientSize = new Size(640, y + 64);
            Theme.ScaleForm(this);
        }
    }

    // Shown before a calibration screenshot. Every item must be ticked before Continue unlocks, so the
    // game is set up properly first (right page, nothing collapsed, scrolled to the top...).
    public class PrepDialog : Form
    {
        public PrepDialog(string heading, string example, string[] items)
        {
            Text = heading;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 330 + items.Length * 52);
            HandleCreated += delegate { Theme.DarkTitleBar(this); };

            Label title = Theme.Label("BEFORE YOU START", Theme.Heading(15f), Color.White);
            title.Location = new Point(24, 18);
            Label intro = Theme.Wrapped("Get the game ready like the picture, then tick each item. When you press Continue, the tool switches to the game and takes a screenshot 3 seconds later.", 592, Theme.Muted);
            intro.Location = new Point(26, 54);
            PictureBox pic = new PictureBox { Image = Assets.Get(example), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(24, 100), Size = new Size(592, 200), BackColor = Theme.Sidebar };
            Controls.AddRange(new Control[] { title, intro, pic });

            Button go = Theme.Primary("Continue", 140);
            Button cancel = Theme.Secondary("Cancel", 120);
            go.Enabled = false;
            go.BackColor = Theme.CardAlt;
            go.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;

            CheckBox[] boxes = new CheckBox[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                CheckBox c = new CheckBox
                {
                    Text = items[i], Location = new Point(28, 316 + i * 52), Size = new Size(588, 46),
                    Font = Theme.Bold(10.5f), ForeColor = Theme.Text, BackColor = Theme.Window, Cursor = Cursors.Hand
                };
                c.CheckedChanged += delegate
                {
                    bool all = true;
                    foreach (CheckBox b in boxes) all &= b.Checked;
                    go.Enabled = all;
                    go.BackColor = all ? Theme.Accent : Theme.CardAlt;
                };
                boxes[i] = c;
                Controls.Add(c);
            }

            int y = 316 + items.Length * 52 + 2;
            go.Location = new Point(616 - 140, y);
            cancel.Location = new Point(616 - 140 - 132, y);
            Controls.Add(go);
            Controls.Add(cancel);
            ClientSize = new Size(640, y + 58);
            Theme.ScaleForm(this);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StaminaDonator
{
    // The main window: a menu on the left with the family logo, one page per feature (each with its
    // own calibration card, so nothing needs scrolling), and the status and log along the bottom.
    public class MainForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int WM_HOTKEY = 0x0312, StopKey = 0x77;   // F8
        const int PageWidth = 816;

        readonly Settings settings = Settings.Load();
        ITextReader reader;
        CancellationTokenSource cancel;
        Thread worker;

        readonly Panel pageHost = new Panel { Dock = DockStyle.Fill };
        readonly Label pageTitle = Theme.Label("", Theme.Heading(20f), Color.White);
        readonly Label pageSubtitle = Theme.Label("", Theme.Ui(10f), Theme.Muted);
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        readonly Dictionary<string, NavButton> nav = new Dictionary<string, NavButton>();
        readonly Dictionary<string, string[]> titles = new Dictionary<string, string[]>();

        readonly Label status = Theme.Label("Ready", Theme.Bold(10.5f), Theme.Text);
        readonly TextBox logBox = new TextBox();
        readonly Label gameLabel = Theme.Label("", Theme.Ui(9.5f), Theme.Muted);
        readonly Button startButton = Theme.Primary("START", 200);
        readonly Banner staminaBanner = new Banner(), jobsBanner = new Banner(), bankBanner = new Banner();
        readonly Label bankCal = Theme.Label("", Theme.Bold(10.5f), Theme.Text);
        readonly Banner petBanner = new Banner();
        readonly Label petCal = Theme.Label("", Theme.Bold(10.5f), Theme.Text);
        volatile PetBot petBot;          // the running pet trainer, for the overlay
        readonly Overlay overlay = new Overlay();
        volatile JobBot jobBot;          // the running auto jobs bot, for the overlay
        volatile StaminaBot staminaBot;  // the running stamina donator, for the overlay
        volatile Scheduler scheduler;    // the running scheduler, for the overlay's status line
        DateTime runStartedAt;
        volatile bool paused;            // paused while the user uses the PC
        Rectangle overlayArea;
        readonly Label staminaCal = Theme.Label("", Theme.Bold(10.5f), Theme.Text);
        readonly Label jobsCal = Theme.Label("", Theme.Bold(10.5f), Theme.Text);
        CheckedListBox perkList;

        public MainForm()
        {
            Text = "Shadow Mobs Macro v" + App.Version;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            
            
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;
            string icon = App.PathOf("icon.ico");
            if (File.Exists(icon)) Icon = new Icon(icon);

            // Main area: page title, the page, then status and log.
            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16) };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 66 };
            pageTitle.Location = new Point(0, 0);
            pageSubtitle.Location = new Point(3, 38);
            header.Controls.Add(pageTitle);
            header.Controls.Add(pageSubtitle);
            main.Controls.Add(pageHost);
            main.Controls.Add(BottomArea());
            main.Controls.Add(header);

            Controls.Add(main);
            Controls.Add(SidebarArea());

            AddPage("stamina", "Stamina Donator", "Donates stamina to your family perks, in your order, until each reaches level 25.", StaminaPage());
            AddPage("jobs", "Auto Jobs", "Masters every job, or keeps doing the job with the most XP per energy.", JobsPage());
            AddPage("pet", "Auto-Train Pet", "Trains your active animal every 20 minutes.", PetPage());
            AddPage("deposit", "Auto Deposit", "Keeps your cash safe in the bank.", DepositPage());
            AddPage("settings", "Settings", "Options that apply to every feature.", SettingsPage());
            ShowPage("stamina");

            Log.Written += delegate(string m) { Ui(delegate { AppendLog(m); }); };
            HandleCreated += delegate { Theme.DarkTitleBar(this); };
            Load += delegate { RefreshCalibration(); };
            Activated += delegate { RefreshCalibration(); };
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate { RefreshCalibration(); };
            timer.Start();
            System.Windows.Forms.Timer overlayTimer = new System.Windows.Forms.Timer { Interval = 300 };
            overlayTimer.Tick += delegate { UpdateOverlay(); };
            overlayTimer.Start();
            Theme.ScaleForm(this);
        }

        // ---------- Frame ----------

        Control SidebarArea()
        {
            Panel side = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = Theme.Sidebar };
            // Five menu entries plus CALIBRATE and START fit in the window's smallest height (about 640).
            PictureBox logo = new PictureBox { Image = Assets.Get("logo"), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Top, Height = 184, Padding = new Padding(20, 14, 20, 4) };
            Panel spacer = new Panel { Dock = DockStyle.Top, Height = 12 };
            Panel foot = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(22, 8, 12, 12) };
            gameLabel.Location = new Point(22, 10);
            Label f8 = Theme.Label("F8 stops everything", Theme.Ui(9.5f), Theme.Muted);
            f8.Location = new Point(22, 36);
            foot.Controls.Add(gameLabel);
            foot.Controls.Add(f8);

            // The big START button: runs every ticked feature (both together if both are ticked).
            Panel startArea = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(18, 10, 18, 10) };
            startButton.Dock = DockStyle.Fill;
            startButton.Click += delegate { if (worker != null) Stop(); else StartSelected(); };
            startArea.Controls.Add(startButton);
            SetStartLook(false);

            // CALIBRATE: every feature's calibration, one after another.
            Panel calibrateArea = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(18, 8, 18, 2) };
            Button calibrate = Theme.Primary("CALIBRATE", 200);
            calibrate.Dock = DockStyle.Fill;
            calibrate.Font = Theme.Heading(12f);
            calibrate.Click += delegate { CalibrateAll(); };
            calibrateArea.Controls.Add(calibrate);

            // Docked to the top in reverse, so they appear in this order.
            string[][] items = { new[] { "stamina", "Stamina Donator" }, new[] { "jobs", "Auto Jobs" }, new[] { "pet", "Auto-Train Pet" }, new[] { "deposit", "Auto Deposit" }, new[] { "settings", "Settings" } };
            for (int i = items.Length - 1; i >= 0; i--)
            {
                string key = items[i][0];
                NavButton b = new NavButton(items[i][1]);
                b.PageClicked += delegate { ShowPage(key); };
                nav[key] = b;
                side.Controls.Add(b);
            }
            // Tickboxes: which features Start runs.
            nav["stamina"].HasTick = true;
            nav["stamina"].Ticked = settings.UseStamina;
            nav["stamina"].TickChanged += delegate { settings.UseStamina = nav["stamina"].Ticked; settings.Save(); };
            nav["jobs"].HasTick = true;
            nav["jobs"].Ticked = settings.UseJobs;
            nav["jobs"].TickChanged += delegate { settings.UseJobs = nav["jobs"].Ticked; settings.Save(); };
            nav["pet"].HasTick = true;
            nav["pet"].Ticked = settings.UsePet;
            nav["pet"].TickChanged += delegate { settings.UsePet = nav["pet"].Ticked; settings.Save(); };
            nav["deposit"].HasTick = true;
            nav["deposit"].Ticked = settings.UseBank;
            nav["deposit"].TickChanged += delegate { settings.UseBank = nav["deposit"].Ticked; settings.Save(); };

            side.Controls.Add(spacer);
            side.Controls.Add(logo);
            side.Controls.Add(calibrateArea);   // added before START so it sits just above it
            side.Controls.Add(startArea);
            side.Controls.Add(foot);
            return side;
        }

        Control BottomArea()
        {
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 160, Padding = new Padding(0, 6, 0, 0) };
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 48 };
            // Just the status; starting and stopping is the big button in the menu (or F8).
            status.Location = new Point(2, 14);
            bar.Controls.Add(status);

            Card logCard = new Card(null, 100, 100);
            logCard.Dock = DockStyle.Fill;
            logCard.Margin = Padding.Empty;
            logCard.Padding = new Padding(14, 10, 6, 10);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Theme.Card;
            logBox.ForeColor = Theme.Muted;
            logBox.Font = new Font("Consolas", 9.5f);
            logBox.Dock = DockStyle.Fill;
            logBox.HandleCreated += delegate { Theme.DarkScrollbars(logBox); };
            logCard.Controls.Add(logBox);

            bottom.Controls.Add(logCard);
            bottom.Controls.Add(bar);
            return bottom;
        }

        void AddPage(string key, string title, string subtitle, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pages[key] = page;
            titles[key] = new[] { title, subtitle };
            pageHost.Controls.Add(page);
        }

        void ShowPage(string key)
        {
            foreach (KeyValuePair<string, Control> p in pages) p.Value.Visible = p.Key == key;
            foreach (KeyValuePair<string, NavButton> n in nav) { n.Value.Selected = n.Key == key; n.Value.Invalidate(); }
            pageTitle.Text = titles[key][0];
            pageSubtitle.Text = titles[key][1];
        }

        static FlowLayoutPanel PageColumn()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Padding.Empty };
        }

        static FlowLayoutPanel CardRow()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        }

        // The card at the bottom of each feature page: status, what to have on screen, and buttons.
        // Calibrating is done for every feature at once with CALIBRATE in the menu; this card shows the
        // status, what the game should look like, and Check screen.
        Card CalibrationCard(string example, Label state, string instructions, Action check)
        {
            Card c = new Card("Calibration", PageWidth, 146);
            PictureBox pic = new PictureBox { Image = Assets.Get(example), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(160, 84), Location = new Point(18, 50), BackColor = Theme.Window };
            state.Location = new Point(194, 48);
            Label how = Theme.Wrapped(instructions, 320, Theme.Muted);
            how.Location = new Point(194, 74);
            Button chk = Theme.Secondary("Check screen", 150);
            chk.Location = new Point(PageWidth - 282, 48);
            chk.Click += delegate { check(); };
            // Button colours and a few small pictures are learned from the screen, so they only match
            // the theme that was on when calibrating.
            Label note = Theme.Wrapped("Calibrate in the theme you play with. If you change theme, calibrate again.", 264, Theme.Warn);
            ToolTip tips = new ToolTip();
            tips.SetToolTip(chk, "Reports what the tool can see, without clicking anything.");
            note.Location = new Point(PageWidth - 282, 96);
            c.Controls.AddRange(new Control[] { pic, state, how, chk, note });
            return c;
        }

        // ---------- Stamina page ----------

        Control StaminaPage()
        {
            FlowLayoutPanel page = PageColumn();
            staminaBanner.Width = PageWidth;
            page.Controls.Add(staminaBanner);

            FlowLayoutPanel row = CardRow();
            Card donate = new Card("Donation", 401, 196);
            Label give = Theme.Label("Give per click", Theme.Ui(10f), Theme.Text);
            give.Location = new Point(18, 54);
            RadioButton one = Radio("1 stamina", settings.GiveAmount == 1, new Point(150, 52));
            RadioButton five = Radio("5 stamina", settings.GiveAmount == 5, new Point(260, 52));
            one.CheckedChanged += delegate { settings.GiveAmount = one.Checked ? 1 : 5; settings.Save(); };
            Label reserve = Theme.Label("Keep in reserve", Theme.Ui(10f), Theme.Text);
            reserve.Location = new Point(18, 90);
            NumericUpDown reserveBox = Number(settings.KeepReserve, 0, 500, delegate(int v) { settings.KeepReserve = v; });
            reserveBox.Location = new Point(150, 87);
            Label max = Theme.Label("Max stamina", Theme.Ui(10f), Theme.Text);
            max.Location = new Point(18, 124);
            NumericUpDown maxBox = Number(settings.MaxStamina, 1, 5000, delegate(int v) { settings.MaxStamina = v; });
            maxBox.Location = new Point(150, 121);
            // "Level all perks to N first": every ticked perk reaches N (in order) before any goes past it.
            Label capLabel = Theme.Label("Level all perks to", Theme.Ui(10f), Theme.Text);
            capLabel.Location = new Point(18, 158);
            NumericUpDown capBox = Number(settings.PerkLevelCap, 0, 25, delegate(int v) { settings.PerkLevelCap = v; });
            capBox.Location = new Point(150, 155);
            Label capNote = Theme.Label("first, then to 25 (0 = off)", Theme.Ui(9.5f), Theme.Muted);
            capNote.Location = new Point(238, 159);
            donate.Controls.AddRange(new Control[] { give, one, five, reserve, reserveBox, max, maxBox, capLabel, capBox, capNote });

            Card order = new Card("Perk order", 401, 196);
            perkList = new CheckedListBox { Location = new Point(18, 52), Size = new Size(230, 132), CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = Theme.CardAlt, ForeColor = Theme.Text, Font = Theme.Ui(10.5f) };
            foreach (string name in settings.PerkOrder) perkList.Items.Add(name, !settings.PerksOff.Contains(name));
            perkList.HandleCreated += delegate { Theme.DarkScrollbars(perkList); };
            perkList.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                string name = (string)perkList.Items[e.Index];
                if (e.NewValue == CheckState.Checked) settings.PerksOff.Remove(name); else settings.PerksOff.Add(name);
                settings.Save();
            };
            Button up = Theme.Secondary("Move up", 126);
            Button down = Theme.Secondary("Move down", 126);
            up.Location = new Point(264, 52);
            down.Location = new Point(264, 100);
            up.Click += delegate { MovePerk(-1); };
            down.Click += delegate { MovePerk(1); };
            Label tip = Theme.Wrapped("Top first. Untick a perk to skip it.", 130, Theme.Muted);
            tip.Location = new Point(264, 146);
            order.Controls.AddRange(new Control[] { perkList, up, down, tip });
            order.Margin = Padding.Empty;

            row.Controls.Add(donate);
            row.Controls.Add(order);
            page.Controls.Add(row);
            Card cal = CalibrationCard("stamina_page", staminaCal, StaminaSetup.Instructions, CheckStamina);
            cal.Margin = new Padding(0, 12, 0, 0);
            page.Controls.Add(cal);
            return page;
        }

        void MovePerk(int by)
        {
            int i = perkList.SelectedIndex, j = i + by;
            if (i < 0 || j < 0 || j >= perkList.Items.Count) return;
            string name = (string)perkList.Items[i];
            bool on = perkList.GetItemChecked(i);
            perkList.Items.RemoveAt(i);
            perkList.Items.Insert(j, name);
            perkList.SetItemChecked(j, on);
            perkList.SelectedIndex = j;
            settings.PerkOrder.Clear();
            foreach (object item in perkList.Items) settings.PerkOrder.Add((string)item);
            settings.Save();
        }

        // START: runs whichever features are ticked in the menu, in priority order (see Scheduler):
        // jobs when there's energy, then the pet when its training is due, then stamina donations, and
        // auto deposit the rest of the time. Each ticked feature must be calibrated for this screen first.
        void StartSelected()
        {
            bool useStamina = settings.UseStamina, useJobs = settings.UseJobs, usePet = settings.UsePet, useBank = settings.UseBank;
            if (!useStamina && !useJobs && !usePet && !useBank) { Log.Write("Tick a feature in the menu, then press START."); return; }
            Size size = GameWindow.DetectSize();
            if (size.IsEmpty) { Log.Write("Roblox isn't open (or is minimised)."); return; }

            Profile staminaProfile = null, jobsProfile = null, petProfile = null, bankProfile = null;
            if (useStamina && (staminaProfile = NeedCalibration(StaminaSetup.Feature, "Stamina Donator", "stamina", size)) == null) return;
            if (useJobs && (jobsProfile = NeedCalibration(JobsSetup.Feature, "Auto Jobs", "jobs", size)) == null) return;
            if (usePet && (petProfile = NeedCalibration(PetSetup.Feature, "Auto-Train Pet", "pet", size)) == null) return;
            if (useBank && (bankProfile = NeedCalibration(BankSetup.Feature, "Auto Deposit", "deposit", size)) == null) return;
            if (!EnsureReader()) return;

            StaminaVision staminaVision = null;
            JobsVision jobsVision = null;
            PetVision petVision = null;
            List<JobInfo> jobs = null;
            try
            {
                if (useStamina) staminaVision = new StaminaVision(staminaProfile, reader);
                if (useJobs)
                {
                    jobs = JobCatalog.Load();
                    jobsVision = new JobsVision(jobsProfile, reader, jobs);
                }
                if (usePet) petVision = new PetVision(petProfile, reader);
            }
            catch (InvalidOperationException e) { Log.Write(e.Message); return; }
            if (useStamina && useJobs && (staminaVision.FamilyButton == null || jobsVision.JobsButton == null))
                Log.Write("Tip: calibrate both again so they learn the FAMILY and JOBS menu buttons; switching pages is more reliable that way.");

            if (!settings.HideNotificationTip)
                using (TipDialog tip = new TipDialog("TURN OFF NOTIFICATIONS FIRST",
                    "Popups that appear over the game can cover the buttons and numbers the tool reads, so clicks get missed.",
                    new[]
                    {
                        "Windows: turn on Do not disturb (click the bell or date at the bottom right of the taskbar), so no toasts pop up over the game.",
                        "Roblox: turn off notifications in the Roblox menu (Settings), and close chat, so friend and chat popups don't cover the game.",
                        "Other apps: quit or mute anything that shows popups over games, like Discord, Steam or game overlays."
                    }, "Start"))
                {
                    if (tip.ShowDialog(this) != DialogResult.OK) return;
                    if (tip.DontShowAgain) { settings.HideNotificationTip = true; settings.Save(); }
                }

            overlayArea = OverlaySetup.Area(size);
            List<string> names = new List<string>();
            if (useStamina) names.Add("donating stamina");
            if (useJobs) names.Add("doing jobs");
            if (usePet) names.Add("training the pet");
            if (useBank) names.Add("depositing cash");
            string what = names.Count == 1 ? char.ToUpper(names[0][0]) + names[0].Substring(1) : "Running: " + string.Join(", ", names);
            StartWorker(what, size, delegate(GameWindow game)
            {
                Action<string> show = delegate(string m) { Ui(delegate { status.Text = m; }); };
                StaminaBot staminaBot = null;
                JobBot jobsBot = null;
                PetBot petBot = null;
                BankBot bankBot = null;
                if (useStamina) { staminaBot = new StaminaBot(game, reader, staminaVision, settings); staminaBot.Status = show; this.staminaBot = staminaBot; }
                if (useJobs) { jobsBot = new JobBot(game, reader, jobsVision, jobs, settings); jobsBot.Status = show; jobBot = jobsBot; }
                if (usePet) { petBot = new PetBot(game, petVision, settings); petBot.Status = show; this.petBot = petBot; }
                if (useBank) { bankBot = new BankBot(game, reader, bankProfile, settings); bankBot.Status = show; }
                Scheduler run = new Scheduler(game, reader, jobsBot, staminaBot, staminaVision, petBot, bankBot, settings);
                scheduler = run;

                while (!run.Done)
                {
                    int wait;
                    try { wait = run.Tick(); }
                    catch (UserActiveException) { wait = 500; }
                    if (!run.Done) game.Sleep(wait);
                }
            });
        }

        // The calibration for a ticked feature, or null (with the reason logged and its page opened).
        Profile NeedCalibration(string feature, string name, string page, Size size)
        {
            Profile p = Profile.Load(feature, size.Width, size.Height);
            if (p == null)
            {
                Log.Write(name + " is ticked but isn't calibrated for " + size.Width + "x" + size.Height + " yet. Press CALIBRATE in the menu first.");
                ShowPage(page);
                return null;
            }
            if (p.ScaledFrom != null) Log.Write(name + ": using the " + p.ScaledFrom + " calibration scaled to this screen. If it misbehaves, calibrate on this screen.");
            return p;
        }

        // CALIBRATE (menu): walks through every feature in turn: Stamina Donator, Auto Jobs, Auto-Train Pet,
        // Auto Deposit. Each starts with its checklist; Cancel on a checklist (or calibration) skips just
        // that feature. Built back to front, since each one starts the next when it's done or skipped.
        void CalibrateAll()
        {
            if (Busy() || !EnsureReader()) return;
            List<JobInfo> jobs = null;
            try { jobs = JobCatalog.Load(); }
            catch (InvalidOperationException e) { Log.Write(e.Message + " Auto Jobs will be skipped."); }

            Action bank = delegate
            {
                CalibrateOne("Auto Deposit", 4, "bank_page", BankSetup.Checklist,
                    delegate { return BankSetup.Steps(reader); },
                    delegate(Frame f, Dictionary<string, Rectangle> m) { BankSetup.Build(f, m).Save(); },
                    delegate { Log.Write("Calibration finished. Use Check screen on each page to test it."); });
            };
            Action pet = delegate { CalibratePet(3, bank); };
            Action jobsStep = delegate
            {
                if (jobs == null) { pet(); return; }
                CalibrateOne("Auto Jobs", 2, "jobs_page", JobsSetup.Checklist,
                    delegate { return JobsSetup.Steps(reader, jobs); },
                    delegate(Frame f, Dictionary<string, Rectangle> m) { JobsSetup.Build(f, m, reader, jobs).Save(); },
                    pet);
            };
            CalibrateOne("Stamina Donator", 1, "stamina_page", StaminaSetup.Checklist,
                delegate { return StaminaSetup.Steps(reader); },
                delegate(Frame f, Dictionary<string, Rectangle> m) { StaminaSetup.Build(f, m, reader).Save(); },
                jobsStep);
        }

        // Auto-Train Pet takes three screenshots, since each screen hides the last: the Safehouse page, the
        // Animals window as it opens (OWNED tab), then its ANIMAL TRAINING tab. Saved after the third;
        // skipping any of them skips the pet.
        void CalibratePet(int number, Action next)
        {
            Dictionary<string, Rectangle> marks = new Dictionary<string, Rectangle>();
            Size size = Size.Empty;
            int done = 0;   // screenshots finished
            Action<Frame, Dictionary<string, Rectangle>> keep = delegate(Frame f, Dictionary<string, Rectangle> m)
            {
                if (done > 0 && (f.W != size.Width || f.H != size.Height)) throw new CalibrationException("the game window changed size between screenshots. Calibrate it again.");
                size = new Size(f.W, f.H);
                foreach (KeyValuePair<string, Rectangle> k in m) marks[k.Key] = k.Value;
                done++;
            };
            CalibrateOne("Auto-Train Pet", number, "pet_safehouse", PetSetup.Checklist1, delegate { return PetSetup.Steps1(reader); }, keep, delegate
            {
                if (done < 1) { next(); return; }
                CalibrateOne("Auto-Train Pet", number, "pet_window", PetSetup.Checklist2, delegate { return PetSetup.Steps2(reader); }, keep, delegate
                {
                    if (done < 2) { next(); return; }
                    CalibrateOne("Auto-Train Pet", number, "pet_page", PetSetup.Checklist3, delegate { return PetSetup.Steps3(reader); },
                        delegate(Frame f, Dictionary<string, Rectangle> m)
                        {
                            keep(f, m);
                            Profile jobsProfile = Profile.Load(JobsSetup.Feature, f.W, f.H);
                            int doJob = jobsProfile != null && jobsProfile.Values.ContainsKey("buttonColor") ? jobsProfile.Values["buttonColor"] : -1;
                            PetSetup.Build(f, marks, doJob).Save();
                            Log.Write("Saved the Auto-Train Pet calibration for " + f.W + "x" + f.H + ".");
                        },
                        next, "screenshot 3 of 3");
                }, "screenshot 2 of 3");
            }, "screenshot 1 of 3");
        }

        // One feature: checklist, screenshot, the steps, then save. Calls next() however it ends, so the
        // following feature carries on. part: for a feature that takes more than one screenshot (it then
        // logs its own "Saved").
        void CalibrateOne(string name, int number, string example, string[] checklist, Func<CalibrationStep[]> steps,
            Action<Frame, Dictionary<string, Rectangle>> save, Action next, string part = null)
        {
            string title = "Calibrate " + name + (part != null ? ", " + part : "") + " (" + number + " of 4)";
            using (PrepDialog prep = new PrepDialog(title, example, checklist))
                if (prep.ShowDialog(this) != DialogResult.OK) { Log.Write("Skipped calibrating " + name + "."); next(); return; }
            Log.Write(name + ": switching to the game in 3 seconds...");
            Screenshot(delegate(Frame f)
            {
                if (f != null)
                    using (CalibrationForm form = new CalibrationForm(title, f, steps()))
                    {
                        if (form.ShowDialog(this) != DialogResult.OK) Log.Write("Skipped calibrating " + name + ".");
                        else
                        {
                            try
                            {
                                save(f, form.Marks);
                                if (part == null) Log.Write("Saved the " + name + " calibration for " + f.W + "x" + f.H + ".");
                            }
                            catch (CalibrationException e) { Log.Write(name + " calibration failed: " + e.Message); }
                        }
                    }
                RefreshCalibration();
                next();
            }, true);
        }

        void CheckStamina()
        {
            if (Busy() || !EnsureReader()) return;
            Size size;
            Profile p = ProfileFor(StaminaSetup.Feature, "The stamina donator", out size);
            if (p == null) return;
            Log.Write("Checking the game screen in 3 seconds...");
            Screenshot(delegate(Frame f)
            {
                try
                {
                    StaminaSnapshot s = new StaminaVision(p, reader).Analyze(f, settings.MaxStamina);
                    f.Save(App.PathOf("check-screen.png"));
                    Log.Write("Check: " + s.Describe());
                }
                catch (InvalidOperationException e) { Log.Write(e.Message); }
            });
        }

        // ---------- Auto jobs page ----------

        Control JobsPage()
        {
            FlowLayoutPanel page = PageColumn();
            jobsBanner.Width = PageWidth;
            page.Controls.Add(jobsBanner);

            Card mode = new Card("Mode", PageWidth, 200);
            RadioButton mastery = Radio("Job Mastery", settings.JobMode == JobBot.MasteryThenXp, new Point(18, 54));
            mastery.Font = Theme.Bold(11f);
            Label masteryNote = Theme.Wrapped("Gets gold on every job, lowest level first, then switches to Most XP per hour by itself.", 400, Theme.Muted);
            masteryNote.Location = new Point(40, 80);
            RadioButton xp = Radio("Most XP per hour", settings.JobMode == JobBot.XpOnly, new Point(18, 124));
            xp.Font = Theme.Bold(11f);
            Label xpNote = Theme.Wrapped("Always does the job with the best XP per energy, since energy is what limits your XP.", 400, Theme.Muted);
            xpNote.Location = new Point(40, 152);
            mastery.CheckedChanged += delegate { settings.JobMode = mastery.Checked ? JobBot.MasteryThenXp : JobBot.XpOnly; settings.Save(); };
            Label startHint = Theme.Wrapped("Tick Auto Jobs in the menu, then press START. Tick others to run them together.", 200, Theme.AccentSoft);
            startHint.Location = new Point(PageWidth - 212, 140);
            Label how = Theme.Wrapped("Jumps straight to jobs from data\\jobs.txt. Mastery progress is remembered between runs.", 340, Theme.Muted);
            how.Location = new Point(PageWidth - 362, 50);
            CheckBox panel = new CheckBox { Text = "Show status panel over the game", Checked = settings.ShowOverlay, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(PageWidth - 362, 100), Font = Theme.Ui(10f), Cursor = Cursors.Hand };
            panel.CheckedChanged += delegate { settings.ShowOverlay = panel.Checked; settings.Save(); if (!panel.Checked) overlay.Hide(); };
            Button place = Theme.Secondary("Place panel", 130);
            place.Location = new Point(PageWidth - 362, 144);
            place.Click += delegate { PlaceOverlay(); };
            mode.Controls.AddRange(new Control[] { mastery, masteryNote, xp, xpNote, startHint, how, panel, place });
            page.Controls.Add(mode);

            Card cal = CalibrationCard("jobs_page", jobsCal, JobsSetup.Instructions, CheckJobs);
            cal.Margin = Padding.Empty;
            page.Controls.Add(cal);
            return page;
        }

        // Keeps the overlay over the game while anything runs; hides it otherwise, or when the
        // game isn't the window in front (so it never floats over other programs).
        // Lines that don't apply to what's ticked are left out.
        void UpdateOverlay()
        {
            Rectangle game = GameWindow.ClientArea();
            if (worker == null || !settings.ShowOverlay || game.IsEmpty || !GameWindow.GameInFront())
            {
                if (overlay.Visible) overlay.Hide();
                return;
            }
            JobBot jobs = jobBot;
            StaminaBot stamina = staminaBot;
            List<string> lines = new List<string>();
            List<Color> colors = new List<Color>();
            int bold = -1;
            if (jobs != null)
            {
                if (jobs.JobName.Length > 0) { lines.Add(jobs.JobName); colors.Add(Color.White); }
                bold = lines.Count;
                lines.Add(jobs.XpPerHour > 0 ? jobs.XpPerHour.ToString("N0") + " XP/hr" : "-- XP/hr");
                colors.Add(Theme.AccentSoft);
                lines.Add(jobs.SessionXp.ToString("N0") + " XP this session");
                colors.Add(Theme.Text);
            }
            if (stamina != null)
            {
                lines.Add(stamina.StaminaDonated.ToString("N0") + " stamina donated");
                colors.Add(Theme.Good);
            }
            PetBot pet = petBot;
            if (pet != null)
            {
                TimeSpan left = pet.NextTrain - DateTime.Now;
                lines.Add(left.TotalSeconds <= 0 ? "Pet: training due" : string.Format("Pet trains in {0}:{1:00}", (int)left.TotalMinutes, left.Seconds));
                colors.Add(Theme.Text);
            }
            TimeSpan running = DateTime.Now - runStartedAt;
            lines.Add(string.Format("Session Runtime: {0}:{1:00}:{2:00}", (int)running.TotalHours, running.Minutes, running.Seconds));
            colors.Add(Theme.Muted);

            Scheduler run = scheduler;
            string state = paused ? "Paused while you use your PC" : jobs != null ? jobs.State : run != null ? run.State : "Running";
            overlay.Place(new Rectangle(game.X + overlayArea.X, game.Y + overlayArea.Y, overlayArea.Width, overlayArea.Height));
            overlay.SetText(state, lines.ToArray(), colors.ToArray(), bold);
            if (!overlay.Visible) overlay.Show();
        }

        void PlaceOverlay()
        {
            if (Busy()) return;
            Log.Write("Switching to the game in 3 seconds to place the status panel...");
            Screenshot(delegate(Frame f)
            {
                using (CalibrationForm form = new CalibrationForm("Place the status panel", f, OverlaySetup.Steps()))
                {
                    if (form.ShowDialog(this) != DialogResult.OK) { Log.Write("Cancelled."); return; }
                    OverlaySetup.Save(f, form.Marks["area"]);
                    Log.Write("Saved where the status panel goes for " + f.W + "x" + f.H + ".");
                }
            });
        }

        void CheckJobs()
        {
            if (Busy() || !EnsureReader()) return;
            Size size;
            Profile p = ProfileFor(JobsSetup.Feature, "Auto jobs", out size);
            if (p == null) return;
            Log.Write("Checking the game screen in 3 seconds...");
            Screenshot(delegate(Frame f)
            {
                try
                {
                    f.Save(App.PathOf("check-screen.png"));
                    Log.Write("Check: " + new JobsVision(p, reader, JobCatalog.Load()).Describe(f));
                }
                catch (InvalidOperationException e) { Log.Write(e.Message); }
            });
        }

        // ---------- Other pages ----------

        Control PetPage()
        {
            FlowLayoutPanel page = PageColumn();
            petBanner.Width = PageWidth;
            page.Controls.Add(petBanner);

            FlowLayoutPanel row = CardRow();
            Card how = new Card("How it works", 401, 196);
            Label text = Theme.Wrapped("Every 20 minutes: SAFEHOUSE > ANIMALS > ANIMAL TRAINING > TRAIN, then it closes the window. If the pet is resting, it waits for the \"Train again in\" timer.", 365, Theme.Text);
            text.Location = new Point(18, 50);
            Label energy = Theme.Wrapped("Training costs 10 energy. With Auto Jobs it trains once there are 10 to spare after jobs.", 365, Theme.Muted);
            energy.Location = new Point(18, 116);
            Label hint = Theme.Wrapped("Order with others ticked: jobs, pet, stamina, deposits.", 365, Theme.AccentSoft);
            hint.Location = new Point(18, 162);
            how.Controls.AddRange(new Control[] { text, energy, hint });

            // Feeding: checked on every visit, on the OWNED tab the window opens on.
            Card feeding = new Card("Feeding", 401, 196);
            CheckBox feed10 = new CheckBox { Text = "FEED 10% (food) at 90% hunger or less", Checked = settings.PetFeed10, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(18, 54), Font = Theme.Ui(10.5f), Cursor = Cursors.Hand };
            CheckBox feed50 = new CheckBox { Text = "FEED 50% (premium food) at 50% or less", Checked = settings.PetFeed50, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(18, 88), Font = Theme.Ui(10.5f), Cursor = Cursors.Hand };
            feed10.CheckedChanged += delegate { settings.PetFeed10 = feed10.Checked; settings.Save(); RefreshCalibration(); };
            feed50.CheckedChanged += delegate { settings.PetFeed50 = feed50.Checked; settings.Save(); RefreshCalibration(); };
            Label feedNote = Theme.Wrapped("Checked every visit. It keeps feeding while a portion fits; with both ticked, premium food goes first, then 10% tops it up.", 365, Theme.Muted);
            feedNote.Location = new Point(18, 128);
            feeding.Controls.AddRange(new Control[] { feed10, feed50, feedNote });
            feeding.Margin = Padding.Empty;

            row.Controls.Add(how);
            row.Controls.Add(feeding);
            page.Controls.Add(row);
            Card cal = CalibrationCard("pet_page", petCal, PetSetup.Instructions, CheckPet);
            cal.Margin = new Padding(0, 12, 0, 0);
            page.Controls.Add(cal);
            return page;
        }

        void CheckPet()
        {
            if (Busy() || !EnsureReader()) return;
            Size size;
            Profile p = ProfileFor(PetSetup.Feature, "Auto-train pet", out size);
            if (p == null) return;
            Log.Write("Checking the game screen in 3 seconds...");
            Screenshot(delegate(Frame f)
            {
                try
                {
                    f.Save(App.PathOf("check-screen.png"));
                    Log.Write("Check: " + new PetVision(p, reader).Describe(f));
                }
                catch (InvalidOperationException e) { Log.Write(e.Message); }
            });
        }

        Control DepositPage()
        {
            FlowLayoutPanel page = PageColumn();
            bankBanner.Width = PageWidth;
            page.Controls.Add(bankBanner);

            Card how = new Card("Deposit and auto-buy", PageWidth, 214);
            Label text = Theme.Wrapped("Keeps your cash in the bank, where other players can't take it: DEPOSIT ALL right after every 5-second payout.", PageWidth - 40, Theme.Text);
            text.Location = new Point(18, 52);

            // Auto-buy withdrawal: an amount taken out before every shop restock, for the auto-buy pass.
            CheckBox withdraw = new CheckBox { Text = "Auto-buy withdrawal of", Checked = settings.AutoBuyWithdraw, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(18, 92), Font = Theme.Bold(10.5f), Cursor = Cursors.Hand };
            TextBox amount = new TextBox { Text = settings.WithdrawAmount > 0 ? Money.Short(settings.WithdrawAmount) : "", Location = new Point(222, 90), Width = 140, BackColor = Theme.CardAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(10.5f) };
            Label example = Theme.Label("e.g. 500M, 2B, 1.5T", Theme.Ui(9.5f), Theme.Muted);
            example.Location = new Point(372, 94);
            withdraw.CheckedChanged += delegate { settings.AutoBuyWithdraw = withdraw.Checked; settings.Save(); };
            amount.Leave += delegate
            {
                long v;
                if (Money.TryParse(amount.Text, out v)) { settings.WithdrawAmount = v; settings.Save(); amount.Text = Money.Short(v); }
                else if (amount.Text.Trim().Length > 0) Log.Write("Couldn't understand \"" + amount.Text + "\" as an amount. Try something like 500M or 2B.");
            };
            Label withdrawNote = Theme.Wrapped("The shop restocks every 5 minutes on the clock (:00, :05, :10...). 10 seconds before each restock the tool withdraws this amount, then deposits what's left 3 seconds after the restock. Skipped if you were attacked in the last minute.", PageWidth - 40, Theme.Muted);
            withdrawNote.Location = new Point(18, 126);
            Label hint = Theme.Wrapped("Tick Auto Deposit in the menu, then press START. With others ticked: jobs when there's energy, the pet when it's due, stamina donations, and deposits the rest of the time.", PageWidth - 40, Theme.AccentSoft);
            hint.Location = new Point(18, 176);
            how.Controls.AddRange(new Control[] { text, withdraw, amount, example, withdrawNote, hint });
            page.Controls.Add(how);

            Card cal = CalibrationCard("bank_page", bankCal, BankSetup.Instructions, CheckBank);
            cal.Margin = new Padding(0, 0, 0, 0);
            page.Controls.Add(cal);
            return page;
        }

        void CheckBank()
        {
            if (Busy() || !EnsureReader()) return;
            Size size;
            Profile p = ProfileFor(BankSetup.Feature, "Auto deposit", out size);
            if (p == null) return;
            Log.Write("Checking the game screen in 3 seconds...");
            Screenshot(delegate(Frame f)
            {
                f.Save(App.PathOf("check-screen.png"));
                Log.Write("Check: Bank page: " + (BankSetup.HeadingSaysBank(reader, f, p.Box("heading")) ? "yes" : "NO") + ".");
            });
        }

        Control SettingsPage()
        {
            FlowLayoutPanel page = PageColumn();
            Card general = new Card("General", PageWidth, 230);
            CheckBox pause = new CheckBox { Text = "Pause while I'm using the mouse or keyboard", Checked = settings.PauseOnActivity, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(18, 54), Font = Theme.Ui(10.5f) };
            pause.CheckedChanged += delegate { settings.PauseOnActivity = pause.Checked; settings.Save(); };
            Label resume = Theme.Label("Carry on after this many seconds of no activity", Theme.Ui(10f), Theme.Text);
            resume.Location = new Point(40, 90);
            NumericUpDown resumeBox = Number(settings.ResumeSeconds, 1, 120, delegate(int v) { settings.ResumeSeconds = v; });
            resumeBox.Location = new Point(360, 87);
            Button folder = Theme.Secondary("Open calibrations folder", 230);
            folder.Location = new Point(18, 136);
            folder.Click += delegate
            {
                string dir = App.PathOf("profiles");
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            };
            Label note = Theme.Wrapped("Each screen size keeps its own calibration, so the desktop and laptop can both be set up. Calibrate again whenever the game's look changes (a new theme, say).", 520, Theme.Muted);
            note.Location = new Point(264, 136);
            general.Controls.AddRange(new Control[] { pause, resume, resumeBox, folder, note });
            page.Controls.Add(general);
            return page;
        }

        RadioButton Radio(string text, bool on, Point at)
        {
            RadioButton r = new RadioButton { Text = text, Checked = on, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card, Location = at, Font = Theme.Ui(10.5f), Cursor = Cursors.Hand };
            return r;
        }

        NumericUpDown Number(int value, int min, int max, Action<int> set)
        {
            NumericUpDown n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = 80, BackColor = Theme.CardAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(10.5f) };
            n.ValueChanged += delegate { set((int)n.Value); settings.Save(); };
            return n;
        }

        // ---------- Calibration status ----------

        void RefreshCalibration()
        {
            Size size = GameWindow.DetectSize();
            gameLabel.Text = size.IsEmpty ? "Game: not open" : "Game: " + size.Width + " x " + size.Height;
            ShowState(StaminaSetup.Feature, "the Family > Perks page", size, staminaBanner, staminaCal, nav["stamina"]);
            ShowState(JobsSetup.Feature, "the Jobs page (scrolled to the top)", size, jobsBanner, jobsCal, nav["jobs"]);
            ShowState(BankSetup.Feature, "the Bank page", size, bankBanner, bankCal, nav["deposit"]);
            ShowState(PetSetup.Feature, "the Safehouse page", size, petBanner, petCal, nav["pet"]);
            // A pet calibration from before feeding still trains, but can't feed.
            if ((settings.PetFeed10 || settings.PetFeed50) && !size.IsEmpty)
            {
                Profile pet = Profile.Load(PetSetup.Feature, size.Width, size.Height);
                if (pet != null && !pet.Boxes.ContainsKey("hunger"))
                {
                    petCal.Text = "Calibrate again to use feeding";
                    petCal.ForeColor = Theme.Warn;
                }
            }
        }

        void ShowState(string feature, string where, Size size, Banner banner, Label label, NavButton button)
        {
            string from;
            Profile.State state = Profile.Check(feature, size, out from);
            string screen = size.Width + "x" + size.Height;
            switch (state)
            {
                case Profile.State.GameClosed:
                    banner.Set("i", "OPEN THE GAME TO CHECK CALIBRATION", "Start Roblox and the tool will check whether this feature is calibrated for your screen.", Theme.Muted, Theme.CardAlt);
                    label.Text = "Open the game to check";
                    label.ForeColor = Theme.Muted;
                    break;
                case Profile.State.Missing:
                    banner.Set("!", "NOT CALIBRATED FOR THIS SCREEN (" + screen + ")", "This won't work until it's calibrated. Open the game on " + where + ", then press CALIBRATE in the menu. It takes about a minute per feature.", Theme.Danger, Theme.DangerFill);
                    label.Text = "Not calibrated for " + screen;
                    label.ForeColor = Theme.Danger;
                    break;
                case Profile.State.Borrowed:
                    banner.Set("!", "USING THE " + from + " CALIBRATION ON " + screen, "It's scaled from another screen size, which usually works. For best results, open the game on " + where + " and press CALIBRATE in the menu.", Theme.Warn, Theme.WarnFill);
                    label.Text = "Borrowed from " + from;
                    label.ForeColor = Theme.Warn;
                    break;
                default:
                    banner.Visible = false;
                    label.Text = "✔  Calibrated for " + screen;
                    label.ForeColor = Theme.Good;
                    break;
            }
            bool warn = state == Profile.State.Missing || state == Profile.State.Borrowed;
            if (button.Warning != warn) { button.Warning = warn; button.Invalidate(); }
        }

        Profile ProfileFor(string feature, string name, out Size size)
        {
            size = GameWindow.DetectSize();
            if (size.IsEmpty) { Log.Write("Roblox isn't open (or is minimised)."); return null; }
            Profile p = Profile.Load(feature, size.Width, size.Height);
            if (p == null) { Log.Write(name + " isn't calibrated for " + size.Width + "x" + size.Height + " yet. Press CALIBRATE in the menu."); return null; }
            if (p.ScaledFrom != null) Log.Write("Using the " + p.ScaledFrom + " calibration scaled to this screen. If it misbehaves, calibrate on this screen.");
            return p;
        }

        // Switches to the game after 3 seconds, screenshots it, then comes back.
        // evenIfFailed: still call back (with null) if the game couldn't be captured, so a sequence can go on.
        void Screenshot(Action<Frame> then, bool evenIfFailed = false)
        {
            Thread t = new Thread(delegate()
            {
                Thread.Sleep(3000);
                GameWindow game = new GameWindow(CancellationToken.None, null, Size.Empty);
                Frame f = game.Capture();
                Ui(delegate
                {
                    Activate();
                    if (f == null) Log.Write("Couldn't capture the game: " + game.LastError);
                    if (f != null || evenIfFailed) then(f);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- Running ----------

        bool EnsureReader()
        {
            if (reader != null) return true;
            try { reader = new WindowsText(); return true; }
            catch (Exception e) { Log.Write("Windows text recognition isn't available: " + e.Message); return false; }
        }

        bool Busy()
        {
            if (worker == null) return false;
            Log.Write("Stop the running task first (F8).");
            return true;
        }

        void StartWorker(string what, Size size, Action<GameWindow> body)
        {
            if (Busy()) return;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            UserActivity activity = null;
            if (settings.PauseOnActivity)
            {
                activity = new UserActivity(settings.ResumeSeconds);
                activity.StartPolling(token);
            }
            GameWindow game = new GameWindow(token, activity, size);
            game.PauseChanged = delegate(bool now) { paused = now; Ui(delegate { status.Text = now ? "Paused while you use the PC" : what; }); };
            runStartedAt = DateTime.Now;
            SetRunning(true);
            status.Text = what;
            Log.Write(what + ". Press F8 to stop.");
            worker = new Thread(delegate()
            {
                try { body(game); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Write("Stopped by an error: " + e.Message); }
                Ui(delegate { worker = null; jobBot = null; staminaBot = null; petBot = null; scheduler = null; paused = false; overlay.Hide(); SetRunning(false); status.Text = "Stopped"; Log.Write("Stopped."); });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Stop()
        {
            if (cancel != null) cancel.Cancel();
        }

        void SetRunning(bool running)
        {
            SetStartLook(running);
            status.ForeColor = running ? Theme.Good : Theme.Text;
        }

        // The big menu button: bright green START, or red STOP while running.
        void SetStartLook(bool running)
        {
            Color green = Color.FromArgb(0xA6, 0xE0, 0x22), red = Theme.Danger;
            startButton.Text = running ? "STOP  (F8)" : "START";
            startButton.BackColor = running ? red : green;
            startButton.ForeColor = running ? Color.White : Color.FromArgb(0x10, 0x10, 0x10);
            startButton.FlatAppearance.MouseOverBackColor = running ? Color.FromArgb(0xF0, 0x6A, 0x6A) : Color.FromArgb(0xBC, 0xF0, 0x3C);
            startButton.FlatAppearance.BorderSize = 2;
            startButton.FlatAppearance.BorderColor = Color.Black;
            startButton.Font = Theme.Heading(running ? 15f : 20f);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!RegisterHotKey(Handle, 1, 0, StopKey)) Log.Write("F8 is in use by another program, so use the Stop button instead.");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) Stop();
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Stop();
            UnregisterHotKey(Handle, 1);
            base.OnFormClosing(e);
        }

        void AppendLog(string message)
        {
            if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(30000);
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        void Ui(Action action)
        {
            if (IsDisposed) return;
            if (!IsHandleCreated) { action(); return; }
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }
    }
}

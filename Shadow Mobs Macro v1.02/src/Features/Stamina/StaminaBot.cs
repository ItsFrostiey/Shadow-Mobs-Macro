using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Donates stamina to the first unfinished perk in the user's order, and moves to the next
    // once it reaches level 25. Only ever clicks the Give buttons, the Perks tab and the sidebar.
    public class StaminaBot
    {
        readonly IGame game;
        readonly ITextReader reader;
        readonly StaminaVision vision;
        readonly Settings settings;
        readonly Random rng = new Random();
        readonly HashSet<int> maxed = new HashSet<int>();
        readonly Dictionary<int, DateTime> skipUntil = new Dictionary<int, DateTime>();
        readonly Dictionary<int, int> notReady = new Dictionary<int, int>();
        const int MaxButtonWaits = 6;

        public Action<string> Status = delegate { };
        public bool Finished;
        public int Donations;          // donations confirmed this run (for running alongside auto jobs)
        public bool NotEnoughStamina;  // the last look found too little stamina to donate
        public int KnownStamina = -1;  // stamina tracked by the scheduler, used when the bar is covered
        public int StaminaDonated;     // stamina given this run (for the status panel)
        public bool GreyButtonSeen;    // GIVE was grey although tracking said there was enough

        // "Level all perks to N first" (Settings.PerkLevelCap; 0 = off).
        readonly HashSet<int> atCap = new HashSet<int>();   // perks seen at or above N
        int capInUse = -1;
        bool levelledToCap;                                 // every ticked perk has reached N
        bool CapPhase { get { return capInUse > 0 && capInUse < 25 && !levelledToCap; } }

        int navigationAttempts, failedDonations;
        string lastStatus;

        public StaminaBot(IGame game, ITextReader reader, StaminaVision vision, Settings settings)
        {
            this.game = game;
            this.reader = reader;
            this.vision = vision;
            this.settings = settings;
        }

        // One look at the screen and at most one action. Returns how long to wait before the next.
        public int Tick()
        {
            int pollMs = Math.Max(1, settings.PollSeconds) * 1000;
            Frame frame = game.Capture();
            if (frame == null) { SetStatus("Can't see the game: " + game.LastError); return pollMs; }
            StaminaSnapshot snap = vision.Analyze(frame, settings.MaxStamina);
            if (!snap.Hud && !snap.HudCovered)
            {
                Explain("Looking for the HEALTH label in the top bar");
                SetStatus("Can't find the HEALTH label. Waiting (a popup may be covering it).");
                return pollMs;
            }
            if (!snap.OnPerksPage)
            {
                // The menu is only clicked with HEALTH actually in sight, so nothing is ever clicked
                // through a Roblox menu or loading screen. (On the Family page, PERKS is fine either way.)
                if (!snap.Hud && !snap.FamilyHeader) { Explain("Waiting for the HEALTH label (a notice is covering it)"); return 2000; }
                return Navigate(snap, pollMs);
            }
            navigationAttempts = 0;

            // The stamina tracked by the scheduler (read once at START, then counted), so the bar never
            // has to be visible. Only if nothing is tracked yet is the bar itself used.
            int staminaNow = KnownStamina >= 0 ? KnownStamina : snap.StaminaVisible && snap.Stamina >= 0 ? snap.Stamina : -1;
            if (staminaNow < 0)
            {
                Explain("Looking for the STAMINA label (a notice is probably covering it)");
                SetStatus("Can't see the stamina bar; checking again in 2 seconds");
                return 2000;
            }
            Explain(null);   // everything looked right: the next problem gets logged again
            int amount = settings.GiveAmount;
            int needed = amount + settings.KeepReserve;
            NotEnoughStamina = staminaNow < needed;
            if (NotEnoughStamina) { SetStatus(string.Format("Stamina {0}. Donating at {1}", staminaNow, needed)); return pollMs; }

            // "Level all perks to N first": while that's on, perks already at N are passed over, so every
            // ticked perk reaches N (in your order) before any goes past it. Then it's levelling to 25.
            if (settings.PerkLevelCap != capInUse) { capInUse = settings.PerkLevelCap; atCap.Clear(); levelledToCap = false; }
            bool capPhase = CapPhase;
            bool anyLeft = false, anyBelowCap = false;
            foreach (string name in settings.PerkOrder)
            {
                if (settings.PerksOff.Contains(name)) continue;
                int index = Array.IndexOf(Perks.Names, name);
                if (index < 0 || maxed.Contains(index)) continue;
                anyLeft = true;
                if (capPhase && atCap.Contains(index)) continue;
                anyBelowCap = true;
                DateTime until;
                if (skipUntil.TryGetValue(index, out until) && game.Now < until) continue;
                return DonateTo(index, amount, staminaNow);
            }
            if (!anyLeft)
            {
                Finished = true;
                Log.Write("Every selected perk is at level 25, or none are selected. Stopped.");
                return 0;
            }
            if (capPhase && !anyBelowCap)
            {
                levelledToCap = true;
                Log.Write(string.Format("Every selected perk is at level {0} or more; now levelling them to 25 in your order", capInUse));
                return 200;
            }
            SetStatus("Selected perks are temporarily unavailable, retrying soon");
            return pollMs;
        }

        int Navigate(StaminaSnapshot snap, int pollMs)
        {
            if (++navigationAttempts > 5)
            {
                navigationAttempts = 0;
                SetStatus("Couldn't get to Family > Perks after several tries. Trying again in a minute.");
                return Math.Max(pollMs, 60000);
            }
            if (snap.FamilyHeader)
            {
                Log.Write("Trying to click the PERKS tab");
                Rectangle tab = vision.Tab;
                SafeClick(tab.X + tab.Width / 2 + snap.Ox, tab.Y + tab.Height / 2 + snap.Oy, snap, tab.Width / 3, tab.Height / 4);
                return 1500;
            }
            Log.Write("Trying to click FAMILY");
            OpenFamily();
            return 1200;
        }

        // Clicks the calibrated FAMILY menu button. Older calibrations without it fall back to reading
        // the menu's words.
        public void OpenFamily()
        {
            Rectangle? button = vision.FamilyButton;
            if (button != null && navigationAttempts >= 3)
            {
                // Two clicks on the saved spot didn't open Family: it may be on the wrong button.
                if (navigationAttempts == 3) Log.Write("The saved FAMILY button didn't open Family; looking for FAMILY in the menu instead. Recalibrate the Family Button step to fix it.");
                button = null;
            }
            if (button == null)
            {
                Sidebar.Open(game, reader, "FAMILY", delegate(Frame f) { return vision.Analyze(f, settings.MaxStamina).FamilyHeader; });
                return;
            }
            Rectangle b = button.Value;
            game.Click(b.X + b.Width / 2 + rng.Next(-b.Width / 6, b.Width / 6 + 1), b.Y + b.Height / 2 + rng.Next(-b.Height / 5, b.Height / 5 + 1));
        }

        int DonateTo(int index, int amount, int staminaBefore)
        {
            string name = Perks.Names[index];
            int lastTop = int.MinValue;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Frame frame = game.Capture();
                if (frame == null) return 3000;
                StaminaSnapshot snap = vision.Analyze(frame, settings.MaxStamina);
                if (!snap.OnPerksPage) return 500;

                PerkSighting perk = snap.Find(index);
                if (perk != null && perk.ButtonsInView)
                {
                    // Only click once the list has stopped moving between two captures.
                    if (Math.Abs(perk.Top - lastTop) > 1) { lastTop = perk.Top; game.Sleep(300); continue; }
                    if (perk.Maxed)
                    {
                        maxed.Add(index);
                        Log.Write(name + " is at level 25, moving to the next perk");
                        return 200;
                    }
                    if (CapPhase)
                    {
                        int level = vision.ReadPerkLevel(frame, perk, snap);
                        if (level >= capInUse)
                        {
                            atCap.Add(index);
                            Log.Write(string.Format("{0} is at level {1} (levelling all to {2} first), moving to the next perk", name, level, capInUse));
                            return 200;
                        }
                    }
                    bool ready = amount == 5 ? perk.Give5Ready : perk.Give1Ready;
                    if (!ready) return ButtonNotReady(index, amount, snap);
                    notReady.Remove(index);
                    return ClickGive(index, amount, staminaBefore, perk, snap);
                }

                Point s = vision.ScrollPoint;
                game.Wheel(s.X + snap.Ox, s.Y + snap.Oy, ScrollDirection(index, perk, snap));
                lastTop = int.MinValue;
                game.Sleep(rng.Next(350, 600));
            }
            skipUntil[index] = game.Now.AddMinutes(10);
            SetStatus("Couldn't scroll to " + name + ". Skipping it for 10 minutes.");
            return 1000;
        }

        // A grey button is usually temporary: the moment after a donation, or a popup. Stay on this
        // perk rather than spending stamina elsewhere, and only move on if it stays grey for a while.
        int ButtonNotReady(int index, int amount, StaminaSnapshot snap)
        {
            string name = Perks.Names[index];
            ParkMouse(snap);
            if (KnownStamina >= 0)
            {
                // Tracking said there was enough, but the game says there isn't: the count was high.
                // Hand back to the scheduler to correct it, rather than waiting or skipping the perk.
                GreyButtonSeen = true;
                NotEnoughStamina = true;
                return 200;
            }
            int tries;
            notReady.TryGetValue(index, out tries);
            notReady[index] = ++tries;
            if (tries >= MaxButtonWaits)
            {
                notReady.Remove(index);
                skipUntil[index] = game.Now.AddHours(1);
                Log.Write(string.Format("Give {0} on {1} has stayed greyed out, so it's skipped for an hour.", amount, name));
                return 200;
            }
            SetStatus(string.Format("Waiting for Give {0} on {1}", amount, name));
            return tries <= 2 ? 1500 : 60000;
        }

        void ParkMouse(StaminaSnapshot snap)
        {
            Point s = vision.ScrollPoint;
            game.MoveMouse(s.X + snap.Ox, s.Y + snap.Oy);
        }

        int ScrollDirection(int index, PerkSighting perk, StaminaSnapshot snap)
        {
            if (perk != null) return perk.Top + vision.Give(1).Bottom > vision.List.Bottom + snap.Oy - 2 ? 1 : -1;
            if (snap.Perks.Count == 0) return -1;
            return index < snap.Perks[0].Index ? -1 : 1;
        }

        int ClickGive(int index, int amount, int staminaBefore, PerkSighting perk, StaminaSnapshot snap)
        {
            string name = Perks.Names[index];
            Rectangle b = vision.Give(amount);
            Frame beforeClick = game.Capture();
            bool oldNotice = beforeClick != null && GaveNotice(beforeClick);   // a notice from the last donation
            SafeClick(b.X + b.Width / 2 + snap.Ox, perk.Top + b.Y + b.Height / 2, snap, b.Width / 5, b.Height / 5);
            ParkMouse(snap);   // a button can change colour under the pointer

            // The game confirms each donation with a "You gave 5 stamina to Enforcers." notice in the
            // top-right corner. Look for it for 2 seconds. The stamina bar is never read here: stamina is
            // tracked, and the notice covers the bar anyway.
            Status(string.Format("Gave {0} to {1}, checking it registered", amount, name));
            DateTime until = game.Now.AddSeconds(2);
            while (game.Now < until)
            {
                game.Sleep(250);
                Frame frame = game.Capture();
                if (frame == null || oldNotice || !GaveNotice(frame)) continue;
                failedDonations = 0;
                Donations++; StaminaDonated += amount;
                Log.Write(string.Format("Gave {0} stamina to {1}", amount, name));
                lastStatus = string.Format("Gave {0} to {1}", amount, name);   // already logged just above
                Status(lastStatus);
                return 2000;   // back-to-back donations 2 seconds apart
            }
            if (oldNotice)
            {
                // The last donation's notice was still up, so a new one can't be told apart. Count it;
                // if the click had missed, the next GIVE being grey corrects the count.
                failedDonations = 0;
                Donations++; StaminaDonated += amount;
                Log.Write(string.Format("Gave {0} stamina to {1}", amount, name));
                return 2000;
            }

            failedDonations++;
            Log.Write(string.Format("Clicked Give {0} on {1} but no \"You gave\" notice appeared; trying again", amount, name));
            if (failedDonations >= 3)
            {
                failedDonations = 0;
                SetStatus("Three GIVE clicks in a row didn't register. Trying again in a minute.");
                return 60000;
            }
            return 2000;
        }

        // The "You gave N stamina to ..." notice in the top-right corner.
        bool GaveNotice(Frame f)
        {
            Rectangle corner = new Rectangle(f.W * 62 / 100, 0, f.W * 38 / 100, f.H * 9 / 100);
            foreach (OcrLine l in reader.Read(f, corner, 1))
            {
                string t = l.Text.ToUpperInvariant();
                if (t.Contains("GAVE") && t.Contains("STAMINA")) return true;
            }
            return false;
        }

        // Refuses any click outside the Give buttons, the Perks tab or the sidebar, so a
        // calibration mistake can never press Upgrade, Leave Family or a refill button.
        void SafeClick(int x, int y, StaminaSnapshot snap, int jitterX, int jitterY)
        {
            int cx = x - snap.Ox, cy = y - snap.Oy;
            Rectangle g1 = vision.Give(1), g5 = vision.Give(5), tab = vision.Tab, list = vision.List;
            bool giveZone = snap.OnPerksPage && cy >= list.Top && cy <= list.Bottom
                && ((cx >= g1.Left && cx < g1.Right) || (cx >= g5.Left && cx < g5.Right));
            bool tabZone = snap.FamilyHeader && tab.Contains(cx, cy);
            if (!giveZone && !tabZone)
                throw new InvalidOperationException(string.Format("Blocked a click outside the allowed areas at {0},{1}", cx, cy));
            game.Click(x + rng.Next(-jitterX, jitterX + 1), y + rng.Next(-jitterY, jitterY + 1));
        }

        // Logs a short "what it's trying to do" line once when it starts, not on every retry.
        string explained;
        void Explain(string what)
        {
            if (what != null && what != explained) Log.Write(what);
            explained = what;
        }

        void SetStatus(string message)
        {
            Status(message);
            if (message != lastStatus) Log.Write(message);
            lastStatus = message;
        }
    }
}

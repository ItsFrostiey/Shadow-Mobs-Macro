using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace StaminaDonator
{
    // Auto jobs. Picks a job from data/jobs.txt (never by reading the whole list), scrolls straight
    // to it with the mouse wheel, then keeps pressing its DO JOB button as soon as there is energy.
    //   Mode 0: master each job from the top (lowest level first), then switch to most XP per hour.
    //   Mode 1: most XP per hour: the job with the best XP per energy, since energy is what limits XP.
    // Only clicks DO JOB buttons and scrolls the list; the sidebar is
    // handled by Sidebar.
    public class JobBot
    {
        public const int MasteryThenXp = 0, XpOnly = 1;

        readonly IGame game;
        readonly ITextReader reader;
        readonly JobsVision vision;
        readonly List<JobInfo> jobs;
        readonly Settings settings;
        readonly Dictionary<string, int> progress;
        readonly Dictionary<string, DateTime> skipUntil = new Dictionary<string, DateTime>();
        readonly Random rng = new Random();

        public Action<string> Status = delegate { };

        // For the in-game overlay: what it's doing (never the job name), the job, and XP per hour.
        public volatile string State = "Starting";
        public volatile string JobName = "";
        public double XpPerHour;
        public readonly DateTime StartedAt = DateTime.Now;
        long xpEarned;
        public long SessionXp { get { return Interlocked.Read(ref xpEarned); } }
        readonly HashSet<string> masteryLooked = new HashSet<string>();   // looked up this run (even if unreadable)

        int mode;
        int level = -1, pendingLevel = -1;
        DateTime levelReadAt = DateTime.MinValue;
        double regenSeconds;          // seconds per energy point; 0 = unknown
        DateTime regenCheckedAt = DateTime.MinValue;
        JobInfo target;
        JobRow onScreen;              // where the target's card is right now
        Frame titleShot;              // its name, to check cheaply that the list hasn't moved
        int sinceMasteryRead = 99, notLit, failed;
        JobInfo lastSeen;             // the last job seen while scrolling, to know which way to go
        int menuMisses;               // clicks on the JOBS menu button that didn't open Jobs
        double pixelsPerTick;         // how far one mouse-wheel notch scrolls; 0 = not measured yet
        string lastStatus;

        public JobBot(IGame game, ITextReader reader, JobsVision vision, List<JobInfo> jobs, Settings settings)
        {
            this.game = game;
            this.reader = reader;
            this.vision = vision;
            this.jobs = jobs;
            this.settings = settings;
            mode = settings.JobMode;
            progress = JobCatalog.LoadProgress();
            measuredXp = JobCatalog.LoadMeasuredXp();
            scrollMemory = JobCatalog.LoadScroll(vision.P.W, vision.P.H);
            if (!vision.HasXpBar) Log.Write("Calibrate auto jobs again to add the XP Bar step; until then XP comes from the job list only (no boosts).");
        }

        // XP a job really gives, measured from the XP bar on every press (boosts included). Jobs not
        // measured yet use the job list's XP, scaled by the boost seen on other jobs.
        readonly Dictionary<string, int> measuredXp;

        int XpOf(JobInfo j)
        {
            int xp;
            if (measuredXp.TryGetValue(j.Name, out xp)) return xp;
            double boost = 0;
            int n = 0;
            foreach (JobInfo k in jobs)
                if (measuredXp.TryGetValue(k.Name, out xp)) { boost += (double)xp / k.Xp; n++; }
            return n > 0 ? (int)Math.Round(j.Xp * boost / n) : j.Xp;
        }

        // XP gained between two readings of the bar, allowing for a level-up in between (the bar
        // restarts). -1 if it doesn't add up to something believable for this job.
        int XpGained(int nowBefore, int maxBefore, int nowAfter, int maxAfter, JobInfo job)
        {
            if (nowBefore < 0 || nowAfter < 0) return -1;
            int gained = nowAfter >= nowBefore && maxAfter == maxBefore ? nowAfter - nowBefore : (maxBefore - nowBefore) + nowAfter;
            return gained >= job.Xp * 8 / 10 && gained <= job.Xp * 5 ? gained : -1;
        }

        // Called after another feature used the game (another page was opened): the job list's
        // position can't be trusted, so the next look starts fresh.
        public void ForgetPosition()
        {
            onScreen = null;
            fromTop = -1;
        }

        public int JobsDone;              // jobs completed this run
        public int KnownEnergy = -1, KnownMax;   // energy tracked by the scheduler, used when the bar is covered
        public bool WaitingForEnergy;     // the last tick found too little energy for the next job

        // For running alongside other features: the energy the job it would do next costs, or -1 if
        // it can't tell yet. Reads only the level from the namecard, so it works from any page.
        public int NextJobCost(Frame f, int maxEnergy)
        {
            UpdateLevel(f);
            if (level <= 0 || maxEnergy <= 0) return -1;
            JobInfo next = Choose(maxEnergy);
            return next == null ? -1 : Cost(next);
        }

        public Rectangle EnergyBar { get { return vision.EnergyBar; } }

        // An energy regen rate already measured (by the scheduler at START), so it isn't hovered again.
        public void UseRegen(double seconds)
        {
            if (seconds <= 0) return;
            regenSeconds = seconds;
            regenCheckedAt = DateTime.Now;
        }
        public Point Park { get { return vision.Park; } }

        // The energy read straight after the last job (for the scheduler's energy clock).
        public int EnergyAfterJob = -1, EnergyMax;
        public DateTime EnergyAfterJobAt;

        int Mastery(JobInfo j)
        {
            int m;
            return progress.TryGetValue(j.Name, out m) ? m : -1;
        }

        int Cost(JobInfo j) { return JobCatalog.Cost(j, Mastery(j)); }

        // One look at the screen and at most one action. Returns how long to wait before the next.
        public int Tick()
        {
            WaitingForEnergy = false;
            Frame f = game.Capture();
            if (f == null) { State = "Can't see the game"; SetStatus("Can't see the game: " + game.LastError); return 3000; }
            if (!vision.OnJobsPage(f))
            {
                onScreen = null;
                fromTop = -1;
                Explain("Looking for the JOBS heading");
                State = "Opening the job list";
                SetStatus("Trying to click JOBS");
                Rectangle? button = vision.JobsButton;
                if (button != null && ++menuMisses > 2)
                {
                    // Two clicks on the saved spot didn't open Jobs: it may be on the wrong button.
                    if (menuMisses == 3) Log.Write("The saved JOBS button didn't open Jobs; looking for JOBS in the menu instead. Recalibrate the Jobs Button step to fix it.");
                    button = null;
                }
                if (button == null) Sidebar.Open(game, reader, "JOBS", vision.OnJobsPage);   // older calibration, or the fallback
                else
                {
                    Rectangle b = button.Value;
                    game.Click(b.X + b.Width / 2 + rng.Next(-b.Width / 6, b.Width / 6 + 1), b.Y + b.Height / 2 + rng.Next(-b.Height / 5, b.Height / 5 + 1));
                }
                return 1200;
            }

            if (regenSeconds <= 0 && (game.Now - regenCheckedAt).TotalMinutes > 5 || (game.Now - regenCheckedAt).TotalMinutes > 30)
            {
                MeasureRegen();
                return 200;
            }

            menuMisses = 0;   // on the Jobs page
            UpdateLevel(f);
            if (level <= 0)
            {
                if (pendingLevel < 0) Explain("Looking for your level on the namecard");   // not even a first reading yet
                State = "Reading your level"; SetStatus("Reading your level..."); return 1000;
            }

            int energy, max;
            bool energyRead = vision.ReadEnergy(f, out energy, out max);
            if (!energyRead && KnownEnergy >= 0 && KnownMax > 0)
            {
                energy = KnownEnergy;   // covered by a notice: use the energy tracked by the scheduler
                max = KnownMax;
            }
            else if (!energyRead)
            {
                Explain("Looking for the energy bar (a notice may be covering it)");
                State = "Can't read energy"; SetStatus("Can't read your energy. Waiting."); return 2000;
            }
            Explain(null);   // the basics read fine: the next problem gets logged again

            JobInfo next = Choose(max);
            if (next == null) { State = "No job available"; JobName = ""; SetStatus("No job can be done right now (all skipped or above your max energy). Retrying in a minute."); return 60000; }

            // In XP mode, a job whose mastery isn't known yet might be cheaper than it looks (5% off
            // from silver), so look it up before committing, if it could beat the current pick.
            JobInfo unknown = MasteryWorthChecking(next, max);
            if (unknown != null) return LookUpMastery(unknown);
            if (next != target)
            {
                target = next;
                onScreen = null;
                sinceMasteryRead = 99;
                notLit = 0;
                Log.Write(string.Format("Next job: {0} ({1} energy, {2:N0} XP{3})", target.Name, Cost(target), XpOf(target),
                    Mastery(target) >= 0 ? ", mastery " + Mastery(target) + "/100" : ""));
            }

            JobName = target.Name;
            UpdateXpPerHour();

            // Find the card once, then only check cheaply that it hasn't moved.
            if (onScreen == null || !StillThere(f))
            {
                // The card moved without the tool scrolling (you scrolled, say): where the list is
                // can't be trusted any more, so the next move starts from the top again.
                if (onScreen != null) fromTop = -1;
                State = "Finding the job";
                onScreen = ScrollTo(target);
                if (onScreen == null)
                {
                    Skip(target, 10, "Couldn't find " + target.Name + " in the list (is its area collapsed?)");
                    return 500;
                }
                Frame now = game.Capture();
                if (now != null) titleShot = now.Crop(vision.TitleAt(onScreen.Top));
                return 100;
            }

            int cost = Cost(target);
            if (energy < cost)
            {
                // Wake just as the energy arrives, so the job starts straight away.
                int wait = regenSeconds > 0 ? (int)((cost - energy) * regenSeconds * 1000) - 400 : 3000;
                WaitingForEnergy = true;
                State = string.Format("Waiting for energy ({0}/{1})", energy, cost);
                Status(string.Format("{0}: waiting for energy {1}/{2}", target.Name, energy, cost));   // changes every tick, so not logged
                return Math.Max(300, Math.Min(30000, wait));
            }

            if (sinceMasteryRead >= 10)
            {
                int m = vision.ReadMastery(f, onScreen.Top);
                sinceMasteryRead = 0;
                if (m >= 0 && m != Mastery(target)) { progress[target.Name] = m; JobCatalog.SaveProgress(progress); }
                if (mode == MasteryThenXp && m >= 100) { Log.Write(target.Name + " has gold mastery, moving on"); target = null; return 100; }
            }

            if (!vision.ButtonLit(f, onScreen.Top))
            {
                game.MoveMouse(vision.Park.X, vision.Park.Y);   // DO JOB darkens under the mouse
                notLit++;
                State = "Waiting for the button";
                if (notLit == 3) return CheckWhyNotLit(f);
                if (notLit >= 6) { notLit = 0; onScreen = null; }
                Status(target.Name + ": DO JOB isn't lit yet");
                return 1000;
            }
            notLit = 0;
            return DoJob(f, energy, cost);
        }

        // Reads the level now and then. A new value only counts once it has been read the same way
        // twice, can't be impossible, and (once the level is known) is at most a few levels up.
        void UpdateLevel(Frame f)
        {
            if (level > 0 && (game.Now - levelReadAt).TotalSeconds < 30) return;
            levelReadAt = game.Now;
            int lv = vision.ReadLevel(f);
            if (lv <= 0 || lv > JobsVision.MaxLevel) return;
            if (lv == level) { pendingLevel = -1; return; }
            if (level > 0 && (lv < level || lv > level + 3)) { pendingLevel = -1; return; }
            if (lv != pendingLevel) { pendingLevel = lv; levelReadAt = DateTime.MinValue; return; }   // re-read next tick
            if (level > 0) Log.Write("Level " + lv + " reached");
            else Log.Write("Your level is " + lv);
            level = lv;
            pendingLevel = -1;
        }

        // DO JOB stayed dark with enough energy. Reads what the button says to find out why.
        int CheckWhyNotLit(Frame f)
        {
            string text = vision.ButtonText(f, onScreen.Top);
            if (text.Contains("LEVEL"))
            {
                Skip(target, 30, target.Name + " is still locked (your level may have been misread)");
                level = -1;   // read it again from scratch
                return 500;
            }
            if (text.Contains("MAX"))
            {
                Skip(target, 30, target.Name + " needs more max energy than you have");
                return 500;
            }
            Status(target.Name + ": DO JOB isn't lit yet");
            return 1000;
        }

        void Skip(JobInfo job, int minutes, string why)
        {
            skipUntil[job.Name] = game.Now.AddMinutes(minutes);
            Log.Write(why + ". Skipping it for " + minutes + " minutes.");
            if (job == target) { target = null; onScreen = null; }
        }

        int DoJob(Frame beforeFrame, int energyBefore, int cost)
        {
            int xpBefore, xpMaxBefore;
            if (!vision.ReadXp(beforeFrame, out xpBefore, out xpMaxBefore)) xpBefore = -1;
            Rectangle b = vision.ButtonAt(onScreen.Top);
            SafeClick(b.X + b.Width / 2, b.Y + b.Height / 2, b.Width / 5, b.Height / 5);
            game.MoveMouse(vision.Park.X, vision.Park.Y);
            game.Sleep(rng.Next(700, 1000));

            // Confirm it worked: energy dropped, or (if a notice covers the energy bar) the XP bar went up.
            Frame after = game.Capture();
            int energy = -1, max = 0, xpNow, xpMaxNow;
            bool energyRead = after != null && vision.ReadEnergy(after, out energy, out max);
            bool xpUp = after != null && xpBefore >= 0 && vision.ReadXp(after, out xpNow, out xpMaxNow) && (xpNow != xpBefore || xpMaxNow != xpMaxBefore);
            bool done = energyRead ? energy <= energyBefore - cost + 1 : xpUp;
            if (!energyRead) { energy = Math.Max(0, energyBefore - cost); max = KnownMax; }   // tracked, not seen
            if (after != null && done)
            {
                failed = 0;
                sinceMasteryRead++;
                int m = Mastery(target);
                if (m >= 0 && m < 100)
                {
                    progress[target.Name] = m + 1;
                    JobCatalog.SaveProgress(progress);
                    if (m + 1 == 25 || m + 1 == 50 || m + 1 == 100) sinceMasteryRead = 99;   // confirm the new medal
                }
                // What the XP bar says this job really gave. Checked on every press; if it differs from
                // what's known (a boost started or ended), the new value is used from now on.
                int xpAfter, xpMaxAfter;
                int gained = vision.ReadXp(after, out xpAfter, out xpMaxAfter) ? XpGained(xpBefore, xpMaxBefore, xpAfter, xpMaxAfter, target) : -1;
                if (gained > 0 && gained != XpOf(target))
                {
                    Log.Write(string.Format("{0} gives {1:N0} XP (job list says {2:N0}), using {1:N0} from now on", target.Name, gained, target.Xp));
                    measuredXp[target.Name] = gained;
                    JobCatalog.SaveMeasuredXp(measuredXp);
                }
                int earned = gained > 0 ? gained : XpOf(target);
                Interlocked.Add(ref xpEarned, earned);
                JobsDone++;
                EnergyAfterJob = energy;
                EnergyMax = max;
                EnergyAfterJobAt = DateTime.Now;
                State = "Running";
                Status(string.Format("Did {0} (+{1:N0} XP)", target.Name, earned));
                Log.Write(string.Format("Did {0} (+{1:N0} XP), energy {2} -> {3}", target.Name, earned, energyBefore, energy));
                return 150;
            }

            failed++;
            onScreen = null;
            State = "Job didn't register, retrying";
            Log.Write("Pressed DO JOB on " + target.Name + " but energy didn't drop");
            if (failed >= 3) { failed = 0; SetStatus("Three jobs in a row didn't register. Pausing for 30 seconds."); return 30000; }
            return 1000;
        }

        // XP per hour on the current job: energy per hour (from the regen rate) times XP per energy.
        // Without a regen rate, what has actually been earned since starting.
        void UpdateXpPerHour()
        {
            if (target != null && regenSeconds > 0) XpPerHour = 3600.0 / regenSeconds * XpOf(target) / Cost(target);
            else
            {
                double hours = (DateTime.Now - StartedAt).TotalHours;
                XpPerHour = hours > 0.03 ? xpEarned / hours : 0;
            }
        }

        // In most-XP mode: the first job, if any, whose mastery is unknown but which would beat the
        // current pick if it turned out to have the silver discount. Each is looked up once.
        JobInfo MasteryWorthChecking(JobInfo pick, int maxEnergy)
        {
            if (mode != XpOnly || pick == null) return null;
            double best = (double)XpOf(pick) / Cost(pick);
            foreach (JobInfo j in jobs)
            {
                if (j == pick || Mastery(j) >= 0 || masteryLooked.Contains(j.Name)) continue;
                if (j.Level > level || JobCatalog.Cost(j, 100) > maxEnergy) continue;
                DateTime until;
                if (skipUntil.TryGetValue(j.Name, out until) && game.Now < until) continue;
                if ((double)XpOf(j) / JobCatalog.Cost(j, 100) > best) return j;
            }
            return null;
        }

        // Scrolls to the job, reads its mastery and remembers it, so the next pick uses real costs.
        int LookUpMastery(JobInfo job)
        {
            State = "Checking job masteries";
            JobName = "";
            masteryLooked.Add(job.Name);
            onScreen = null;
            target = null;
            JobRow row = ScrollTo(job);
            Frame f = row == null ? null : game.Capture();
            int m = f == null ? -1 : vision.ReadMastery(f, row.Top);
            if (m >= 0)
            {
                progress[job.Name] = m;
                JobCatalog.SaveProgress(progress);
                Log.Write(string.Format("Checked {0}: mastery {1}/100, so it costs {2} energy", job.Name, m, Cost(job)));
            }
            else Log.Write("Couldn't read the mastery of " + job.Name + ", so counting it at full price");
            return 100;
        }

        JobInfo Choose(int maxEnergy)
        {
            if (mode == MasteryThenXp)
            {
                foreach (JobInfo j in jobs)
                {
                    if (!Usable(j, maxEnergy)) continue;
                    if (Mastery(j) < 100) return j;
                }
                mode = XpOnly;
                Log.Write("Every job you can do has gold mastery. Switching to most XP per hour.");
            }
            JobInfo best = null;
            double bestRatio = 0;
            foreach (JobInfo j in jobs)
            {
                if (!Usable(j, maxEnergy)) continue;
                double ratio = (double)XpOf(j) / Cost(j);
                if (best == null || ratio > bestRatio + 1e-9 || (Math.Abs(ratio - bestRatio) < 1e-9 && XpOf(j) > XpOf(best))) { best = j; bestRatio = ratio; }
            }
            return best;
        }

        // Unlocked, affordable with full energy, and not being skipped.
        bool Usable(JobInfo j, int maxEnergy)
        {
            if (j.Level > level || Cost(j) > maxEnergy) return false;
            DateTime until;
            return !skipUntil.TryGetValue(j.Name, out until) || game.Now >= until;
        }

        bool StillThere(Frame f)
        {
            if (titleShot == null) return false;
            Rectangle t = vision.TitleAt(onScreen.Top);
            return Templates.Matches(titleShot, f, t.X, t.Y);
        }

        // Hovers the energy bar and reads the regen tooltip, since the rate differs per player.
        void MeasureRegen()
        {
            State = "Checking energy regen";
            regenCheckedAt = game.Now;
            Point c = vision.EnergyCentre;
            double seconds = 0;
            for (int attempt = 0; attempt < 2 && seconds <= 0; attempt++)
            {
                game.MoveMouse(c.X + attempt * 3, c.Y);
                game.Sleep(1200);
                Frame f = game.Capture();
                if (f != null) seconds = vision.ReadRegen(f);
            }
            game.MoveMouse(vision.Park.X, vision.Park.Y);
            if (seconds > 0 && seconds != regenSeconds) Log.Write("Energy comes back at 1 every " + seconds + " sec");
            if (seconds <= 0 && regenSeconds <= 0) Log.Write("Couldn't read the energy regen rate, so checking energy every 3 seconds instead");
            if (seconds > 0) regenSeconds = seconds;
        }

        // Brings the job's card into view with the mouse wheel.
        // Every job has an address: "N notches down from the top of the list", learned the first time
        // it's found and saved (job-scroll.txt). Going to a remembered job is just: scroll to the top
        // (overshooting there is harmless), then exactly N notches down. Only a job with no address,
        // or whose address missed, is searched for. Null if it can't be brought into view.
        JobRow ScrollTo(JobInfo job)
        {
            int notches;
            if (scrollMemory.TryGetValue(job.Name, out notches))
            {
                if (fromTop < 0) ScrollHome();
                if (notches != fromTop) Wheel(notches - fromTop);
                Frame f = WaitStill();
                if (f != null)
                    foreach (JobRow r in vision.ReadRows(f))
                        if (r.Job == job && r.InView) return r;
                scrollMemory.Remove(job.Name);
                SaveScrollMemory();
                Log.Write("The remembered spot for " + job.Name + " was off, finding it again");
            }
            return SearchFor(job);
        }

        // Finds a job with no address yet, counting notches from the top, then remembers the count.
        JobRow SearchFor(JobInfo job)
        {
            if (fromTop < 0) ScrollHome();
            Rectangle list = vision.List;
            double wanted = list.Top + list.Height / 2.0 - vision.Pitch / 2.0;
            List<JobRow> before = null;
            int lastTicks = 0;
            for (int attempt = 0; attempt < 14; attempt++)
            {
                Frame f = WaitStill();
                if (f == null) return null;
                List<JobRow> rows = vision.ReadRows(f);
                foreach (JobRow r in rows)
                    if (r.Job == job && r.InView)
                    {
                        if (fromTop >= 0)
                        {
                            scrollMemory[job.Name] = fromTop;
                            SaveScrollMemory();
                        }
                        return r;
                    }
                LearnWheel(before, rows, lastTicks);
                before = rows;

                // How far the list has to move, in pixels: positive means scroll down.
                double move;
                JobRow anchor = Nearest(rows, job);
                if (anchor != null)
                {
                    lastSeen = anchor.Job;
                    move = anchor.Top + (job.Position - anchor.Job.Position) * vision.Pitch - wanted;
                }
                else
                {
                    // Only headings in view: head towards the job from wherever a job was last seen.
                    bool down = lastSeen == null ? job.Index >= jobs.Count / 2 : job.Position > lastSeen.Position;
                    move = down ? list.Height * 0.6 : -list.Height * 0.6;
                }

                int ticks;
                if (pixelsPerTick > 0) ticks = (int)Math.Round(move / pixelsPerTick);
                else ticks = 3 * Math.Sign(move);   // notch size unknown: a small test move to measure it
                if (ticks == 0) ticks = Math.Sign(move);
                ticks = Math.Max(-250, Math.Min(250, ticks));
                if (ticks == 0) return null;
                Wheel(ticks);
                lastTicks = ticks;
            }
            return null;
        }

        readonly Dictionary<string, int> scrollMemory;
        int fromTop = -1;   // notches scrolled down from the top of the list; -1 = unknown

        void SaveScrollMemory() { JobCatalog.SaveScroll(vision.P.W, vision.P.H, scrollMemory); }

        void Wheel(int ticks)
        {
            game.Wheel(vision.Park.X, vision.Park.Y, ticks);
            if (fromTop >= 0) fromTop = Math.Max(0, fromTop + ticks);   // the list can't go above its top
        }

        // Scrolls up until the list stops moving: then it's at the very top, the starting point for
        // every job's address.
        void ScrollHome()
        {
            State = "Finding the job";
            Frame last = WaitStill();
            for (int round = 0; round < 8 && last != null; round++)
            {
                game.Wheel(vision.Park.X, vision.Park.Y, -120);
                Frame now = WaitStill();
                if (now == null) return;
                if (SameList(last, now)) { fromTop = 0; return; }
                last = now;
            }
        }

        // After scrolling, waits until two looks in a row show the list in the same place, so it's never
        // read mid-movement (which is what made it overshoot and scroll back and forth).
        Frame WaitStill()
        {
            Frame previous = game.Capture();
            for (int i = 0; i < 15 && previous != null; i++)
            {
                game.Sleep(110);
                Frame now = game.Capture();
                if (now == null) return null;
                if (SameList(previous, now)) return now;
                previous = now;
            }
            return previous;
        }

        bool SameList(Frame a, Frame b)
        {
            Rectangle l = vision.List;
            // A strip down the middle of the list is plenty to see whether it moved.
            Rectangle strip = new Rectangle(l.X + l.Width / 4, l.Y, l.Width / 2, l.Height);
            return Templates.Score(a.Crop(strip), b, strip.X, strip.Y, 6) < 6;
        }
        // Measures how far one wheel notch scrolled, from a job seen before and after the scroll.
        void LearnWheel(List<JobRow> before, List<JobRow> after, int ticks)
        {
            if (before == null || ticks == 0) return;
            foreach (JobRow a in before)
                foreach (JobRow b in after)
                    if (a.Job == b.Job && a.Top != b.Top)
                    {
                        double measured = Math.Abs(a.Top - b.Top) / (double)Math.Abs(ticks);
                        pixelsPerTick = pixelsPerTick <= 0 ? measured : (pixelsPerTick + measured) / 2;
                        return;
                    }
        }

        static JobRow Nearest(List<JobRow> rows, JobInfo job)
        {
            JobRow best = null;
            foreach (JobRow r in rows)
                if (best == null || Math.Abs(r.Job.Index - job.Index) < Math.Abs(best.Job.Index - job.Index)) best = r;
            return best;
        }

        // Refuses any click outside the DO JOB column inside the list.
        void SafeClick(int x, int y, int jitterX, int jitterY)
        {
            Rectangle b = vision.ButtonAt(0), list = vision.List;
            if (x < b.Left || x >= b.Right || y < list.Top || y > list.Bottom)
                throw new InvalidOperationException(string.Format("Blocked a click outside the DO JOB buttons at {0},{1}", x, y));
            game.Click(x + rng.Next(-jitterX, jitterX + 1), y + rng.Next(-jitterY, jitterY + 1));
        }

        // Logs a detailed explanation once when a kind of problem starts, not on every retry.
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

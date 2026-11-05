using System;
using System.Drawing;

namespace StaminaDonator
{
    // Runs any mix of the ticked features in one loop, by priority, checked about every 5 seconds:
    //   1. Stamina donator: stamina >= Give per click + reserve -> Family > Perks, one donation.
    //   2. Auto jobs: enough energy for the next job -> Jobs page, do jobs until energy runs short.
    //   3. Auto deposit: the rest of the time, sit on the Bank page depositing after every payout.
    // Popups (the deposit and donation notices) cover the stamina and energy bars, so at START it reads
    // both bars, hovers each for its regen rate, and from then on predicts them (ResourceClock),
    // correcting whenever a bar is readable. Without auto deposit, auto jobs waits on the Jobs page.
    public class Scheduler
    {
        readonly IGame game;
        readonly ITextReader reader;
        readonly JobBot jobs;               // each may be null when that feature isn't ticked
        readonly StaminaBot stamina;
        readonly StaminaVision staminaVision;
        readonly BankBot bank;
        readonly Settings settings;
        readonly ResourceClock energyClock = new ResourceClock("Energy"), staminaClock = new ResourceClock("Stamina");
        bool staminaDone, started;
        int lastJobsDone;
        DateTime nextDonationTry = DateTime.MinValue, nextJobsTry = DateTime.MinValue, lastPlanLog = DateTime.MinValue;

        public Scheduler(IGame game, ITextReader reader, JobBot jobs, StaminaBot stamina, StaminaVision staminaVision, BankBot bank, Settings settings)
        {
            this.game = game;
            this.reader = reader;
            this.jobs = jobs;
            this.stamina = stamina;
            this.staminaVision = staminaVision;
            this.bank = bank;
            this.settings = settings;
        }

        // True when there's nothing left to do (only the donator was ticked, and every perk is maxed).
        public bool Done { get { return jobs == null && bank == null && stamina != null && stamina.Finished; } }

        int StaminaNeeded { get { return settings.GiveAmount + settings.KeepReserve; } }

        public int Tick()
        {
            if (stamina != null && !staminaDone && stamina.Finished)
            {
                staminaDone = true;
                if (jobs != null || bank != null) Log.Write("Every selected perk is at level 25; carrying on with the other features.");
            }

            // Donating alone: the donator stays on Family > Perks and waits for stamina itself.
            if (jobs == null && bank == null) return stamina.Tick();
            // Jobs (and maybe stamina) without the bank: jobs waits on its own page, as before.
            if (bank == null && stamina == null) return jobs.Tick();

            if (!started) { if (jobs != null || stamina != null) MeasureAtStart(); started = true; }

            Frame f = game.Capture();
            if (f == null) return 2000;
            UpdateClocks(f);
            DateTime now = DateTime.Now;

            // 0. The auto-buy withdrawal is on a fixed clock: from 45 seconds before it until the
            //    deposit after the restock, stay on the Bank. (Attack notices are watched on every page.)
            if (bank != null)
            {
                bank.CheckForAttack(f);
                if (bank.NeedsAttention(now))
                {
                    if (jobs != null) jobs.State = "Auto-buy withdrawal";
                    return Math.Min(bank.Tick(), 1000);
                }
            }

            // 1. Stamina
            if (stamina != null && !staminaDone && now >= nextDonationTry && staminaClock.Predict(now) >= StaminaNeeded) return DonateOnce();

            // 2. Jobs
            int cost = -1;
            if (jobs != null)
            {
                cost = jobs.NextJobCost(f, energyClock.Max);
                if (bank == null) { Feed(); return Math.Min(jobs.Tick(), 15000); }   // jobs + stamina: wait on the Jobs page
                if (now >= nextJobsTry && cost > 0 && energyClock.Predict(now) >= cost) return DoJobs();
            }

            // 3. Deposit
            LogPlan(cost);
            if (jobs != null) jobs.State = "Depositing cash";
            return Math.Min(bank.Tick(), 5000);
        }

        // Gives the bots the tracked stamina and energy, which they use when a notice covers the bars.
        void Feed()
        {
            DateTime now = DateTime.Now;
            if (stamina != null) stamina.KnownStamina = staminaClock.Predict(now);
            if (jobs != null) { jobs.KnownEnergy = energyClock.Predict(now); jobs.KnownMax = energyClock.Max; }
        }

        // At START: find the HEALTH label, read both bars, then hover each for its regen rate.
        void MeasureAtStart()
        {
            Point park = jobs != null ? jobs.Park : staminaVision != null ? staminaVision.ScrollPoint : new Point(0, 0);
            game.MoveMouse(park.X, park.Y);   // nothing hovered, so no tooltip is covering the top bar

            // 1. Find the HEALTH label (the donator's landmark) and read both bars, before any hovering:
            //    the regen tooltips open over the HEALTH row.
            Frame f = null;
            if (staminaVision != null)
            {
                Log.Write("Looking for the HEALTH label in the top bar");
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    f = game.Capture();
                    if (f != null && staminaVision.Analyze(f, settings.MaxStamina).Hud) break;
                    f = null;
                    game.Sleep(1000);
                }
                if (f == null) Log.Write("Couldn't find the HEALTH label yet; carrying on and will keep looking");
            }
            if (f == null) f = game.Capture();
            Log.Write("Reading stamina and energy");
            if (f != null) UpdateClocks(f);

            // 2. Hover each bar for its regen rate, then move away and let the tooltip close.
            Log.Write("Hovering the bars for their regen rates");
            if (jobs != null)
            {
                energyClock.RegenSeconds = ResourceClock.MeasureRegen(game, reader, jobs.EnergyBar, park);
                jobs.UseRegen(energyClock.RegenSeconds);
            }
            if (stamina != null) staminaClock.RegenSeconds = ResourceClock.MeasureRegen(game, reader, staminaVision.StaminaBar, park);

            // 3. Don't go on until the tooltip has closed and HEALTH can be seen again.
            if (staminaVision != null)
            {
                bool seen = false;
                for (int attempt = 0; attempt < 12 && !seen; attempt++)
                {
                    game.Sleep(500);
                    Frame g = game.Capture();
                    seen = g != null && staminaVision.Analyze(g, settings.MaxStamina).Hud;
                    if (!seen && attempt % 3 == 2)
                    {
                        if (attempt == 2) Log.Write("Waiting for the regen tooltip to close");
                        game.MoveMouse(park.X + (attempt % 2 == 0 ? 40 : -40), park.Y + 30);   // a small move, away from the top bar
                    }
                }
                Log.Write(seen ? "HEALTH label found again; starting" : "Still can't see the HEALTH label; carrying on anyway");
            }
            else game.Sleep(800);
            if (jobs != null) Log.Write(Describe(energyClock));
            if (stamina != null) Log.Write(Describe(staminaClock));
        }

        static string Describe(ResourceClock c)
        {
            return string.Format("{0}: {1}/{2}, +1 every {3}", c.Name, c.Known ? c.Predict(DateTime.Now).ToString() : "?", c.Max > 0 ? c.Max.ToString() : "?",
                c.RegenSeconds > 0 ? c.RegenSeconds + " sec" : "? (couldn't read the tooltip; will go by readings only)");
        }

        // Reads whichever bars are readable right now and corrects the clocks; covered bars are skipped.
        void UpdateClocks(Frame f)
        {
            DateTime now = DateTime.Now;
            int value, max;
            TimeSpan? next;
            if (jobs != null)
            {
                if (ResourceClock.ReadBar(reader, f, jobs.EnergyBar, energyClock.Max, out value, out max, out next)) energyClock.Observe(value, max, next, now);
                // A job just done: its energy reading (taken before any popup) is the freshest.
                if (jobs.JobsDone != lastJobsDone && jobs.EnergyAfterJob >= 0)
                {
                    lastJobsDone = jobs.JobsDone;
                    if (jobs.EnergyAfterJobAt > now.AddSeconds(-30)) energyClock.Observe(jobs.EnergyAfterJob, jobs.EnergyMax, null, jobs.EnergyAfterJobAt);
                }
            }
            if (stamina != null && ResourceClock.ReadBar(reader, f, staminaVision.StaminaBar, staminaClock.Max, out value, out max, out next))
                staminaClock.Observe(value, max, next, now);
        }

        // About once a minute while depositing: when it expects to switch over next.
        void LogPlan(int jobCost)
        {
            DateTime now = DateTime.Now;
            if ((now - lastPlanLog).TotalSeconds < 60) return;
            lastPlanLog = now;
            string plan = "Depositing.";
            if (stamina != null && !staminaDone)
            {
                DateTime? s = staminaClock.When(StaminaNeeded, now);
                plan += " Stamina " + StaminaNeeded + " expected " + (s == null ? "at an unknown time" : "at " + s.Value.ToString("HH:mm:ss")) + ".";
            }
            if (jobs != null && jobCost > 0)
            {
                DateTime? e = energyClock.When(jobCost, now);
                plan += " Energy " + jobCost + " for the next job expected " + (e == null ? "at an unknown time" : "at " + e.Value.ToString("HH:mm:ss")) + ".";
            }
            Log.Write(plan);
        }

        // Family > Perks, one donation (with its 5-second popup wait).
        // Family > Perks, then keeps donating (2 seconds apart) until stamina is below the donation
        // amount, so no stamina is left over. The stamina count drops with every donation.
        int DonateOnce()
        {
            Log.Write(string.Format("Stamina is {0} (tracked); switching to Family > Perks to donate", staminaClock.Predict(DateTime.Now)));
            if (jobs != null) jobs.State = "Donating stamina";
            int before = stamina.Donations, counted = before;
            DateTime giveUp = DateTime.Now.AddSeconds(90);
            while (!stamina.Finished && DateTime.Now < giveUp)
            {
                // Bank withdrawal coming up: stop here and go back for it.
                if (bank != null && bank.NeedsAttention(DateTime.Now)) break;
                int wait;
                try { Feed(); wait = stamina.Tick(); }
                catch (UserActiveException) { wait = 500; }
                if (stamina.Donations != counted)
                {
                    staminaClock.Spend(settings.GiveAmount * (stamina.Donations - counted), DateTime.Now);
                    counted = stamina.Donations;
                    giveUp = DateTime.Now.AddSeconds(60);   // each donation earns more time for the next
                    Frame f = game.Capture();
                    if (f != null) UpdateClocks(f);          // a visible bar beats the count
                    if (staminaClock.Predict(DateTime.Now) < StaminaNeeded) break;   // all used up
                }
                else if (stamina.NotEnoughStamina) break;
                game.Sleep(Math.Min(wait, 3000));
            }
            if (stamina.Donations != before)
                Log.Write(string.Format("Donated {0} times; stamina now {1}", stamina.Donations - before, staminaClock.Predict(DateTime.Now)));
            else if (!stamina.Finished)
            {
                // The prediction was off, or a button stayed grey: re-read before trying again.
                nextDonationTry = DateTime.Now.AddMinutes(stamina.NotEnoughStamina ? 0.5 : 2);
                Log.Write(stamina.NotEnoughStamina ? "Stamina was lower than predicted; will re-check." : "Couldn't donate this time; trying again in 2 minutes.");
            }
            if (jobs != null) jobs.ForgetPosition();
            return 200;
        }

        // Jobs page, then jobs until energy runs short for the next one.
        int DoJobs()
        {
            Log.Write(string.Format("Energy is {0} (tracked), enough for the next job; switching to Jobs", energyClock.Predict(DateTime.Now)));
            int before = jobs.JobsDone, counted = before;
            DateTime giveUp = DateTime.Now.AddMinutes(2);
            while (DateTime.Now < giveUp)
            {
                if (bank != null && bank.NeedsAttention(DateTime.Now)) break;   // bank withdrawal coming up
                int wait;
                try { Feed(); wait = jobs.Tick(); }
                catch (UserActiveException) { wait = 500; }
                if (jobs.JobsDone != counted)
                {
                    // The energy left after each job, so the next one is judged on the real amount.
                    counted = jobs.JobsDone;
                    if (jobs.EnergyAfterJob >= 0) energyClock.Observe(jobs.EnergyAfterJob, jobs.EnergyMax, null, jobs.EnergyAfterJobAt);
                }
                if (jobs.WaitingForEnergy) break;   // energy used up for now
                game.Sleep(Math.Min(wait, 3000));
            }
            if (jobs.JobsDone == before)
            {
                nextJobsTry = DateTime.Now.AddMinutes(1);   // didn't manage one: don't flip straight back
                Log.Write("Couldn't do a job this time; back to depositing, trying again in a minute.");
            }
            jobs.ForgetPosition();
            return 200;
        }
    }
}

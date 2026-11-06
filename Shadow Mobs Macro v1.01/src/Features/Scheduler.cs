using System;
using System.Drawing;

namespace StaminaDonator
{
    // Runs any mix of the ticked features in one loop, by priority, checked about every 5 seconds:
    //   0. Auto deposit's auto-buy withdrawal, when one is due (it's on a fixed clock).
    //   1. Auto jobs: enough energy for the next job -> Jobs page, do jobs until energy runs short.
    //   2. Auto-train pet: training due (every 20 minutes) -> Safehouse > Animals > Animal Training.
    //   3. Stamina donator: stamina >= Give per click + reserve -> Family > Perks, donate until it's used up.
    //   4. Auto deposit the rest of the time (or, without it, wait on the Jobs page / until the next thing).
    //
    // Stamina is read ONCE, at START: its value, the "(+1 0:31)" countdown and the regen rate from the
    // tooltip. From then on it's counted (ResourceClock), never read again, because the donation and
    // deposit notices cover the bar. Energy is counted the same way, but a clear energy reading is still
    // used to correct it, since jobs report the energy they leave.
    public class Scheduler
    {
        readonly IGame game;
        readonly ITextReader reader;
        readonly JobBot jobs;               // each may be null when that feature isn't ticked
        readonly StaminaBot stamina;
        readonly StaminaVision staminaVision;
        readonly PetBot pet;
        readonly BankBot bank;
        readonly Settings settings;
        readonly ResourceClock energyClock = new ResourceClock("Energy"), staminaClock = new ResourceClock("Stamina");
        bool staminaDone, started;
        int lastJobsDone;
        DateTime nextDonationTry = DateTime.MinValue, nextJobsTry = DateTime.MinValue, lastPlanLog = DateTime.MinValue;

        // What it's doing, for the status panel's big line when auto jobs isn't ticked (with auto jobs,
        // the job bot's own State is shown, and this is copied to it).
        public volatile string State = "Starting";

        void SetState(string state)
        {
            State = state;
            if (jobs != null) jobs.State = state;
        }

        public Scheduler(IGame game, ITextReader reader, JobBot jobs, StaminaBot stamina, StaminaVision staminaVision,
            PetBot pet, BankBot bank, Settings settings)
        {
            this.game = game;
            this.reader = reader;
            this.jobs = jobs;
            this.stamina = stamina;
            this.staminaVision = staminaVision;
            this.pet = pet;
            this.bank = bank;
            this.settings = settings;
        }

        // True when there's nothing left to do (only the donator was ticked, and every perk is maxed).
        public bool Done { get { return jobs == null && bank == null && pet == null && stamina != null && stamina.Finished; } }

        int StaminaNeeded { get { return settings.GiveAmount + settings.KeepReserve; } }

        public int Tick()
        {
            if (stamina != null && !staminaDone && stamina.Finished)
            {
                staminaDone = true;
                if (!Done) Log.Write("Every selected perk is at level 25; carrying on with the other features.");
            }
            if (!started) { MeasureAtStart(); started = true; }

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
                    SetState("Auto-buy withdrawal");
                    return Math.Min(bank.Tick(), 1000);
                }
            }

            // 1. Jobs
            int cost = -1;
            if (jobs != null)
            {
                cost = jobs.NextJobCost(f, energyClock.Max);
                if (now >= nextJobsTry && cost > 0 && energyClock.Predict(now) >= cost) return DoJobs();
            }

            // 2. Pet (its training costs a little energy; without auto jobs the energy isn't known, so it
            //    just goes when the timer is up and the game decides)
            if (pet != null && pet.Due(now) && (!energyClock.Known || energyClock.Predict(now) >= PetBot.EnergyCost)) return TrainPet();

            // 3. Stamina
            if (stamina != null && !staminaDone && now >= nextDonationTry && staminaClock.Predict(now) >= StaminaNeeded) return DonateOnce();

            // 4. Deposit, or wait
            LogPlan(cost);
            if (bank != null)
            {
                SetState("Depositing cash");
                return Math.Min(bank.Tick(), 5000);
            }
            if (jobs != null) { Feed(); return Math.Min(jobs.Tick(), 15000); }   // waits on the Jobs page
            State = "Waiting";
            return WaitForNext(now);
        }

        // Nothing to do and nowhere to wait: sleep until the next thing is due (at most 30 seconds).
        int WaitForNext(DateTime now)
        {
            DateTime next = now.AddSeconds(30);
            if (stamina != null && !staminaDone)
            {
                DateTime? s = staminaClock.When(StaminaNeeded, now);
                if (s != null && s.Value < next) next = s.Value;
            }
            if (pet != null && pet.NextTrain < next) next = pet.NextTrain;
            return (int)Math.Max(500, (next - now).TotalMilliseconds + 200);
        }

        // Gives the bots the tracked stamina and energy, which they use instead of reading the bars.
        void Feed()
        {
            DateTime now = DateTime.Now;
            if (stamina != null) stamina.KnownStamina = staminaClock.Predict(now);
            if (jobs != null) { jobs.KnownEnergy = energyClock.Predict(now); jobs.KnownMax = energyClock.Max; }
        }

        // At START: find the HEALTH label, read the bars (stamina for the only time), then hover each for
        // its regen rate, and wait for the tooltip to close.
        void MeasureAtStart()
        {
            if (stamina == null && jobs == null) return;   // nothing to measure (deposit / pet only)
            Point park = jobs != null ? jobs.Park : staminaVision.ScrollPoint;
            game.MoveMouse(park.X, park.Y);   // nothing hovered, so no tooltip is covering the top bar

            // 1. HEALTH, then the bars. A notice may be covering them, so keep trying (up to 30 s):
            //    this is the one time stamina is read.
            if (staminaVision != null) Log.Write("Looking for the HEALTH label in the top bar");
            string reading = stamina != null && jobs != null ? "Reading stamina and energy" : stamina != null ? "Reading stamina" : "Reading energy";
            SetState(reading);
            Log.Write(reading);
            DateTime giveUp = DateTime.Now.AddSeconds(30);
            bool staminaRead = stamina == null, energyRead = jobs == null;
            while (DateTime.Now < giveUp && !(staminaRead && energyRead))
            {
                Frame f = game.Capture();
                if (f != null && (staminaVision == null || staminaVision.Analyze(f, settings.MaxStamina).Hud))
                {
                    int value, max;
                    TimeSpan? next;
                    if (!staminaRead && ResourceClock.ReadBar(reader, f, staminaVision.StaminaBar, 0, out value, out max, out next))
                    {
                        staminaClock.Observe(value, max, next, DateTime.Now);
                        staminaRead = true;
                    }
                    if (!energyRead && ResourceClock.ReadBar(reader, f, jobs.EnergyBar, 0, out value, out max, out next))
                    {
                        energyClock.Observe(value, max, next, DateTime.Now);
                        energyRead = true;
                    }
                }
                if (!(staminaRead && energyRead)) game.Sleep(1000);
            }
            if (!staminaRead) Log.Write("Couldn't read the stamina bar (something was covering it); will read it as soon as it's clear");

            // 2. Hover each bar for its regen rate (tried twice), then move away and let the tooltip close.
            Log.Write("Hovering the bars for their regen rates");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (jobs != null && energyClock.RegenSeconds <= 0)
                {
                    energyClock.RegenSeconds = ResourceClock.MeasureRegen(game, reader, jobs.EnergyBar, park);
                    jobs.UseRegen(energyClock.RegenSeconds);
                }
                if (stamina != null && staminaClock.RegenSeconds <= 0)
                    staminaClock.RegenSeconds = ResourceClock.MeasureRegen(game, reader, staminaVision.StaminaBar, park);
            }

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
                c.RegenSeconds > 0 ? c.RegenSeconds + " sec" : "? (couldn't read the tooltip)");
        }

        // Corrects the energy count from a clear energy reading (and from the reading each job leaves).
        // Stamina is NOT re-read: only if START couldn't read it at all, or couldn't get its regen rate.
        void UpdateClocks(Frame f)
        {
            DateTime now = DateTime.Now;
            int value, max;
            TimeSpan? next;
            if (jobs != null)
            {
                // The energy a job left first, then a reading of the bar now (newer, so it wins).
                if (jobs.JobsDone != lastJobsDone && jobs.EnergyAfterJob >= 0)
                {
                    lastJobsDone = jobs.JobsDone;
                    if (jobs.EnergyAfterJobAt > now.AddSeconds(-30)) energyClock.Observe(jobs.EnergyAfterJob, jobs.EnergyMax, null, jobs.EnergyAfterJobAt);
                }
                if (ResourceClock.ReadBar(reader, f, jobs.EnergyBar, energyClock.Max, out value, out max, out next)) energyClock.Observe(value, max, next, now);
            }
            bool staminaIncomplete = !staminaClock.Known || staminaClock.RegenSeconds <= 0;
            if (stamina != null && staminaIncomplete && ResourceClock.ReadBar(reader, f, staminaVision.StaminaBar, staminaClock.Max, out value, out max, out next))
            {
                bool first = !staminaClock.Known;
                staminaClock.Observe(value, max, next, now);
                if (first) Log.Write(Describe(staminaClock));
            }
        }

        // About once a minute while depositing/waiting: when it expects to switch over next.
        void LogPlan(int jobCost)
        {
            DateTime now = DateTime.Now;
            if ((now - lastPlanLog).TotalSeconds < 60) return;
            lastPlanLog = now;
            string plan = bank != null ? "Depositing." : "Waiting.";
            if (stamina != null && !staminaDone)
            {
                DateTime? s = staminaClock.When(StaminaNeeded, now);
                plan += string.Format(" Stamina {0} (tracked); {1} expected {2}.", staminaClock.Predict(now), StaminaNeeded,
                    s == null ? "at an unknown time" : "at " + s.Value.ToString("HH:mm:ss"));
            }
            if (jobs != null && jobCost > 0)
            {
                DateTime? e = energyClock.When(jobCost, now);
                plan += " Energy " + jobCost + " for the next job expected " + (e == null ? "at an unknown time" : "at " + e.Value.ToString("HH:mm:ss")) + ".";
            }
            if (pet != null) plan += " Pet training at " + pet.NextTrain.ToString("HH:mm:ss") + ".";
            Log.Write(plan);
        }

        // Family > Perks, then keeps donating (2 seconds apart) until stamina is below the donation
        // amount, so none is left over. The count drops with every confirmed donation.
        int DonateOnce()
        {
            Log.Write(string.Format("Stamina is {0} (tracked); switching to Family > Perks to donate", staminaClock.Predict(DateTime.Now)));
            SetState("Donating stamina");
            int before = stamina.Donations, counted = before;
            stamina.GreyButtonSeen = false;
            DateTime giveUp = DateTime.Now.AddSeconds(90);
            while (!stamina.Finished && DateTime.Now < giveUp)
            {
                if (bank != null && bank.NeedsAttention(DateTime.Now)) break;   // bank withdrawal coming up
                int wait;
                try { Feed(); wait = stamina.Tick(); }
                catch (UserActiveException) { wait = 500; }
                if (stamina.Donations != counted)
                {
                    staminaClock.Spend(settings.GiveAmount * (stamina.Donations - counted), DateTime.Now);
                    counted = stamina.Donations;
                    giveUp = DateTime.Now.AddSeconds(60);   // each donation earns more time for the next
                    if (staminaClock.Predict(DateTime.Now) < StaminaNeeded) break;   // all used up
                }
                else if (stamina.NotEnoughStamina) break;
                game.Sleep(Math.Min(wait, 3000));
            }
            if (stamina.GreyButtonSeen)
            {
                // The game showed GIVE greyed out, so there's less than the count said: correct it.
                stamina.GreyButtonSeen = false;
                staminaClock.Observe(Math.Min(staminaClock.Predict(DateTime.Now), settings.GiveAmount - 1), staminaClock.Max, null, DateTime.Now);
                Log.Write(string.Format("GIVE {0} was greyed out, so stamina is below {0}; corrected the count to {1}", settings.GiveAmount, staminaClock.Predict(DateTime.Now)));
            }
            if (stamina.Donations != before)
                Log.Write(string.Format("Donated {0} times; stamina now {1} (tracked)", stamina.Donations - before, staminaClock.Predict(DateTime.Now)));
            else if (!stamina.Finished && !stamina.NotEnoughStamina)
            {
                nextDonationTry = DateTime.Now.AddMinutes(2);
                Log.Write("Couldn't donate this time; trying again in 2 minutes.");
            }
            if (jobs != null) jobs.ForgetPosition();
            return 200;
        }

        // Jobs page, then jobs until energy runs short for the next one.
        int DoJobs()
        {
            Log.Write(string.Format("Energy is {0} (tracked), enough for the next job; switching to Jobs", energyClock.Predict(DateTime.Now)));
            State = "Doing jobs";   // the job bot sets its own, more detailed State
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
                Log.Write("Couldn't do a job this time; trying again in a minute.");
            }
            jobs.ForgetPosition();
            return 200;
        }

        // Safehouse > Animals > Animal Training > TRAIN (or read how long it's resting), then close.
        int TrainPet()
        {
            SetState("Training the pet");
            int before = pet.Trained;
            try { pet.TrainOnce(); }
            catch (UserActiveException) { return 500; }
            if (pet.Trained != before) energyClock.Spend(PetBot.EnergyCost, DateTime.Now);
            if (jobs != null) jobs.ForgetPosition();
            return 200;
        }
    }
}

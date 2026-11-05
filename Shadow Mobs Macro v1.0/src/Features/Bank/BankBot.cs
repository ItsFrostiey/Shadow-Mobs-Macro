using System;
using System.Drawing;

namespace StaminaDonator
{
    // Auto deposit: keeps cash on hand in the bank so other players can't take it.
    // Cash arrives in a payout every 5 seconds. The bot presses DEPOSIT ALL, watches the ON HAND
    // number for the moment it next changes (a payout), and from then on presses DEPOSIT ALL right
    // after every payout, 5 seconds apart. Each payout it sees re-times the rhythm, so it never drifts.
    //
    // Auto-buy withdrawal (optional): the shop restocks on every round 5 minutes of the clock. 10 seconds
    // before each restock it withdraws the set amount for the auto-buy pass, holds off depositing until
    // 3 seconds after the restock, then deposits what's left. If you were attacked in the last 60 seconds
    // it skips that restock; if you're attacked while holding the cash, it deposits straight away.
    //
    // The only things it clicks are DEPOSIT ALL, the Amount box, WITHDRAW and the BANK menu button.
    public class BankBot
    {
        const int PayoutMs = 5000;
        const int WithdrawBeforeRestockMs = 10000, DepositAfterRestockMs = 3000;

        readonly IGame game;
        readonly ITextReader reader;
        readonly Settings settings;
        readonly Rectangle heading, onHand, deposit, bankButton;
        readonly Rectangle? amountBox, withdrawButton;
        readonly Random rng = new Random();

        public Action<string> Status = delegate { };
        public int Deposits;

        bool synced;
        DateTime nextPayout;
        int missed;
        DateTime lastAttack = DateTime.MinValue, lastAttackCheck = DateTime.MinValue;
        DateTime handledRestock = DateTime.MinValue;   // the restock already withdrawn for (or skipped)
        DateTime holdUntil = DateTime.MinValue;        // no deposits before this (auto-buy is spending)

        public BankBot(IGame game, ITextReader reader, Profile profile, Settings settings)
        {
            this.game = game;
            this.reader = reader;
            this.settings = settings;
            heading = profile.Box("heading");
            onHand = profile.Box("onhand");
            deposit = profile.Box("deposit");
            bankButton = profile.Box("bankbtn");
            Rectangle r;
            if (profile.Boxes.TryGetValue("amount", out r)) amountBox = r;
            if (profile.Boxes.TryGetValue("withdraw", out r)) withdrawButton = r;
            if (WithdrawOn && (amountBox == null || withdrawButton == null))
                Log.Write("Auto-buy withdrawal needs the Amount Box and Withdraw Button calibration steps; calibrate again to turn it on.");
        }

        bool WithdrawOn { get { return settings.AutoBuyWithdraw && settings.WithdrawAmount > 0; } }

        // The next round-5-minutes moment of the clock (the shop restock).
        static DateTime NextRestock(DateTime now)
        {
            DateTime slot = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0);
            return slot.AddMinutes(5);
        }

        // For the scheduler: the bank needs the tool from 45 seconds before a withdrawal until the
        // deposit after the restock, so other features shouldn't start anything long then.
        public bool NeedsAttention(DateTime now)
        {
            if (holdUntil > DateTime.MinValue) return true;   // withdrawn: stay until the deposit after the restock is done
            if (!WithdrawOn || amountBox == null) return false;
            DateTime restock = NextRestock(now);
            return restock != handledRestock && now >= restock.AddMilliseconds(-WithdrawBeforeRestockMs - 45000);
        }

        public int Tick()
        {
            Frame f = game.Capture();
            if (f == null) { Status("Can't see the game: " + game.LastError); return 2000; }
            CheckForAttack(f);
            if (!BankSetup.HeadingSaysBank(reader, f, heading))
            {
                // Payouts keep their 5-second rhythm while the tool is away, so the timing is kept.
                Status("Opening the Bank page");
                Log.Write("Trying to click BANK");
                ClickBox(bankButton, 6);
                return 1200;
            }

            DateTime now = game.Now;

            // Holding withdrawn cash for the auto-buy: deposit 3 seconds after the restock, or at once
            // if attacked meanwhile.
            if (holdUntil > DateTime.MinValue)
            {
                bool attacked = (now - lastAttack).TotalSeconds < 5;
                if (now >= holdUntil || attacked)
                {
                    holdUntil = DateTime.MinValue;
                    Log.Write(attacked ? "Attacked while holding the auto-buy cash: depositing it now" : "Restock done; depositing what's left");
                    DepositAll();
                    return 300;
                }
                Status(string.Format("Holding cash for the auto-buy restock ({0:0}s)", (holdUntil - now).TotalSeconds));
                return (int)Math.Min(1000, Math.Max(100, (holdUntil - now).TotalMilliseconds));
            }

            // Time to withdraw for the auto-buy?
            if (WithdrawOn && amountBox != null && withdrawButton != null)
            {
                DateTime restock = NextRestock(now);
                DateTime withdrawAt = restock.AddMilliseconds(-WithdrawBeforeRestockMs);
                if (restock != handledRestock && now >= withdrawAt)
                {
                    handledRestock = restock;
                    if ((now - lastAttack).TotalSeconds < 60)
                        Log.Write("Attacked in the last minute, so skipping this auto-buy withdrawal; next one at " + restock.AddMinutes(5).AddMilliseconds(-WithdrawBeforeRestockMs).ToString("HH:mm:ss"));
                    else
                    {
                        Withdraw();
                        holdUntil = restock.AddMilliseconds(DepositAfterRestockMs);
                    }
                    return 300;
                }
                double untilWithdraw = (withdrawAt - now).TotalMilliseconds;
                if (untilWithdraw > 0 && untilWithdraw < 6000) return (int)Math.Max(50, untilWithdraw - 50);   // don't sleep past it
            }

            if (synced)
                while (nextPayout.AddMilliseconds(1500) < now) nextPayout = nextPayout.AddMilliseconds(PayoutMs);
            if (!synced) return Sync();

            double untilPayout = (nextPayout - now).TotalMilliseconds;
            if (untilPayout > 400)
            {
                Status(string.Format("Deposited {0} times. Next payout in {1:0.0}s", Deposits, untilPayout / 1000));
                return (int)Math.Min(untilPayout - 300, MsUntilWithdraw(now));
            }

            // Watch for the payout around when it's due, re-time from it, then deposit.
            DateTime? seen = WatchForPayout(nextPayout.AddMilliseconds(1500));
            if (seen != null) missed = 0;
            else if (++missed >= 3)
            {
                synced = false;
                Log.Write("Didn't see the cash change for 3 payouts in a row, timing it again");
                return 500;
            }
            nextPayout = (seen ?? nextPayout).AddMilliseconds(PayoutMs);
            DepositAll();
            return 0;
        }

        double MsUntilWithdraw(DateTime now)
        {
            if (!WithdrawOn || amountBox == null) return double.MaxValue;
            DateTime restock = NextRestock(now);
            if (restock == handledRestock) return double.MaxValue;
            return Math.Max(50, (restock.AddMilliseconds(-WithdrawBeforeRestockMs) - now).TotalMilliseconds - 50);
        }

        // Deposit all, then wait for the next payout to learn when payouts happen, and deposit that too.
        int Sync()
        {
            Status("Timing the cash payouts");
            DepositAll();
            game.Sleep(400);   // let the ON HAND number update after the deposit
            DateTime? seen = WatchForPayout(game.Now.AddMilliseconds(PayoutMs + 2500));
            if (seen == null)
            {
                Log.Write("Cash on hand didn't change for 7 seconds; trying again");
                return 1000;
            }
            synced = true;
            missed = 0;
            nextPayout = seen.Value.AddMilliseconds(PayoutMs);
            Log.Write("Found the payout timing; depositing right after every payout from now on");
            DepositAll();
            return 0;
        }

        // Watches the ON HAND number until its picture changes (a payout landed) and returns when it did,
        // or null if nothing changed before the deadline. Checks roughly every 50 ms, and looks for an
        // attack notice about once a second meanwhile.
        DateTime? WatchForPayout(DateTime until)
        {
            Frame first = game.Capture();
            if (first == null) return null;
            Frame before = first.Crop(onHand);
            while (game.Now < until)
            {
                game.Sleep(40);
                Frame f = game.Capture();
                if (f == null) continue;
                if (Templates.Score(before, f, onHand.X, onHand.Y, 4) >= 4) return game.Now;
                CheckForAttack(f);
            }
            return null;
        }

        // The red "YOU WERE ATTACKED" notice in the top-right corner. Checked at most once a second.
        public void CheckForAttack(Frame f)
        {
            if ((game.Now - lastAttackCheck).TotalMilliseconds < 1000) return;
            lastAttackCheck = game.Now;
            Rectangle corner = new Rectangle(f.W * 62 / 100, 0, f.W * 38 / 100, f.H * 11 / 100);
            foreach (OcrLine l in reader.Read(f, corner, 1))
                if (l.Text.ToUpperInvariant().Contains("ATTACKED"))
                {
                    if ((game.Now - lastAttack).TotalSeconds > 10) Log.Write("Spotted a \"You were attacked\" notice");
                    lastAttack = game.Now;
                    return;
                }
        }

        void Withdraw()
        {
            Log.Write(string.Format("Withdrawing ${0:N0} for the auto-buy restock at {1:HH:mm}", settings.WithdrawAmount, NextRestock(game.Now)));
            // The Amount box keeps what was typed, so only type it if it doesn't already show the amount.
            string wanted = settings.WithdrawAmount.ToString();
            Frame f = game.Capture();
            if (f != null && AmountBoxShows(f, wanted))
                Log.Write("The Amount box already shows " + wanted + ", so just pressing WITHDRAW");
            else
            {
                ClickBox(amountBox.Value, 4);
                game.Sleep(rng.Next(150, 250));
                game.ClearAndType(wanted);
                game.Sleep(rng.Next(150, 250));
            }
            ClickBox(withdrawButton.Value, 5);
        }

        // The digits in the Amount box (ignoring commas, $ and spaces) match the amount exactly.
        bool AmountBoxShows(Frame f, string digits)
        {
            string text = "";
            foreach (OcrLine l in reader.Read(f, amountBox.Value, 3)) text += l.Text;
            System.Text.StringBuilder d = new System.Text.StringBuilder();
            foreach (char c in Numbers.Fix(text)) if (char.IsDigit(c)) d.Append(c);
            return d.ToString() == digits;
        }

        void DepositAll()
        {
            ClickBox(deposit, 5);
            Deposits++;
            Status(string.Format("Deposited all ({0} times this run)", Deposits));
        }

        // Clicks near the middle of a calibrated box (within 1/spread of its size).
        void ClickBox(Rectangle b, int spread)
        {
            game.Click(b.X + b.Width / 2 + rng.Next(-b.Width / spread, b.Width / spread + 1),
                       b.Y + b.Height / 2 + rng.Next(-b.Height / spread, b.Height / spread + 1));
        }
    }
}

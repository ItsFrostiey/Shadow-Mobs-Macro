using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StaminaDonator
{
    // Kept in settings.txt beside the exe, one "Name=value" per line, so it can be edited by hand.
    public class Settings
    {
        // General
        public bool PauseOnActivity = true;
        public int ResumeSeconds = 5;

        // Stamina donator
        public int GiveAmount = 5;          // 1 or 5
        public int KeepReserve = 0;         // stamina never to spend
        public int MaxStamina = 50;
        public int PollSeconds = 5;
        public List<string> PerkOrder = new List<string>(Perks.Names);
        public HashSet<string> PerksOff = new HashSet<string>();
        public int PerkLevelCap;            // "level all perks to N first" (0 = off), then on to 25

        // Auto jobs: 0 = master every job then most XP per hour, 1 = most XP per hour only
        public int JobMode = 0;
        public bool ShowOverlay = true;      // status panel over the game while auto jobs runs
        public bool HideNotificationTip;     // "Don't show this again" on the notifications tip

        // Which features run when Start is pressed (the tickboxes in the menu).
        public bool UseStamina = true;
        public bool UseJobs;
        public bool UseBank;
        public bool UsePet;

        // Auto-train pet feeding, on every visit, while a portion fits (premium first if both are ticked).
        public bool PetFeed10;   // FEED 10% (food) at 90% hunger or less
        public bool PetFeed50;   // FEED 50% (premium food) at 50% hunger or less

        // Auto deposit: withdraw this much 10 seconds before every shop restock, for the auto-buy pass.
        public bool AutoBuyWithdraw;
        public long WithdrawAmount;

        static string FilePath { get { return App.PathOf("settings.txt"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FilePath)) return s;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                int eq = raw.IndexOf('=');
                if (eq < 0) continue;
                string key = raw.Substring(0, eq).Trim(), value = raw.Substring(eq + 1).Trim();
                int n;
                bool isInt = int.TryParse(value, out n);
                switch (key)
                {
                    case "PauseOnActivity": s.PauseOnActivity = value != "false"; break;
                    case "ResumeSeconds": if (isInt && n > 0) s.ResumeSeconds = n; break;
                    case "GiveAmount": if (n == 1 || n == 5) s.GiveAmount = n; break;
                    case "KeepReserve": if (isInt && n >= 0) s.KeepReserve = n; break;
                    case "MaxStamina": if (isInt && n > 0) s.MaxStamina = n; break;
                    case "PollSeconds": if (isInt && n > 0) s.PollSeconds = n; break;
                    case "PerkLevelCap": if (isInt && n >= 0 && n <= 25) s.PerkLevelCap = n; break;
                    case "PerkOrder": s.PerkOrder = MergeOrder(value); break;
                    case "ShowOverlay": s.ShowOverlay = value != "false"; break;
                    case "HideNotificationTip": s.HideNotificationTip = value == "true"; break;
                    case "JobMode": if (n == 0 || n == 1) s.JobMode = n; break;
                    case "UseStamina": s.UseStamina = value == "true"; break;
                    case "UseJobs": s.UseJobs = value == "true"; break;
                    case "UseBank": s.UseBank = value == "true"; break;
                    case "UsePet": s.UsePet = value == "true"; break;
                    case "PetFeed10": s.PetFeed10 = value == "true"; break;
                    case "PetFeed50": s.PetFeed50 = value == "true"; break;
                    case "AutoBuyWithdraw": s.AutoBuyWithdraw = value == "true"; break;
                    case "WithdrawAmount": long amount; if (long.TryParse(value, out amount) && amount >= 0) s.WithdrawAmount = amount; break;
                    case "PerksOff": s.PerksOff = new HashSet<string>(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)); break;
                }
            }
            return s;
        }

        // Keeps the saved order, ignores unknown names, and adds back anything missing.
        static List<string> MergeOrder(string csv)
        {
            List<string> order = new List<string>();
            foreach (string raw in csv.Split(','))
            {
                string name = raw.Trim();
                if (Array.IndexOf(Perks.Names, name) >= 0 && !order.Contains(name)) order.Add(name);
            }
            foreach (string name in Perks.Names) if (!order.Contains(name)) order.Add(name);
            return order;
        }

        public void Save()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("PauseOnActivity=" + (PauseOnActivity ? "true" : "false"));
            b.AppendLine("ResumeSeconds=" + ResumeSeconds);
            b.AppendLine("GiveAmount=" + GiveAmount);
            b.AppendLine("KeepReserve=" + KeepReserve);
            b.AppendLine("MaxStamina=" + MaxStamina);
            b.AppendLine("PollSeconds=" + PollSeconds);
            b.AppendLine("PerkLevelCap=" + PerkLevelCap);
            b.AppendLine("PerkOrder=" + string.Join(",", PerkOrder));
            b.AppendLine("PerksOff=" + string.Join(",", PerksOff));
            b.AppendLine("JobMode=" + JobMode);
            b.AppendLine("ShowOverlay=" + (ShowOverlay ? "true" : "false"));
            b.AppendLine("HideNotificationTip=" + (HideNotificationTip ? "true" : "false"));
            b.AppendLine("UseStamina=" + (UseStamina ? "true" : "false"));
            b.AppendLine("UseJobs=" + (UseJobs ? "true" : "false"));
            b.AppendLine("UseBank=" + (UseBank ? "true" : "false"));
            b.AppendLine("UsePet=" + (UsePet ? "true" : "false"));
            b.AppendLine("PetFeed10=" + (PetFeed10 ? "true" : "false"));
            b.AppendLine("PetFeed50=" + (PetFeed50 ? "true" : "false"));
            b.AppendLine("AutoBuyWithdraw=" + (AutoBuyWithdraw ? "true" : "false"));
            b.AppendLine("WithdrawAmount=" + WithdrawAmount);
            File.WriteAllText(FilePath, b.ToString());
        }
    }
}

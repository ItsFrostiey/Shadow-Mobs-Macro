using System;
using System.Drawing;
using System.Text.RegularExpressions;

namespace StaminaDonator
{
    // Keeps track of a resource that refills over time (energy, stamina) without having to see it.
    // Popups (like the deposit notice) cover the bars, so the scheduler reads a bar when it can and
    // otherwise predicts it: last reading + one point per regen interval, timed from the bar's own
    // "(+1 0:02)" countdown, capped at the maximum.
    public class ResourceClock
    {
        public readonly string Name;
        public double RegenSeconds;          // seconds per point; 0 = unknown
        public int Max;
        int baseValue = -1;
        DateTime baseTime, nextPoint;
        int estimateValue = -1;              // for working out the regen rate when the tooltip couldn't be read
        DateTime estimateTime;

        public ResourceClock(string name) { Name = name; }

        public bool Known { get { return baseValue >= 0; } }

        // A fresh reading. untilNext is the bar's countdown to the next point, if it could be read.
        public void Observe(int value, int max, TimeSpan? untilNext, DateTime now)
        {
            // No regen rate from the tooltip? Work it out from the bar rising between readings.
            if (estimateValue < 0 || value < estimateValue) { estimateValue = value; estimateTime = now; }
            else if (RegenSeconds <= 0 && value - estimateValue >= 2 && (Max <= 0 || value < Max))
            {
                RegenSeconds = Math.Round((now - estimateTime).TotalSeconds / (value - estimateValue));
                Log.Write(string.Format("{0} regen worked out from the bar: +1 every {1} sec", Name, RegenSeconds));
            }
            baseValue = value;
            baseTime = now;
            if (max > 0) Max = max;
            if (untilNext != null) nextPoint = now + untilNext.Value;
            else if (nextPoint <= now && RegenSeconds > 0) nextPoint = now.AddSeconds(RegenSeconds);   // phase unknown: assume a full interval
        }

        public int Predict(DateTime now)
        {
            if (!Known) return -1;
            int v = baseValue;
            if (RegenSeconds > 0 && nextPoint > baseTime && now >= nextPoint)
                v += 1 + (int)((now - nextPoint).TotalSeconds / RegenSeconds);
            return Max > 0 ? Math.Min(v, Max) : v;
        }

        // Points used up (a donation, a job), so the prediction carries on from the lower value.
        public void Spend(int amount, DateTime now)
        {
            if (!Known) return;
            baseValue = Math.Max(0, Predict(now) - amount);
            baseTime = now;
            estimateValue = -1;   // spending breaks the "rising over time" measurement; start it again
            while (RegenSeconds > 0 && nextPoint <= now) nextPoint = nextPoint.AddSeconds(RegenSeconds);
        }

        // When it's expected to reach the target, or null if unknown.
        public DateTime? When(int target, DateTime now)
        {
            int v = Predict(now);
            if (v < 0) return null;
            if (v >= target) return now;
            if (RegenSeconds <= 0) return null;
            DateTime next = nextPoint > now ? nextPoint : now.AddSeconds(RegenSeconds);
            return next.AddSeconds((target - v - 1) * RegenSeconds);
        }

        // Reads a bar's "24 / 50" value and its "(+1 0:31)" countdown. False if the bar can't be read
        // (covered by a popup, say), or the numbers make no sense.
        public static bool ReadBar(ITextReader reader, Frame f, Rectangle bar, int knownMax, out int value, out int max, out TimeSpan? untilNext)
        {
            untilNext = null;
            if (!Numbers.ReadPair(reader, f, bar, out value, out max)) return false;
            if (knownMax > 0 && max != knownMax) return false;   // a misread, or something covering it
            string text = "";
            foreach (OcrLine l in reader.ReadWhite(f, bar, 3)) text += " " + l.Text;
            Match m = Regex.Match(Numbers.Fix(text), @"\+\s*1\s*\(?\s*(\d{1,2})\s*[:.]\s*(\d{2})");
            int mm, ss;
            if (m.Success && int.TryParse(m.Groups[1].Value, out mm) && int.TryParse(m.Groups[2].Value, out ss) && ss < 60)
                untilNext = new TimeSpan(0, mm, ss);
            return true;
        }

        // Hovers a bar and reads its regen tooltip ("ONLINE: +1 every 10 sec"). Seconds per point, or 0.
        public static double MeasureRegen(IGame game, ITextReader reader, Rectangle bar, Point park)
        {
            double seconds = 0;
            for (int attempt = 0; attempt < 2 && seconds <= 0; attempt++)
            {
                game.MoveMouse(bar.X + bar.Width / 2 + attempt * 3, bar.Y + bar.Height / 2);
                game.Sleep(1200);
                Frame f = game.Capture();
                if (f != null) seconds = JobsVision.ReadRegenTooltip(reader, f, bar);
            }
            game.MoveMouse(park.X, park.Y);
            return seconds;
        }
    }
}

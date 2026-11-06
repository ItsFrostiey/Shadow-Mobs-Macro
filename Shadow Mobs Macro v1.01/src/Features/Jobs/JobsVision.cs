using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace StaminaDonator
{
    public class JobRow
    {
        public JobInfo Job;
        public int Top;          // y of the top of the job's name text
        public bool InView;      // its whole DO JOB button is on screen
        public bool Lit;         // DO JOB is yellow: unlocked and enough energy right now
    }

    // Everything that knows what the Jobs page looks like, driven by the jobs calibration.
    public class JobsVision
    {
        public readonly Profile P;
        readonly ITextReader reader;
        readonly List<JobInfo> jobs;
        readonly Rectangle heading, level, energy, list, names, button, mastery, title, park;
        public readonly int Pitch;
        readonly int buttonColor;

        public JobsVision(Profile profile, ITextReader reader, List<JobInfo> jobs)
        {
            P = profile;
            this.reader = reader;
            this.jobs = jobs;
            heading = P.Box("heading");
            level = P.Box("level");
            energy = P.Box("energy");
            list = P.Box("list");
            names = P.Box("names");
            button = P.Box("button");
            mastery = P.Box("mastery");
            title = P.Box("title");
            park = P.Box("park");
            Pitch = Math.Max(1, P.Box("pitch").Height);
            buttonColor = P.Value("buttonColor");
        }

        // The JOBS button in the game's menu, or null on calibrations made before that step existed.
        public Rectangle? JobsButton
        {
            get { Rectangle r; return P.Boxes.TryGetValue("jobsbtn", out r) ? (Rectangle?)r : null; }
        }

        public Rectangle List { get { return list; } }
        public Point Park { get { return park.Location; } }
        public Point EnergyCentre { get { return new Point(energy.X + energy.Width / 2, energy.Y + energy.Height / 2); } }
        public Rectangle ButtonAt(int top) { return new Rectangle(button.X, top + button.Y, button.Width, button.Height); }
        public Rectangle TitleAt(int top) { return new Rectangle(title.X, top + title.Y, title.Width, title.Height); }

        public bool OnJobsPage(Frame f) { return JobsSetup.HeadingSaysJobs(reader, f, heading); }

        public const int MaxLevel = 250;   // comfortably above the last job's unlock level (196)

        // The XP bar ("174337 / 201152 XP"): XP into this level and XP needed. False if it can't be read,
        // or if this calibration predates the XP Bar step.
        public bool ReadXp(Frame f, out int now, out int max)
        {
            now = max = -1;
            Rectangle xp;
            return P.Boxes.TryGetValue("xp", out xp) && Numbers.ReadPair(reader, f, xp, out now, out max);
        }

        public bool HasXpBar { get { return P.Boxes.ContainsKey("xp"); } }

        // Reads the level several ways and takes the most common believable answer. The square's
        // border can read as an extra "1" (137 -> 1371), so an impossible number loses its last digit.
        public int ReadLevel(Frame f)
        {
            Dictionary<int, int> votes = new Dictionary<int, int>();
            int best = -1, bestVotes = 0;
            foreach (int raw in Numbers.ReadInts(reader, f, level))
            {
                int n = raw;
                while (n > MaxLevel) n /= 10;
                if (n < 1) continue;
                int v;
                votes.TryGetValue(n, out v);
                votes[n] = ++v;
                if (v > bestVotes) { bestVotes = v; best = n; }
            }
            return best;
        }

        public bool ReadEnergy(Frame f, out int now, out int max) { return Numbers.ReadPair(reader, f, energy, out now, out max); }

        // Reads every job name in view. Slow-ish (one big OCR), so only used while looking for a job.
        public List<JobRow> ReadRows(Frame f)
        {
            List<JobRow> rows = new List<JobRow>();
            Rectangle area = Rectangle.FromLTRB(names.Left, list.Top, names.Right, list.Bottom);
            foreach (OcrLine line in reader.Read(f, area, 2))
            {
                JobInfo j = JobCatalog.Match(jobs, line.Text);
                if (j == null) continue;
                bool seen = false;
                foreach (JobRow r in rows) if (r.Job == j) seen = true;
                if (seen) continue;
                JobRow row = new JobRow();
                row.Job = j;
                row.Top = line.Box.Top;
                Rectangle b = ButtonAt(row.Top);
                row.InView = b.Top >= list.Top + 2 && b.Bottom <= list.Bottom - 2;
                row.Lit = row.InView && ButtonLit(f, row.Top);
                rows.Add(row);
            }
            rows.Sort(delegate(JobRow a, JobRow b) { return a.Top.CompareTo(b.Top); });
            return rows;
        }

        // Samples 20 points along the button's top and bottom margins, away from its text and icon.
        // Lit means most of them are the calibrated yellow (allowing for the slightly darker hover
        // shade). Greyed out (not enough energy, max energy too low, locked) is a dull olive, far off.
        public bool ButtonLit(Frame f, int top)
        {
            Rectangle b = ButtonAt(top);
            int iy = Math.Max(2, b.Height / 7), hits = 0, total = 0;
            for (int i = 0; i < 10; i++)
            {
                int x = b.Left + b.Width * (2 * i + 1) / 20;
                foreach (int y in new[] { b.Top + iy, b.Bottom - 1 - iy })
                {
                    total++;
                    if (Px.Near(f.Get(x, y), buttonColor, 40)) hits++;
                }
            }
            return hits * 10 >= total * 7;
        }

        // What the button area says: "DO JOB", "LEVEL 176", "READY IN 4:44", "MAX ENERGY TOO LOW".
        public string ButtonText(Frame f, int top)
        {
            Rectangle b = ButtonAt(top);
            Rectangle area = Rectangle.FromLTRB(b.Left, b.Top, b.Right, b.Bottom + b.Height / 2);
            StringBuilder text = new StringBuilder();
            foreach (OcrLine l in reader.Read(f, area, 2)) text.Append(' ').Append(l.Text);
            return text.ToString().ToUpperInvariant();
        }

        // Completions from "Mastery 26/50 to Silver" (26) or "GOLD MASTERY" (100). -1 if unreadable.
        public int ReadMastery(Frame f, int top)
        {
            Rectangle area = new Rectangle(mastery.X, top + mastery.Y, mastery.Width, mastery.Height);
            foreach (int k in new[] { 3, 4 })
                foreach (bool white in new[] { true, false })
                {
                    StringBuilder text = new StringBuilder();
                    foreach (OcrLine l in white ? reader.ReadWhite(f, area, k) : reader.Read(f, area, k)) text.Append(' ').Append(l.Text);
                    string t = text.ToString();
                    Match m = Regex.Match(Numbers.Fix(t), @"(\d+)\s*/\s*(25|50|100)");
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return Math.Min(100, n);
                    if (Fuzzy.Distance(t, "GOLD MASTERY") < 0.3) return 100;
                }
            return -1;
        }

        // Seconds per energy point from the tooltip shown while hovering the energy bar
        // ("ONLINE: +1 every 10 sec"). 0 if it can't be read.
        public double ReadRegen(Frame f) { return ReadRegenTooltip(reader, f, energy); }

        public Rectangle EnergyBar { get { return energy; } }

        // The regen tooltip shown while hovering a bar (energy or stamina): seconds per point, or 0.
        public static double ReadRegenTooltip(ITextReader reader, Frame f, Rectangle bar)
        {
            // The tooltip opens below the bar, a little to the left: read a generous area around there.
            Rectangle area = Rectangle.FromLTRB(bar.Left - bar.Width * 3 / 2, bar.Bottom - bar.Height / 2, bar.Right + bar.Width / 4, bar.Bottom + bar.Height * 8);
            double fallback = 0;
            foreach (OcrLine l in reader.Read(f, area, 2))
            {
                Match m = Regex.Match(l.Text, @"every\s*([0-9OoIl]+)\s*(s|m)", RegexOptions.IgnoreCase);
                int n;
                if (!m.Success || !int.TryParse(Numbers.Fix(m.Groups[1].Value), out n) || n <= 0) continue;
                double seconds = m.Groups[2].Value.ToLowerInvariant() == "m" ? n * 60 : n;
                if (l.Text.ToUpperInvariant().Contains("ONLINE")) return seconds;
                if (fallback == 0) fallback = seconds;
            }
            return fallback;
        }

        public string Describe(Frame f)
        {
            StringBuilder b = new StringBuilder();
            b.Append("Jobs page: " + (OnJobsPage(f) ? "yes" : "NO"));
            b.Append(". Level: " + ReadLevel(f));
            int e, max;
            b.Append(". Energy: " + (ReadEnergy(f, out e, out max) ? e + "/" + max : "can't read"));
            int xn, xm;
            b.Append(". XP: " + (!HasXpBar ? "not calibrated (calibrate again to add it)" : ReadXp(f, out xn, out xm) ? xn + "/" + xm : "can't read"));
            foreach (JobRow r in ReadRows(f))
            {
                b.Append(". " + r.Job.Name);
                if (!r.InView) b.Append(" (button off screen)");
                else b.Append(" (" + (r.Lit ? "lit" : "grey") + ", mastery " + ReadMastery(f, r.Top) + ")");
            }
            return b.ToString();
        }
    }
}

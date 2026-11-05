using System;
using System.Collections.Generic;
using System.IO;

namespace StaminaDonator
{
    public class JobInfo
    {
        public string Name;
        public int Index;        // 0 = top of the list
        public int Level;        // unlock level
        public int Energy;       // base cost, before any mastery discount
        public int Xp;
        public double Position;  // how far down the list its card starts, in job-card heights, headings included
    }

    // The fixed job list from data/jobs.txt, plus how far each job's mastery has got
    // (job-progress.txt beside the exe, so it survives restarts).
    public static class JobCatalog
    {
        // Heights of the list's headings, in job-card heights (from the user's screenshots).
        const double TierHeading = 0.36, AreaBanner = 0.7;

        public static List<JobInfo> Load()
        {
            string path = App.PathOf("data", "jobs.txt");
            if (!File.Exists(path)) throw new InvalidOperationException("data\\jobs.txt is missing. It needs to sit beside the exe.");
            List<JobInfo> jobs = new List<JobInfo>();
            double position = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("==")) { position += AreaBanner; continue; }
                if (line.StartsWith("--")) { position += TierHeading; continue; }
                string[] p = line.Split('|');
                int level, energy, xp;
                if (p.Length < 4 || !int.TryParse(p[1].Trim(), out level) || !int.TryParse(p[2].Trim(), out energy) || !int.TryParse(p[3].Trim(), out xp)) continue;
                JobInfo j = new JobInfo();
                j.Name = p[0].Trim();
                j.Index = jobs.Count;
                j.Level = level;
                j.Energy = energy;
                j.Xp = xp;
                j.Position = position;
                position += 1;
                jobs.Add(j);
            }
            if (jobs.Count == 0) throw new InvalidOperationException("data\\jobs.txt has no jobs in it.");
            return jobs;
        }

        // From silver (50 done) a job costs 5% less energy, rounded. Gold adds nothing more.
        public static int Cost(JobInfo job, int mastery)
        {
            if (mastery < 50) return job.Energy;
            return (int)Math.Round(job.Energy * 0.95, MidpointRounding.AwayFromZero);
        }

        // Which job a line of OCR text is, or null.
        public static JobInfo Match(List<JobInfo> jobs, string text)
        {
            JobInfo best = null;
            double bestDistance = 0.25;
            foreach (JobInfo j in jobs)
            {
                double d = Fuzzy.Distance(text, j.Name);
                if (d < bestDistance) { bestDistance = d; best = j; }
            }
            return best;
        }

        static string ProgressPath { get { return App.PathOf("job-progress.txt"); } }

        // Name -> completions, 0 to 100. Jobs never seen aren't in it.
        public static Dictionary<string, int> LoadProgress()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            if (!File.Exists(ProgressPath)) return d;
            foreach (string line in File.ReadAllLines(ProgressPath))
            {
                int eq = line.LastIndexOf('=');
                int n;
                if (eq > 0 && int.TryParse(line.Substring(eq + 1), out n)) d[line.Substring(0, eq)] = Math.Max(0, Math.Min(100, n));
            }
            return d;
        }

        public static void SaveProgress(Dictionary<string, int> progress)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> k in progress) lines.Add(k.Key + "=" + k.Value);
            try { File.WriteAllLines(ProgressPath, lines.ToArray()); }
            catch (IOException) { }
        }
        // How many mouse-wheel notches down from the top of the list each job sits, learned the first
        // time each job is found. Per screen size (profiles/WxH/job-scroll.txt), as notch size can differ.
        static string ScrollPath(int w, int h) { return Path.Combine(Profile.Folder(w, h), "job-scroll.txt"); }

        public static Dictionary<string, int> LoadScroll(int w, int h)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            string path = ScrollPath(w, h);
            if (!File.Exists(path)) return d;
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.LastIndexOf('=');
                int n;
                if (eq > 0 && int.TryParse(line.Substring(eq + 1), out n) && n >= 0) d[line.Substring(0, eq)] = n;
            }
            return d;
        }

        public static void SaveScroll(int w, int h, Dictionary<string, int> notches)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> k in notches) lines.Add(k.Key + "=" + k.Value);
            try
            {
                Directory.CreateDirectory(Profile.Folder(w, h));
                File.WriteAllLines(ScrollPath(w, h), lines.ToArray());
            }
            catch (IOException) { }
        }

        // XP actually gained per job, measured from the XP bar (boosts included). job-xp.txt.

        static string XpPath { get { return App.PathOf("job-xp.txt"); } }

        public static Dictionary<string, int> LoadMeasuredXp()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            if (!File.Exists(XpPath)) return d;
            foreach (string line in File.ReadAllLines(XpPath))
            {
                int eq = line.LastIndexOf('=');
                int n;
                if (eq > 0 && int.TryParse(line.Substring(eq + 1), out n) && n > 0) d[line.Substring(0, eq)] = n;
            }
            return d;
        }

        public static void SaveMeasuredXp(Dictionary<string, int> xp)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> k in xp) lines.Add(k.Key + "=" + k.Value);
            try { File.WriteAllLines(XpPath, lines.ToArray()); }
            catch (IOException) { }
        }
    }
}
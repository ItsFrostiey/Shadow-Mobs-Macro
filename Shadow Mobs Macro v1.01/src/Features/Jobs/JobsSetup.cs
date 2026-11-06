using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Calibration for auto jobs, done on a screenshot of the Jobs page scrolled to the very top.
    // Everything with words is read as text; the only colour kept is the lit DO JOB button, re-taken
    // whenever this is calibrated again. The list is scrolled with the mouse wheel (dragging the
    // scrollbar doesn't work in this game), so the scrollbar isn't calibrated.
    public static class JobsSetup
    {
        public const string Feature = "jobs";
        public const string Instructions = "Open the game on the Jobs page, scrolled right to the top, with at least 1 energy so Keep Watch on the Corner's DO JOB button is lit.";

        static readonly Dictionary<string, string> Why = new Dictionary<string, string>
        {
            { "heading", "Confirms the Jobs page is open. If it isn't, the tool opens Jobs from the sidebar by itself." },
            { "level", "Your level decides which jobs are unlocked, so the tool picks the right job straight from its job list instead of reading the whole page." },
            { "energy", "Read as text (like 30 / 90): your energy now and your max. Jobs above your max are skipped, and the tool waits exactly as long as the next job needs." },
            { "xp", "Read before and after every job, so XP per hour and session XP use what you really earn (boosts like +25% included), not just the job list's numbers." },
            { "name1", "Every job card is measured from its name. Names are read as text, so a new theme won't break it; this teaches the tool where they sit." },
            { "button1", "Where DO JOB is on each card, and what lit yellow looks like, so a greyed-out job (not enough energy, or max energy too low) is never clicked." },
            { "mastery1", "Where the Mastery text is, so the tool can read your progress (like 26/50 to Silver) on the way to gold." },
            { "name2", "The gap between two names is the height of one job card. With it the tool can work out where any job is in the list." },
            { "list", "The area the tool reads job names from and scrolls with the mouse wheel. For safety it never clicks outside it." },
            { "jobsbtn", "Where the tool clicks to open the Jobs page, from any other page (such as Family, when running both)." }
        };

        // Shown in a big amber box on steps where a mistake is easy and costly. (None at the moment.)
        static readonly Dictionary<string, string> Warnings = new Dictionary<string, string>();

        public static CalibrationStep[] Steps(ITextReader reader, List<JobInfo> jobs)
        {
            CalibrationStep[] steps = CalibrationStep.Describe(Feature, AllSteps(reader, jobs), Why);
            foreach (CalibrationStep s in steps)
            {
                string w;
                if (Warnings.TryGetValue(s.Key, out w)) s.Warning = w;
            }
            return steps;
        }

        // Ticked off by the user before the screenshot is taken.
        public static readonly string[] Checklist =
        {
            "The game is open on the Jobs page",
            "Every area is expanded: no area banner shows a + (collapsed) button",
            "The job list is scrolled right to the very top, with NEW ASHPORT and Keep Watch on the Corner showing",
            "I have at least 1 energy, so Keep Watch on the Corner's DO JOB button is lit yellow"
        };

        static CalibrationStep[] AllSteps(ITextReader reader, List<JobInfo> jobs)
        {
            return new[]
            {
                new CalibrationStep("heading", "Jobs Heading", "Drag a box around the JOBS heading at the top of the page.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        Rectangle t = Snap.Tighten(f, r);
                        if (!HeadingSaysJobs(reader, f, t)) throw new CalibrationException("Couldn't read JOBS there. Drag the box around just that heading.");
                        return t;
                    }),
                new CalibrationStep("level", "Level", "Drag a box around your level: the number in the yellow square on your namecard.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (Numbers.ReadInt(reader, f, r) <= 0) throw new CalibrationException("Couldn't read a number there. Drag the box around just the level number.");
                        return r;
                    }),
                new CalibrationStep("energy", "Energy Bar", "Drag a box around the whole orange energy bar, including the \"30 / 90\" text and its empty part.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        int a, b;
                        if (!Numbers.ReadPair(reader, f, r, out a, out b)) throw new CalibrationException("Couldn't read the energy (like 30 / 90) there. Cover the whole energy bar.");
                        return r;
                    }),
                new CalibrationStep("xp", "XP Bar", "Drag a box around the purple XP bar on your namecard, including the \"174337 / 201152 XP\" text and its empty part.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        int a, b;
                        if (!Numbers.ReadPair(reader, f, r, out a, out b)) throw new CalibrationException("Couldn't read your XP (like 174337 / 201152 XP) there. Cover the whole XP bar.");
                        return r;
                    }),
                new CalibrationStep("name1", "First Job Name", "Drag a box around the name Keep Watch on the Corner.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { Rectangle t = Snap.Tighten(f, r); TitleTop(f, t, reader, jobs, 0); return t; }),
                new CalibrationStep("button1", "First \"Do Job\" Button", "Drag a box inside Keep Watch on the Corner's DO JOB button. It needs to be lit yellow.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Button(f, r); }),
                new CalibrationStep("mastery1", "First Job Mastery Bar", "Drag a box around Keep Watch on the Corner's whole mastery bar, including the Mastery text.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m) { return Snap.Check(f, r); }),
                new CalibrationStep("name2", "Second Job Name", "Drag a box around the name Tag Rival Turf.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        Rectangle t = Snap.Tighten(f, r);
                        TitleTop(f, t, reader, jobs, 1);
                        if (t.Top <= m["name1"].Bottom) throw new CalibrationException("Tag Rival Turf should be below Keep Watch on the Corner. Try again.");
                        return t;
                    }),
                new CalibrationStep("list", "Entire Job List", "Drag one big box over the job list: from just under the JOBS heading, down to the bottom of the game, and across the full width of the job cards.",
                    delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                    {
                        r = Snap.Check(f, r);
                        if (r.Height < f.H / 3) throw new CalibrationException("That box is too short. Cover the whole list, down to the bottom of the game.");
                        foreach (string k in new[] { "name1", "button1", "mastery1", "name2" })
                            if (!r.Contains(m[k])) throw new CalibrationException("That box doesn't cover the first two jobs and the DO JOB button. Make it bigger.");
                        return r;
                    }),
                new CalibrationStep("jobsbtn", "Jobs Button", "Drag a box around the JOBS button in the left-hand menu.", StaminaSetup.MenuButton(reader, "JOBS"))
            };
        }

        public static bool HeadingSaysJobs(ITextReader reader, Frame f, Rectangle heading)
        {
            Rectangle area = Rectangle.Inflate(heading, heading.Height, heading.Height / 2);
            foreach (OcrLine line in reader.Read(f, area, 2))
            {
                string t = line.Text.ToUpperInvariant();
                if (t.Contains("JOB") && !t.Contains("AUTO")) return true;
            }
            return false;
        }

        // Where OCR says a job's name starts. Each job card is measured from this.
        static int TitleTop(Frame f, Rectangle name, ITextReader reader, List<JobInfo> jobs, int index)
        {
            Rectangle area = Rectangle.Inflate(name, name.Height, name.Height / 2);
            foreach (OcrLine line in reader.Read(f, area, 2))
            {
                JobInfo j = JobCatalog.Match(jobs, line.Text);
                if (j != null && j.Index == index) return line.Box.Top;
            }
            throw new CalibrationException("Couldn't read the name " + jobs[index].Name + " there. Drag the box around just the name.");
        }

        public static Profile Build(Frame f, Dictionary<string, Rectangle> m, ITextReader reader, List<JobInfo> jobs)
        {
            Profile p = new Profile(Feature, f.W, f.H);
            foreach (string k in new[] { "heading", "level", "energy", "xp", "list" }) p.Boxes[k] = m[k];

            Rectangle list = m["list"], name1 = m["name1"], button = m["button1"], mastery = m["mastery1"];
            int top1 = TitleTop(f, name1, reader, jobs, 0), top2 = TitleTop(f, m["name2"], reader, jobs, 1);
            if (top2 - top1 < 10) throw new CalibrationException("The two job names are too close together. Calibrate again.");
            p.Boxes["pitch"] = new Rectangle(0, 0, 0, top2 - top1);

            // The strip OCR reads job names from, and each card's parts relative to its name's top.
            p.Boxes["names"] = Rectangle.FromLTRB(Math.Max(list.Left, name1.Left - 8), list.Top, mastery.Right, list.Bottom);
            p.Boxes["button"] = new Rectangle(button.X, button.Y - top1, button.Width, button.Height);
            p.Boxes["mastery"] = new Rectangle(mastery.X, mastery.Y - top1, mastery.Width, mastery.Height);
            p.Boxes["title"] = new Rectangle(name1.X, name1.Y - top1, mastery.Right - name1.X, name1.Height);
            p.Values["buttonColor"] = Px.Modal(f, button);


            // A safe spot for the mouse: the list's left margin, away from every button.
            p.Boxes["park"] = new Rectangle((list.Left + name1.Left) / 2, list.Top + list.Height / 2, 1, 1);
            p.Boxes["jobsbtn"] = m["jobsbtn"];
            return p;
        }
    }
}

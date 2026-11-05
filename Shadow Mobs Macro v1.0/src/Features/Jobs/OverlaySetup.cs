using System;
using System.Collections.Generic;
using System.Drawing;

namespace StaminaDonator
{
    // Where the auto jobs overlay sits on the game, saved like any calibration (per screen size).
    // The box's size sets the text size. Without a calibration it fills the top-left corner, left of
    // the player card and above the sidebar.
    public static class OverlaySetup
    {
        public const string Feature = "overlay";

        public static CalibrationStep[] Steps()
        {
            CalibrationStep s = new CalibrationStep("area", "Overlay Area",
                "Drag a box where you want the auto jobs status panel to appear. The bigger the box, the bigger the text.",
                delegate(Frame f, Rectangle r, Dictionary<string, Rectangle> m)
                {
                    r = Snap.Check(f, r);
                    if (r.Height < 24 || r.Width < 60) throw new CalibrationException("That box is too small to read. Make it at least a little wider and taller.");
                    return r;
                });
            s.Why = "The panel shows what auto jobs is doing, the job, and your XP per hour, right on the game. It doesn't block clicks and the tool never reads it.";
            s.Example = "jobs_overlay";
            return new[] { s };
        }

        public static void Save(Frame f, Rectangle area)
        {
            Profile p = new Profile(Feature, f.W, f.H);
            p.Boxes["area"] = area;
            p.Save();
        }

        // The panel's box inside the game, for this game size.
        public static Rectangle Area(Size game)
        {
            Profile p = Profile.Load(Feature, game.Width, game.Height);
            Rectangle r;
            if (p != null && p.Boxes.TryGetValue("area", out r)) return r;
            // Default: the whole top-left corner, left of the player card and above SAFEHOUSE (it covers
            // the Roblox menu buttons, which is fine: clicks pass through).
            return new Rectangle(0, 0, (int)(game.Width * 0.114), (int)(game.Height * 0.115));
        }
    }
}

using System;

namespace StaminaDonator
{
    // The family perks that take stamina, top to bottom as the game lists them.
    public static class Perks
    {
        public static readonly string[] Names =
        {
            "Enforcers", "Guardians", "Hustlers", "Medics", "Runners",
            "Bagmen", "Insurance", "Veterans", "Demolition", "Boss Hunters"
        };

        // Which perk a line of OCR text is, or -1.
        public static int Match(string text)
        {
            for (int i = 0; i < Names.Length; i++)
                if (Fuzzy.StartsLike(text, Names[i])) return i;
            return -1;
        }
    }
}

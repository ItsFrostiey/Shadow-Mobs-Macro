using System;
using System.Globalization;

namespace StaminaDonator
{
    // Amounts the way the game writes them: 500M, 2B, 1.5T (also plain or comma'd numbers, with or
    // without a $).
    public static class Money
    {
        public static bool TryParse(string text, out long value)
        {
            value = 0;
            string s = (text ?? "").Trim().ToUpperInvariant().Replace("$", "").Replace(",", "").Replace(" ", "");
            if (s.Length == 0) return false;
            double scale = 1;
            char last = s[s.Length - 1];
            if (last == 'K') scale = 1e3;
            else if (last == 'M') scale = 1e6;
            else if (last == 'B') scale = 1e9;
            else if (last == 'T') scale = 1e12;
            if (scale > 1) s = s.Substring(0, s.Length - 1);
            double n;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || n <= 0) return false;
            value = (long)Math.Round(n * scale);
            return value > 0;
        }

        public static string Short(long v)
        {
            if (v >= 1000000000000L) return Trim(v / 1e12) + "T";
            if (v >= 1000000000L) return Trim(v / 1e9) + "B";
            if (v >= 1000000L) return Trim(v / 1e6) + "M";
            if (v >= 1000L) return Trim(v / 1e3) + "K";
            return v.ToString(CultureInfo.InvariantCulture);
        }

        static string Trim(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}

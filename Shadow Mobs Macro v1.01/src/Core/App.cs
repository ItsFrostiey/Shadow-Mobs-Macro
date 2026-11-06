using System;
using System.IO;
using System.Windows.Forms;

namespace StaminaDonator
{
    public static class App
    {
        public const string Version = "1.01";   // also in Program.cs's assembly attributes
        public static readonly string Dir = Path.GetDirectoryName(Application.ExecutablePath);

        public static string PathOf(params string[] parts)
        {
            string[] all = new string[parts.Length + 1];
            all[0] = Dir;
            Array.Copy(parts, 0, all, 1, parts.Length);
            return Path.Combine(all);
        }
    }

    // One line per event, to the window and to log.txt beside the exe.
    public static class Log
    {
        public static event Action<string> Written;
        static readonly object sync = new object();
        static string FilePath { get { return App.PathOf("log.txt"); } }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            lock (sync)
            {
                try { File.AppendAllText(FilePath, line + Environment.NewLine); }
                catch (IOException) { }
            }
            Action<string> w = Written;
            if (w != null) w(message);
        }

        // Keeps the file small: only the last 2000 lines survive a restart.
        public static void Trim()
        {
            try
            {
                if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < 1000000) return;
                string[] lines = File.ReadAllLines(FilePath);
                string[] keep = new string[Math.Min(2000, lines.Length)];
                Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
                File.WriteAllLines(FilePath, keep);
            }
            catch (IOException) { }
        }
    }
}

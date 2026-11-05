using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StaminaDonator
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            // Real pixels on every monitor whatever its Windows scaling (125%, 150%...). Without
            // this, a scaled screen is reported at the wrong size and clicks land in the wrong place.
            bool perMonitor = false;
            try { perMonitor = SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
            if (!perMonitor) SetProcessDPIAware();

            Log.Trim();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

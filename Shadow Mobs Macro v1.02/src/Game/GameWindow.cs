using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace StaminaDonator
{
    public interface IGame
    {
        Frame Capture();                  // null when the game can't be seen; see LastError
        string LastError { get; }
        void Click(int x, int y);         // may throw UserActiveException instead of clicking
        void Wheel(int x, int y, int ticks);  // positive scrolls down; may also throw
        void ClearAndType(string digits);     // into whatever text box has focus; may also throw
        void MoveMouse(int x, int y);     // may also throw
        void Sleep(int ms);               // throws OperationCanceledException when stopped
        DateTime Now { get; }
    }

    // Finds the Roblox window, screenshots it and sends ordinary mouse input to it.
    // All coordinates are pixels inside the game window, whatever monitor it is on.
    public class GameWindow : IGame
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public MOUSEINPUT mi; }

        const int MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004, WHEEL = 0x0800, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        readonly CancellationToken cancel;
        readonly UserActivity activity;
        readonly Size expected;
        readonly Random rng = new Random();
        POINT origin;
        bool wasPaused;

        public string LastError { get; private set; }
        public DateTime Now { get { return DateTime.Now; } }
        public Action<bool> PauseChanged = delegate { };

        // expected = Size.Empty accepts any window size. A null activity never pauses.
        public GameWindow(CancellationToken cancel, UserActivity activity, Size expected)
        {
            this.cancel = cancel;
            this.activity = activity;
            this.expected = expected;
        }

        static IntPtr FindRoblox()
        {
            foreach (string name in new[] { "RobloxPlayerBeta", "Windows10Universal" })
                foreach (Process p in Process.GetProcessesByName(name))
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
            return IntPtr.Zero;
        }

        // The game's size in real pixels, or Size.Empty if it isn't open.
        public static Size DetectSize()
        {
            IntPtr window = FindRoblox();
            if (window == IntPtr.Zero || IsIconic(window)) return Size.Empty;
            RECT r;
            GetClientRect(window, out r);
            return new Size(r.Right - r.Left, r.Bottom - r.Top);
        }

        void Focus(IntPtr window)
        {
            if (IsIconic(window)) ShowWindow(window, 9);
            IntPtr current = GetForegroundWindow();
            if (current == window) return;
            uint me = GetCurrentThreadId();
            uint them = GetWindowThreadProcessId(current, IntPtr.Zero);
            AttachThreadInput(me, them, true);
            SetForegroundWindow(window);
            AttachThreadInput(me, them, false);
            Sleep(250);
        }

        // While the user is active it waits here, so it never pulls the game over their work.
        void WaitWhilePaused()
        {
            if (activity == null) return;
            while (activity.Paused)
            {
                if (!wasPaused) { wasPaused = true; PauseChanged(true); }
                Sleep(200);
            }
            if (wasPaused) { wasPaused = false; PauseChanged(false); }
        }

        void ThrowIfUserActive()
        {
            if (activity != null && activity.Paused) throw new UserActiveException();
        }

        public Frame Capture()
        {
            WaitWhilePaused();
            IntPtr window = FindRoblox();
            if (window == IntPtr.Zero) { LastError = "Roblox isn't running"; return null; }
            Focus(window);
            if (GetForegroundWindow() != window) { LastError = "couldn't bring Roblox to the front"; return null; }
            RECT r;
            GetClientRect(window, out r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) { LastError = "the game window has no size"; return null; }
            if (!expected.IsEmpty && (Math.Abs(w - expected.Width) > 8 || Math.Abs(h - expected.Height) > 8))
            {
                LastError = string.Format("the game is now {0}x{1} but was {2}x{3} when started. Press Start again.", w, h, expected.Width, expected.Height);
                return null;
            }
            POINT p = new POINT();
            ClientToScreen(window, ref p);
            origin = p;
            try
            {
                using (Bitmap b = new Bitmap(w, h))
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(w, h));
                    return Frame.FromBitmap(b);
                }
            }
            catch (Exception e)
            {
                LastError = "the screen can't be captured (is the PC locked?): " + e.Message;
                return null;
            }
        }

        // Clicks like a person: glides to the button over a fraction of a second (slightly curved,
        // easing in), rests on it so the game sees the pointer arrive and the button highlight, then
        // presses and holds briefly before releasing. Roblox can miss presses that come too fast.
        public void Click(int x, int y)
        {
            ThrowIfUserActive();
            GlideTo(x, y);
            Sleep(rng.Next(140, 240));   // rest on the button
            ThrowIfUserActive();
            Send(LEFTDOWN, 0);
            Sleep(rng.Next(80, 140));    // a real press lasts about this long
            Send(LEFTUP, 0);
            Sleep(rng.Next(100, 180));   // don't move away the instant it's released
        }

        // Moves the pointer from where it is to (x, y) in small steps along a gentle curve.
        void GlideTo(int x, int y)
        {
            POINT start;
            GetCursorPos(out start);
            int tx = origin.X + x, ty = origin.Y + y;
            double dx = tx - start.X, dy = ty - start.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(6, Math.Min(22, (int)(distance / 40)));
            double bend = (rng.NextDouble() - 0.5) * Math.Min(60, distance / 6);   // sideways bow of the curve
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double eased = 1 - Math.Pow(1 - t, 3);   // fast at first, slowing into the button
                double side = Math.Sin(Math.PI * t) * bend;
                double px = start.X + dx * eased - (distance > 0 ? dy / distance * side : 0);
                double py = start.Y + dy * eased + (distance > 0 ? dx / distance * side : 0);
                SendAbsolute((int)Math.Round(px), (int)Math.Round(py));
                Sleep(rng.Next(10, 18));
            }
            SendAbsolute(tx, ty);   // land exactly on the spot
        }

        // Glides there rather than jumping, so the game sees the pointer leave whatever it was over
        // (a jump can leave a hover tooltip stuck open).
        public void MoveMouse(int x, int y)
        {
            ThrowIfUserActive();
            GlideTo(x, y);
        }

        public static bool GameInFront()
        {
            IntPtr window = FindRoblox();
            return window != IntPtr.Zero && GetForegroundWindow() == window;
        }

        // Where the game's picture is on screen (for the overlay), or empty if it isn't showing.
        public static Rectangle ClientArea()
        {
            IntPtr window = FindRoblox();
            if (window == IntPtr.Zero || IsIconic(window)) return Rectangle.Empty;
            RECT r;
            GetClientRect(window, out r);
            POINT p = new POINT();
            ClientToScreen(window, ref p);
            return new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public short wVk, wScan; public int dwFlags, time; public IntPtr dwExtraInfo; }
        // The same size as INPUT, which Windows requires.
        [StructLayout(LayoutKind.Sequential)]
        struct KEYINPUT { public int type; public KEYBDINPUT ki; public int padA, padB; }
        [DllImport("user32.dll", EntryPoint = "SendInput")] static extern uint SendKeys(uint n, KEYINPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        const int VK_BACK = 0x08, VK_CONTROL = 0x11, VK_DELETE = 0x2E, KEYUP = 0x0002;

        // Empties the focused text box (select all, delete, plus backspaces in case select-all isn't
        // supported), then types the digits at a person's pace.
        public void ClearAndType(string digits)
        {
            ThrowIfUserActive();
            Key(VK_CONTROL, false); Key('A', false); Key('A', true); Key(VK_CONTROL, true);
            Sleep(rng.Next(60, 100));
            Key(VK_DELETE, false); Key(VK_DELETE, true);
            for (int i = 0; i < 20; i++) { Key(VK_BACK, false); Sleep(15); Key(VK_BACK, true); Sleep(15); }
            Sleep(rng.Next(80, 140));
            foreach (char c in digits)
            {
                ThrowIfUserActive();
                Key(c, false);
                Sleep(rng.Next(40, 80));
                Key(c, true);
                Sleep(rng.Next(50, 110));
            }
        }

        void Key(int vk, bool up)
        {
            KEYINPUT k = new KEYINPUT();
            k.type = 1;
            k.ki.wVk = (short)vk;
            k.ki.wScan = (short)MapVirtualKey((uint)vk, 0);
            k.ki.dwFlags = up ? KEYUP : 0;
            SendKeys(1, new[] { k }, Marshal.SizeOf(typeof(KEYINPUT)));
            if (activity != null) activity.NoteBotInput();
        }

        public void Wheel(int x, int y, int ticks)
        {
            ThrowIfUserActive();
            MoveTo(x, y);
            Sleep(rng.Next(30, 60));
            // One notch per event: games often count a big single event as just one notch.
            for (int i = 0; i < Math.Abs(ticks); i++)
            {
                ThrowIfUserActive();
                Send(WHEEL, ticks > 0 ? -120 : 120);
                Sleep(16);   // about one frame apart, so the game counts every notch
            }
        }

        // Roblox only registers a press after it has seen the pointer arrive, hence the nudge.
        void MoveTo(int x, int y)
        {
            int sx = origin.X + x, sy = origin.Y + y;
            SendAbsolute(sx - 1, sy);
            Sleep(40);
            SendAbsolute(sx, sy);
        }

        // Measured across every monitor, so it works when the game isn't on the main screen.
        void SendAbsolute(int x, int y)
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            INPUT i = new INPUT();
            i.mi.dx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
            i.mi.dy = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
            i.mi.dwFlags = MOVE | ABSOLUTE | VIRTUALDESK;
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
            if (activity != null) activity.NoteBotInput(x, y);
        }

        void Send(int flags, int data)
        {
            INPUT i = new INPUT();
            i.mi.dwFlags = flags;
            i.mi.mouseData = data;
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
            if (activity != null) activity.NoteBotInput();
        }

        public void Sleep(int ms)
        {
            if (cancel.WaitHandle.WaitOne(Math.Max(0, ms))) throw new OperationCanceledException();
        }
    }
}

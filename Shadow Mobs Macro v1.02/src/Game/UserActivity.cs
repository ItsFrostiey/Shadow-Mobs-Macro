using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StaminaDonator
{
    // Thrown instead of clicking when the user is using the PC. The bot then starts over from a
    // fresh look at the screen, because the user may have moved or changed the page.
    public class UserActiveException : Exception { }

    // Notices the user's own mouse and keyboard use by checking for anything the bot didn't do.
    // It only polls the cursor position and last-input time; it installs no input hooks.
    public class UserActivity
    {
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public int cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("kernel32.dll")] static extern uint GetTickCount();

        const int Grace = 400, Slack = 3;   // bot input takes a moment to land, and can round by a pixel

        readonly object sync = new object();
        readonly int resumeMs;
        int expectedX, expectedY;
        uint lastBotInput, lastUserInput, seenInput;
        bool everActive;

        public UserActivity(int resumeSeconds)
        {
            resumeMs = Math.Max(1, resumeSeconds) * 1000;
            POINT c;
            GetCursorPos(out c);
            expectedX = c.X;
            expectedY = c.Y;
            seenInput = LastInput();
            lastBotInput = GetTickCount();
        }

        static uint LastInput()
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO));
            GetLastInputInfo(ref info);
            return info.dwTime;
        }

        public void Poll()
        {
            lock (sync)
            {
                uint now = GetTickCount();
                if (unchecked((int)(now - lastBotInput)) < Grace) return;
                POINT c;
                GetCursorPos(out c);
                int dx = Math.Abs(c.X - expectedX), dy = Math.Abs(c.Y - expectedY);
                expectedX = c.X;
                expectedY = c.Y;
                if (dx > Slack || dy > Slack) MarkActive(now);

                uint input = LastInput();
                if (input != seenInput)
                {
                    seenInput = input;
                    if (unchecked((int)(input - lastBotInput)) > Grace) MarkActive(now);
                }
            }
        }

        void MarkActive(uint now)
        {
            lastUserInput = now;
            everActive = true;
        }

        // Call straight after sending any input.
        public void NoteBotInput(int x, int y)
        {
            lock (sync)
            {
                expectedX = x;
                expectedY = y;
                lastBotInput = GetTickCount();
            }
        }

        public void NoteBotInput()
        {
            lock (sync) lastBotInput = GetTickCount();
        }

        public bool Paused
        {
            get
            {
                Poll();
                lock (sync) return everActive && unchecked((int)(GetTickCount() - lastUserInput)) < resumeMs;
            }
        }

        public void StartPolling(CancellationToken cancel)
        {
            Thread t = new Thread(delegate()
            {
                while (!cancel.WaitHandle.WaitOne(100)) Poll();
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}

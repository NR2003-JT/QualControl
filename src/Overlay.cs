using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QualControl
{
    // Where the board goes and how big: Scale on top of "86% of the game window's height", the top-left corner as
    // a share of the game window (NaN = left edge, centred), and the seconds each full page holds before it scrolls.
    public class BoardLayout
    {
        public double Scale = 1, X = double.NaN, Y = double.NaN, Hold = 3.5;

        public BoardLayout Copy() { return (BoardLayout)MemberwiseClone(); }

        public Point Place(Rectangle game, Size size)
        {
            int x = double.IsNaN(X) ? game.Left + Math.Max(8, game.Width / 50) : game.Left + (int)Math.Round(X * game.Width);
            int y = double.IsNaN(Y) ? game.Top + (game.Height - size.Height) / 2 : game.Top + (int)Math.Round(Y * game.Height);
            Rectangle scr = Screen.FromRectangle(game).Bounds;
            x = Math.Max(scr.Left - size.Width + 40, Math.Min(scr.Right - 40, x));
            y = Math.Max(scr.Top, Math.Min(scr.Bottom - 40, y));
            return new Point(x, y);
        }

        public void Remember(Rectangle game, Point at, Size size)
        {
            X = (double)(at.X - game.Left) / game.Width;
            Y = (double)(at.Y - game.Top) / game.Height;
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public override string ToString()
        {
            return string.Format(Inv, "{0:0.00},{1},{2},{3:0.0}", Scale, double.IsNaN(X) ? "" : X.ToString("0.0000", Inv), double.IsNaN(Y) ? "" : Y.ToString("0.0000", Inv), Hold);
        }

        public static BoardLayout Parse(string v)
        {
            var l = new BoardLayout();
            string[] p = (v ?? "").Split(',');
            double d;
            if (p.Length > 0 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, Inv, out d) && d > 0.2 && d < 3) l.Scale = d;
            if (p.Length > 1 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, Inv, out d)) l.X = d;
            if (p.Length > 2 && double.TryParse(p[2], System.Globalization.NumberStyles.Float, Inv, out d)) l.Y = d;
            if (p.Length > 3 && double.TryParse(p[3], System.Globalization.NumberStyles.Float, Inv, out d) && d >= 0.5 && d <= 30) l.Hold = d;
            return l;
        }
    }

    // A window that sits over the game without taking the focus or the mouse.
    public class OverlayForm : Form
    {
        protected readonly bool clickThrough;
        public OverlayForm(bool clickThrough)
        {
            this.clickThrough = clickThrough;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.Black;
        }

        protected override bool ShowWithoutActivation { get { return clickThrough; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x8;                                    // tool window, topmost
                if (clickThrough) cp.ExStyle |= 0x08000000 | 0x20 | 0x80000; // no activate, transparent to the mouse, layered
                return cp;
            }
        }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        public static Rectangle GameRect(IntPtr h)
        {
            RECT r;
            if (h != IntPtr.Zero && GetWindowRect(h, out r) && r.Right - r.Left > 200 && r.Bottom - r.Top > 200)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return Screen.PrimaryScreen.Bounds;
        }
    }

    // Pausing and resuming NR2003 by pressing its pause key for the player (NR2003 reads the keyboard through
    // DirectInput, so the key goes in as a scan code with the game window in front). The game must run windowed or
    // borderless for the chart to show over it.
    public static class GamePause
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit, Size = 28)]
        struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(4)] public KEYBDINPUT ki; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;

        // "Pause" (default) or a key name Windows knows, such as P or F9.
        public static string KeyName = "Pause";

        // NR2003's own pause and resume (what its pause key runs): pause stamps the time and sets the sim state
        // to 3, resume moves the clocks on by the time paused and sets it back to 2. Called in the game when the
        // code is the build QualControl knows; the pause key otherwise.
        const long PauseFn = 0x5464E0, ResumeFn = 0x5465E0;
        static readonly int[] PauseCode = { 0xE8, -1, -1, -1, -1, 0x6A, 0x03, 0xA3, 0xA8, 0xBC, 0xBE, 0x00, 0xE8 };
        static readonly int[] ResumeCode = { 0x53, 0x56, 0x57, 0xE8, -1, -1, -1, -1, 0x8B, 0x15, 0xA8, 0xBC, 0xBE, 0x00 };

        static bool Matches(ProcessMemory mem, long at, int[] code)
        {
            try
            {
                for (int i = 0; i < code.Length; i++) if (code[i] >= 0 && mem.U8(at + i) != code[i]) return false;
                return true;
            }
            catch (GameMemoryException) { return false; }
        }

        // Returns how the game was paused.
        public static string Pause(ProcessMemory mem)
        {
            if (Matches(mem, PauseFn, PauseCode) && mem.CallGame(PauseFn)) return "through its own pause";
            Press(mem.Process);
            return "with the pause key";
        }

        // Resumes and hands the keyboard back to the game so the player drives on.
        public static void Resume(ProcessMemory mem)
        {
            if (mem == null) return;
            if (!(Matches(mem, ResumeFn, ResumeCode) && mem.CallGame(ResumeFn))) { Press(mem.Process); return; }
            try { if (mem.Process.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(mem.Process.MainWindowHandle); } catch { }
        }

        public static void Press(Process game)
        {
            if (game != null && game.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(game.MainWindowHandle);
            ushort scan;
            uint ext = 0;
            if (string.Equals(KeyName, "Pause", StringComparison.OrdinalIgnoreCase)) { scan = 0x45; ext = KEYEVENTF_EXTENDEDKEY; } // DIK_PAUSE
            else
            {
                Keys k;
                if (!Enum.TryParse(KeyName, true, out k)) k = Keys.Pause;
                scan = (ushort)MapVirtualKey((uint)k, 0);
            }
            var down = new INPUT { type = 1, ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | ext } };
            var up = new INPUT { type = 1, ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | ext | KEYEVENTF_KEYUP } };
            SendInput(1, new[] { down }, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(60);
            SendInput(1, new[] { up }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

}

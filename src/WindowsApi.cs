using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace HexBot
{
    // Documented Windows calls live here so the rest of the app reads like ordinary C#.
    internal static class WindowsApi
    {
        // Window and pointer queries used to check a click's destination.
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);

        // Hotkeys work even while the test window is in the foreground.
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);

        // Mouse input is submitted through the normal Windows input API.
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, MouseInputEvent[] events, int size);

        // DPI awareness keeps capture coordinates in physical screen pixels.
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        // A waitable timer gives the background scanner short, cancellable waits.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr callback, IntPtr argument, bool resume);

        // These layouts must match Windows' native structures, including x64 padding.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseData
        {
            public int X, Y;
            public uint WheelData, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInputEvent
        {
            public uint Type;
            public MouseData Mouse;
        }

        // Use per-monitor awareness when supported, with an older Windows fallback.
        public static void EnableDpiAwareness()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch (EntryPointNotFoundException)
            {
                SetProcessDPIAware();
            }
        }

        // Reading the title does not require opening the target process.
        public static string WindowTitle(IntPtr window)
        {
            StringBuilder title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

        // Convert the client rectangle to desktop coordinates, excluding the title bar.
        public static Rectangle ClientBounds(IntPtr window)
        {
            NativeRectangle rectangle;
            Point origin = Point.Empty;
            if (!GetClientRect(window, out rectangle) || !ClientToScreen(window, ref origin))
                return Rectangle.Empty;
            return new Rectangle(origin.X, origin.Y, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        }

        // The scanner uses this instead of reading form properties from another thread.
        public static Rectangle WindowBounds(IntPtr window)
        {
            NativeRectangle rectangle;
            if (!GetWindowRect(window, out rectangle))
                return Rectangle.Empty;
            return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        }

        // Return the physical desktop position used by sampling and click checks.
        public static Point CursorPosition()
        {
            Point point;
            if (!GetCursorPos(out point))
                throw new InvalidOperationException("Could not read the pointer position.");
            return point;
        }

        // The worker polls F7 too, so stopping does not depend on a UI refresh.
        public static bool StopKeyPressed()
        {
            return (GetAsyncKeyState(0x76) & 0x8000) != 0;
        }

        // Focus, client bounds and process identity must still match at click time.
        public static string ClickBlockReason(IntPtr target, uint expectedProcess, Point pointer)
        {
            if (target == IntPtr.Zero || !IsWindow(target))
                return "Focus your test window and press F9.";
            uint actualProcess;
            GetWindowThreadProcessId(target, out actualProcess);
            if (actualProcess != expectedProcess)
                return "The bound window closed. Bind it again with F9.";
            if (IsIconic(target) || GetForegroundWindow() != target)
                return "Waiting for the bound window to be active.";
            if (!ClientBounds(target).Contains(pointer) || GetAncestor(WindowFromPoint(pointer), 2) != target)
                return "Move the pointer inside the bound window.";
            if ((GetAsyncKeyState(0x01) & 0x8000) != 0)
                return "Release the left mouse button.";
            return null;
        }

        // A click is one down/up pair; the pointer is never moved.
        public static bool SendLeftClick()
        {
            MouseInputEvent down = new MouseInputEvent();
            down.Mouse.Flags = 0x0002;
            MouseInputEvent up = new MouseInputEvent();
            up.Mouse.Flags = 0x0004;
            int inputSize = Marshal.SizeOf(typeof(MouseInputEvent));
            uint accepted = SendInput(2, new MouseInputEvent[] { down, up }, inputSize);

            // If only the down event succeeded, make a best effort to release it.
            if (accepted == 1)
                SendInput(1, new MouseInputEvent[] { up }, inputSize);
            return accepted == 2;
        }
    }
}

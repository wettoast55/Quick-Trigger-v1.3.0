using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ColorTriggerLab
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        // The pointer-sized extra-info field also gives INPUT its correct x64 alignment.
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, flags, time;
            public UIntPtr extraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public MOUSEINPUT mouse; }

        public static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }

        public static string Title(IntPtr window)
        {
            StringBuilder title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

        public static Rectangle ClientBounds(IntPtr window)
        {
            RECT rect;
            Point origin = Point.Empty;
            if (!GetClientRect(window, out rect) || !ClientToScreen(window, ref origin)) return Rectangle.Empty;
            return new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        // Check both focus and pointer location: a click must stay inside the bound client.
        // A window handle alone is insufficient because Windows can reuse it after close.
        public static string ClickBlockReason(IntPtr target, uint process, Point cursor)
        {
            uint currentProcess;
            if (target == IntPtr.Zero || !IsWindow(target)) return "Bind your test window with F9 first.";
            GetWindowThreadProcessId(target, out currentProcess);
            if (currentProcess != process) return "The bound window closed. Bind it again with F9.";
            if (IsIconic(target) || GetForegroundWindow() != target) return "Waiting for the bound window to be active.";
            if (!ClientBounds(target).Contains(cursor) || GetAncestor(WindowFromPoint(cursor), 2) != target)
                return "Move the pointer inside the bound window.";
            if ((GetAsyncKeyState(0x01) & 0x8000) != 0) return "Waiting for the left mouse button to be released.";
            return null;
        }

        public static bool LeftClick()
        {
            INPUT down = new INPUT(); down.mouse.flags = 0x0002;
            INPUT up = new INPUT(); up.mouse.flags = 0x0004;
            uint sent = SendInput(2, new INPUT[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            // A partially inserted pair must not leave a synthetic button held down.
            if (sent == 1) SendInput(1, new INPUT[] { up }, Marshal.SizeOf(typeof(INPUT)));
            return sent == 2;
        }
    }
}

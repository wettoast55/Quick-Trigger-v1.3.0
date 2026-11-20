using System;
using System.Drawing;
using System.Windows.Forms;

namespace PopQuiz
{
    // Keep a snapshot of a display's identity and physical desktop coordinates.
    internal sealed class DisplayChoice
    {
        public string DeviceName { get; private set; }
        public Rectangle Bounds { get; private set; }
        public bool IsPrimary { get; private set; }

        // A separate data object also lets geometry tests use synthetic monitor layouts.
        public DisplayChoice(string deviceName, Rectangle bounds, bool primary)
        {
            DeviceName = deviceName;
            Bounds = bounds;
            IsPrimary = primary;
        }

        // Read all attached displays; their origins may be negative or vertically offset.
        public static DisplayChoice[] ConnectedDisplays()
        {
            Screen[] screens = Screen.AllScreens;
            DisplayChoice[] choices = new DisplayChoice[screens.Length];
            for (int index = 0; index < screens.Length; index++)
                choices[index] = new DisplayChoice(screens[index].DeviceName, screens[index].Bounds, screens[index].Primary);
            return choices;
        }

        // A disconnected, moved or resized monitor invalidates the run's coordinate snapshot.
        public static bool StillConnected(string deviceName, Rectangle expectedBounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.DeviceName == deviceName)
                    return screen.Bounds == expectedBounds;
            }
            return false;
        }

        // Use the Windows display device name, resolution and primary marker in the dropdown.
        public override string ToString()
        {
            string name = DeviceName.Substring(DeviceName.LastIndexOf('\\') + 1);
            string label = name + "  -  " + Bounds.Width + " x " + Bounds.Height;
            if (IsPrimary)
                label += "  (primary)";
            return label;
        }
    }

    // Geometry is kept independent of the UI to test negative and mixed-size monitor layouts.
    internal static class DisplayLayout
    {
        // Preserve the requested position when possible, otherwise keep the entire area on screen.
        public static Rectangle Fit(Rectangle area, Rectangle display)
        {
            if (display.Width < 1 || display.Height < 1)
                throw new ArgumentException("The selected display has no usable bounds.");
            int width = Math.Max(1, Math.Min(area.Width, Math.Min(1024, display.Width)));
            int height = Math.Max(1, Math.Min(area.Height, Math.Min(1024, display.Height)));
            int left = Math.Max(display.Left, Math.Min(area.Left, display.Right - width));
            int top = Math.Max(display.Top, Math.Min(area.Top, display.Bottom - height));
            return new Rectangle(left, top, width, height);
        }

        // Center within one chosen display, rather than across the combined virtual desktop.
        public static Rectangle Centered(Rectangle display, int width, int height)
        {
            Rectangle area = new Rectangle(display.X + (display.Width - width) / 2,
                display.Y + (display.Height - height) / 2, width, height);
            return Fit(area, display);
        }
    }
}

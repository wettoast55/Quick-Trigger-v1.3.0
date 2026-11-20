using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PopQuiz
{
    // A short-lived bright green border identifies the exact capture rectangle during setup.
    internal sealed class AreaOverlay : Form
    {
        internal const int LifetimeMs = 3000;
        internal const int FadeStartsMs = 2000;
        internal const double BrightOpacity = 0.95;
        private readonly Timer fadeTimer = new Timer();
        private readonly Stopwatch age = new Stopwatch();

        // No title bar, taskbar entry or activation: this is an indicator, not another control.
        public AreaOverlay()
        {
            Text = "Pop Quiz - Chalkboard";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Lime;
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = BrightOpacity;
            fadeTimer.Interval = 16;
            fadeTimer.Tick += OnFadeTick;
        }

        // Showing the outline must not steal focus from the window the user just bound.
        protected override bool ShowWithoutActivation { get { return true; } }

        // Layered transparency plus transparent/no-activate styles lets mouse input pass through.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000020 | 0x00000080 | 0x08000000;
                return parameters;
            }
        }

        // Reusing the window restarts the three-second lifetime while the user adjusts values.
        public void ShowArea(Rectangle area)
        {
            Bounds = area;
            UpdateBorder();
            Opacity = BrightOpacity;
            if (!Visible)
                Show();
            // Applying exact physical bounds after handle creation avoids initial DPI adjustments.
            Bounds = area;
            UpdateBorder();
            age.Restart();
            fadeTimer.Start();
        }

        // The first two seconds stay bright; the final second fades smoothly to transparent.
        internal static double OpacityAt(double elapsedMs)
        {
            if (elapsedMs <= FadeStartsMs)
                return BrightOpacity;
            if (elapsedMs >= LifetimeMs)
                return 0;
            return BrightOpacity * (LifetimeMs - elapsedMs) / (LifetimeMs - FadeStartsMs);
        }

        // A hollow window region shows a border while leaving the center completely unobstructed.
        internal static Region BorderShape(Size size)
        {
            Region border = new Region(new Rectangle(Point.Empty, size));
            const int thickness = 4;
            if (size.Width > thickness * 2 && size.Height > thickness * 2)
                border.Exclude(new Rectangle(thickness, thickness, size.Width - thickness * 2, size.Height - thickness * 2));
            return border;
        }

        // Replace and dispose the previous shape when a rectangle's dimensions change.
        private void UpdateBorder()
        {
            Region previous = Region;
            Region = BorderShape(ClientSize);
            if (previous != null)
                previous.Dispose();
        }

        // Elapsed time, not tick counts, determines when the outline disappears.
        private void OnFadeTick(object sender, EventArgs e)
        {
            double opacity = OpacityAt(age.Elapsed.TotalMilliseconds);
            if (opacity <= 0)
            {
                Close();
                return;
            }
            Opacity = opacity;
        }

        // Hit testing is also transparent for windows owned by this application.
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084)
            {
                message.Result = new IntPtr(-1);
                return;
            }
            base.WndProc(ref message);
        }

        // Closing early, replacing the preview or exiting always releases the animation timer.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                fadeTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}

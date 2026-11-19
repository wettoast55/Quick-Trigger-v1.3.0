using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HexBot
{
    // A local scene supplies a moving target, a same-color decoy and a click counter.
    internal sealed class DemoForm : Form
    {
        private readonly Timer animationTimer = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Color targetColor = Color.FromArgb(255, 213, 74);
        private bool moving = true;
        private bool showTarget = true;
        private bool showDecoy;
        private int clicks;
        public event EventHandler GeometryChanged;

        // Match the detector's chosen color when the scene is used for calibration.
        public Color TargetColor
        {
            get { return targetColor; }
            set { targetColor = value; Invalidate(); }
        }

        // The dotted box always encloses the same 120 x 120 physical-pixel area.
        public Rectangle ScanRegion
        {
            get
            {
                Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
                return new Rectangle(center.X - 60, center.Y - 60, 120, 120);
            }
        }

        // This UI animation timer affects only the demo, never the scanner.
        public DemoForm()
        {
            Text = "HexBot - Test Scene";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(17, 24, 36);
            ClientSize = new Size(580, 430);
            MinimumSize = new Size(540, 400);
            DoubleBuffered = true;
            KeyPreview = true;
            animationTimer.Interval = 25;
            animationTimer.Tick += OnAnimationTick;
            animationTimer.Start();
        }

        // Repainting calculates the target's position from elapsed time.
        private void OnAnimationTick(object sender, EventArgs e)
        {
            Invalidate();
        }

        // Keyboard switches create predictable positive and negative test cases.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Space: moving = !moving; break;
                case Keys.T: showTarget = !showTarget; break;
                case Keys.D: showDecoy = !showDecoy; break;
                case Keys.R: clicks = 0; break;
            }
            Invalidate();
        }

        // Count every received left-click, including manual clicks used to focus the scene.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                clicks++;
            Invalidate();
        }

        // Tell the main form when the scan box moves on the desktop.
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (GeometryChanged != null)
                GeometryChanged(this, EventArgs.Empty);
        }

        // Resizing also changes the box's center.
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (GeometryChanged != null)
                GeometryChanged(this, EventArgs.Empty);
        }

        // Draw flat, predictable colors so tolerance changes are easy to understand.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int centerX = ClientSize.Width / 2;
            int centerY = ClientSize.Height / 2;
            using (Font title = new Font("Segoe UI", 18, FontStyle.Bold))
                graphics.DrawString("HexBot test scene", title, Brushes.White, 22, 18);
            graphics.DrawString("The target crosses the dotted scan area.", Font, Brushes.LightSlateGray, 24, 52);
            using (Pen border = new Pen(Color.SlateGray))
            {
                border.DashStyle = DashStyle.Dash;
                graphics.DrawRectangle(border, centerX - 61, centerY - 61, 122, 122);
            }
            using (Brush targetBrush = new SolidBrush(targetColor))
            {
                if (showTarget)
                {
                    int offset = 0;
                    if (moving)
                        offset = (int)(Math.Sin(clock.Elapsed.TotalSeconds * 1.4) * (ClientSize.Width / 2 - 70));
                    int targetX = centerX + offset;
                    graphics.FillEllipse(targetBrush, targetX - 12, centerY - 37, 24, 24);
                    graphics.FillRectangle(targetBrush, targetX - 19, centerY - 8, 38, 35);
                    graphics.FillRectangle(targetBrush, targetX - 19, centerY + 24, 13, 24);
                    graphics.FillRectangle(targetBrush, targetX + 6, centerY + 24, 13, 24);
                }
                if (showDecoy)
                    graphics.FillRectangle(targetBrush, centerX - 42, centerY + 36, 18, 18);
            }
            graphics.DrawString("Clicks received: " + clicks, Font, Brushes.White, 24, ClientSize.Height - 74);
            graphics.DrawString("Space: pause motion    T: show/hide target    D: toggle decoy", Font, Brushes.LightSlateGray, 24, ClientSize.Height - 48);
            graphics.DrawString("F6: start/stop    F7: stop    R: reset click counter", Font, Brushes.LightSlateGray, 24, ClientSize.Height - 25);
        }

        // Dispose also handles forms created and closed by tests without being shown.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                animationTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}

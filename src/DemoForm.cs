using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColorTriggerLab
{
    internal sealed class DemoForm : Form
    {
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private int clicks;
        private bool moving = true;
        private bool visible = true;
        private bool decoy;
        private Color target = Color.FromArgb(255, 213, 74);
        public event EventHandler GeometryChanged;
        public Color TargetColor { get { return target; } set { target = value; Invalidate(); } }
        public Rectangle ScanRegion
        {
            get
            {
                Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
                return new Rectangle(center.X - 60, center.Y - 60, 120, 120);
            }
        }

        public DemoForm()
        {
            Text = "Color Trigger Lab - Test Scene";
            BackColor = Color.FromArgb(15, 22, 31);
            ForeColor = Color.FromArgb(225, 232, 243);
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(600, 500);
            MinimumSize = new Size(400, 360);
            DoubleBuffered = true;
            KeyPreview = true;
            timer.Interval = 25;
            timer.Tick += delegate { Invalidate(); };
            timer.Start();
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) moving = !moving;
                if (e.KeyCode == Keys.T) visible = !visible;
                if (e.KeyCode == Keys.D) decoy = !decoy;
                if (e.KeyCode == Keys.R) clicks = 0;
                Invalidate();
            };
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { clicks++; Invalidate(); } };
            LocationChanged += delegate { if (GeometryChanged != null) GeometryChanged(this, EventArgs.Empty); };
            SizeChanged += delegate { if (GeometryChanged != null) GeometryChanged(this, EventArgs.Empty); };
            FormClosed += delegate { timer.Dispose(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = ClientSize.Width / 2, cy = ClientSize.Height / 2;
            using (Pen grid = new Pen(Color.FromArgb(29, 39, 53)))
            {
                for (int x = 0; x < ClientSize.Width; x += 40) g.DrawLine(grid, x, 76, x, ClientSize.Height - 82);
                for (int y = 76; y < ClientSize.Height - 82; y += 40) g.DrawLine(grid, 0, y, ClientSize.Width, y);
            }
            using (Font title = new Font("Segoe UI", 17, FontStyle.Bold)) g.DrawString("Controlled test scene", title, Brushes.White, 22, 18);
            g.DrawString("Target crosses the scan area. Same-color decoys also trigger it.", Font, Brushes.LightSlateGray, 24, 52);
            using (Pen area = new Pen(Color.FromArgb(72, 110, 134), 1)) { area.DashStyle = DashStyle.Dash; g.DrawRectangle(area, cx - 61, cy - 61, 122, 122); }
            if (visible)
            {
                int x = cx + (moving ? (int)(Math.Sin(clock.Elapsed.TotalSeconds * 1.4) * Math.Max(80, ClientSize.Width / 2 - 70)) : 0);
                using (Brush brush = new SolidBrush(target))
                {
                    g.FillEllipse(brush, x - 12, cy - 37, 24, 24);
                    g.FillRectangle(brush, x - 19, cy - 8, 38, 35);
                    g.FillRectangle(brush, x - 19, cy + 24, 13, 24);
                    g.FillRectangle(brush, x + 6, cy + 24, 13, 24);
                }
            }
            if (decoy) using (Brush brush = new SolidBrush(target)) g.FillRectangle(brush, cx - 42, cy + 36, 18, 18);
            using (Pen cross = new Pen(Color.FromArgb(160, 178, 200))) { g.DrawLine(cross, cx - 5, cy, cx + 5, cy); g.DrawLine(cross, cx, cy - 5, cx, cy + 5); }
            string state = "Clicks received: " + clicks + "     Target: " + (visible ? "shown" : "hidden") + "     Decoy: " + (decoy ? "on" : "off");
            g.DrawString(state, Font, Brushes.White, 24, ClientSize.Height - 72);
            g.DrawString("Space: pause motion    T: show/hide target    D: toggle decoy", Font, Brushes.LightSlateGray, 24, ClientSize.Height - 46);
            g.DrawString("F6: start/stop detector    F7: emergency stop    R: reset clicks", Font, Brushes.LightSlateGray, 24, ClientSize.Height - 24);
        }
    }
}

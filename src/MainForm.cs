using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ColorTriggerLab
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Background = Color.FromArgb(15, 20, 28);
        private static readonly Color Surface = Color.FromArgb(24, 32, 44);
        private static readonly Color Muted = Color.FromArgb(155, 172, 194);
        private static readonly Color Accent = Color.FromArgb(99, 222, 185);
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly TriggerGate gate = new TriggerGate();
        private readonly List<TriggerEvent> events = new List<TriggerEvent>();
        private readonly HashSet<int> registeredHotkeys = new HashSet<int>();
        private NumericUpDown left, top, regionWidth, regionHeight, tolerance, minimumPixels, stableFrames, cooldown, interval;
        private TextBox hex;
        private Panel swatch, preview;
        private CheckBox autoClick, repeat;
        private Label status, detection, metrics, binding, footer, eventCount;
        private Button start;
        private ListView eventList;
        private Bitmap frame;
        private Graphics captureGraphics;
        private IntPtr targetWindow;
        private uint targetProcess;
        private DemoForm demo;
        private bool followDemo;
        private bool running;
        private bool updating;
        private long triggerCount;
        private long frameCount;
        private double lastRateTime;
        private double lastUiTime;
        private double scanRate;
        private double lastTriggerTime = double.NegativeInfinity;
        private Color targetColor = Color.FromArgb(255, 213, 74);

        public MainForm()
        {
            Text = "Color Trigger Lab";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Background;
            ForeColor = Color.FromArgb(229, 236, 246);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1080, 790);
            MinimumSize = new Size(920, 690);
            StartPosition = FormStartPosition.CenterScreen;
            BuildInterface();
            SetRegion(CenteredRegion(Screen.PrimaryScreen.Bounds, 120, 120));
            timer.Interval = 33;
            timer.Tick += Scan;
            Shown += delegate { RegisterHotkeys(); };
            FormClosing += delegate {
                timer.Stop();
                foreach (int id in registeredHotkeys) Native.UnregisterHotKey(Handle, id);
                if (demo != null && !demo.IsDisposed) demo.Close();
                if (captureGraphics != null) captureGraphics.Dispose();
                if (frame != null) frame.Dispose();
                timer.Dispose();
            };
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 10), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(root);
            Panel heading = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = "Color Trigger Lab", Location = new Point(0, 0), AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold) };
            Label subtitle = new Label { Text = "Measure how visible colors translate into trigger events.", Location = new Point(2, 45), AutoSize = true, ForeColor = Muted };
            heading.Controls.Add(title); heading.Controls.Add(subtitle); root.Controls.Add(heading, 0, 0);
            start = Button("Start monitoring   F6", delegate { Toggle(); });
            start.Width = 216; start.Height = 40; start.BackColor = Accent; start.ForeColor = Background;
            start.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            heading.Controls.Add(start);
            heading.Resize += delegate { start.Location = new Point(heading.ClientSize.Width - start.Width, 5); };

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 354));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 1);
            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 12, 0) };
            body.Controls.Add(controls, 0, 0);

            AddSection(controls, "01  CAPTURE REGION");
            controls.Controls.Add(ButtonRow(Button("Draw region", PickRegion), Button("Screen center", delegate {
                Stop(); followDemo = false; SetRegion(CenteredRegion(Screen.FromPoint(Cursor.Position).Bounds, (int)regionWidth.Value, (int)regionHeight.Value));
            })));
            left = Number(-32768, 32767, 0); top = Number(-32768, 32767, 0);
            regionWidth = Number(1, 1024, 120); regionHeight = Number(1, 1024, 120);
            controls.Controls.Add(NumberPair("Left", left, "Top", top));
            controls.Controls.Add(NumberPair("Width", regionWidth, "Height", regionHeight));
            foreach (NumericUpDown n in new NumericUpDown[] { left, top, regionWidth, regionHeight })
                n.ValueChanged += delegate { if (!updating) { followDemo = false; Stop(); } };
            controls.Controls.Add(Hint("Physical screen pixels. Fixed region, up to 1024 x 1024.", 34));

            AddSection(controls, "02  COLOR MATCH");
            Panel colorRow = new Panel { Width = 320, Height = 36 };
            swatch = new Panel { BackColor = targetColor, Location = new Point(0, 2), Size = new Size(34, 29) };
            hex = new TextBox { Text = "#FFD54A", Location = new Point(44, 3), Width = 120, BackColor = Surface, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            Button choose = Button("Choose color", ChooseColor); choose.Location = new Point(174, 0); choose.Width = 146;
            colorRow.Controls.Add(swatch); colorRow.Controls.Add(hex); colorRow.Controls.Add(choose); controls.Controls.Add(colorRow);
            hex.TextChanged += delegate { if (!updating) Stop(); };
            hex.Leave += delegate { ReadColor(false); };
            controls.Controls.Add(Hint("F8 samples the pixel under your pointer.", 25));
            tolerance = Number(0, 255, 18); minimumPixels = Number(1, 1048576, 8);
            controls.Controls.Add(NumberPair("RGB tolerance", tolerance, "Min. pixels", minimumPixels));

            AddSection(controls, "03  TRIGGER RULES");
            stableFrames = Number(1, 120, 2); cooldown = Number(20, 10000, 250); interval = Number(16, 2000, 33);
            controls.Controls.Add(NumberPair("Stable frames", stableFrames, "Cooldown (ms)", cooldown));
            Panel intervalRow = new Panel { Width = 320, Height = 49 };
            AddNumber(intervalRow, "Scan interval (ms)", interval, 0, 148);
            repeat = new CheckBox { Text = "Repeat while matched", AutoSize = true, Location = new Point(163, 21), ForeColor = Muted };
            intervalRow.Controls.Add(repeat); controls.Controls.Add(intervalRow);
            autoClick = new CheckBox { Text = "Send a left-click on trigger", AutoSize = true, Margin = new Padding(0, 7, 0, 5) };
            controls.Controls.Add(autoClick);
            autoClick.CheckedChanged += delegate {
                Stop();
                if (autoClick.Checked && !registeredHotkeys.Contains(7)) {
                    autoClick.Checked = false; SetFooter("Automatic clicking requires the F7 stop hotkey. Close the app using F7, then restart this app.", true);
                }
                else SetFooter(autoClick.Checked ? "Click mode selected. Bind a window with F9, then start with F6." : "Detection-only mode. Trigger events are logged without clicking.", false);
            };
            binding = Hint("No window bound. Focus your game, then press F9.", 43); controls.Controls.Add(binding);
            foreach (NumericUpDown n in new NumericUpDown[] { tolerance, minimumPixels, stableFrames, cooldown, interval }) n.ValueChanged += delegate { Stop(); };
            repeat.CheckedChanged += delegate { Stop(); };

            controls.Controls.Add(ButtonRow(Button("Open test scene", OpenDemo), Button("Export CSV", ExportCsv)));
            controls.Controls.Add(Hint("F7 stops immediately. Clicks stay at the current pointer; the app never aims or moves it.", 48));

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(18, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            body.Controls.Add(right, 1, 0);
            status = new Label { Text = "PAUSED  /  DETECTION ONLY", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            detection = new Label { Text = "Ready to inspect a region", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 19, FontStyle.Bold) };
            right.Controls.Add(status, 0, 0); right.Controls.Add(detection, 0, 1);
            preview = new PreviewPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(7, 12, 19), Margin = new Padding(0, 0, 0, 0) };
            preview.Paint += PaintPreview; right.Controls.Add(preview, 0, 2);
            metrics = new Label { Text = "0 matching pixels     |     0 scans/s\nCapture + scan: -- ms     |     Triggers: 0", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), ForeColor = Muted };
            right.Controls.Add(metrics, 0, 3);
            right.Controls.Add(new Label { Text = "TRIGGER EVENTS", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Muted }, 0, 4);
            eventList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BackColor = Surface, ForeColor = ForeColor, BorderStyle = BorderStyle.None };
            eventList.Columns.Add("Time (local)", 116); eventList.Columns.Add("Event", 100); eventList.Columns.Add("Pixels", 65); eventList.Columns.Add("Presence ms", 100);
            right.Controls.Add(eventList, 0, 5);
            eventCount = new Label { Text = "Latest 250 displayed. Export retains up to 10,000 events.", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0), Font = new Font("Segoe UI", 8.5f) };
            right.Controls.Add(eventCount, 0, 6);
            footer = new Label { Text = "Start with the test scene, or select a region from your own game.", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(0, 10, 0, 0), AutoEllipsis = true };
            root.Controls.Add(footer, 0, 2);
        }

        private static Button Button(string text, EventHandler click)
        {
            Button button = new Button { Text = text, Width = 155, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Color.FromArgb(229, 236, 246), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 4) };
            button.FlatAppearance.BorderSize = 0; button.Click += click; return button;
        }
        private static Panel ButtonRow(Button a, Button b)
        {
            Panel p = new Panel { Width = 320, Height = 39, Margin = new Padding(0) }; a.Location = Point.Empty; b.Location = new Point(165, 0); p.Controls.Add(a); p.Controls.Add(b); return p;
        }
        private static Label Hint(string text, int height) { return new Label { Text = text, Width = 320, Height = height, ForeColor = Muted, Margin = new Padding(0, 2, 0, 0), Font = new Font("Segoe UI", 8.8f) }; }
        private static void AddSection(Control parent, string text) { parent.Controls.Add(new Label { Text = text, Width = 320, Height = 27, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 8, 0, 1) }); }
        private NumericUpDown Number(int min, int max, int value) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, BackColor = Surface, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = false }; }
        private static void AddNumber(Panel panel, string label, NumericUpDown number, int x, int width)
        {
            panel.Controls.Add(new Label { Text = label, Location = new Point(x, 0), Width = width, Height = 20, ForeColor = Muted, Font = new Font("Segoe UI", 8.8f) });
            number.Location = new Point(x, 21); number.Width = width; panel.Controls.Add(number);
        }
        private static Panel NumberPair(string a, NumericUpDown an, string b, NumericUpDown bn)
        {
            Panel p = new Panel { Width = 320, Height = 49, Margin = new Padding(0, 0, 0, 4) }; AddNumber(p, a, an, 0, 148); AddNumber(p, b, bn, 165, 155); return p;
        }

        private Rectangle CaptureRegion { get { return new Rectangle((int)left.Value, (int)top.Value, (int)regionWidth.Value, (int)regionHeight.Value); } }
        private static Rectangle CenteredRegion(Rectangle bounds, int width, int height) { return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height); }
        private void SetRegion(Rectangle region)
        {
            updating = true;
            left.Value = Math.Max(left.Minimum, Math.Min(left.Maximum, region.X)); top.Value = Math.Max(top.Minimum, Math.Min(top.Maximum, region.Y));
            regionWidth.Value = Math.Max(1, Math.Min(1024, region.Width)); regionHeight.Value = Math.Max(1, Math.Min(1024, region.Height));
            updating = false;
        }
        private void SetFooter(string text, bool error) { footer.Text = text; footer.ForeColor = error ? Color.Salmon : Muted; }
        private bool ReadColor(bool report)
        {
            Color color;
            if (!PixelDetector.TryParseColor(hex.Text, out color)) { if (report) SetFooter("Enter a six-digit RGB hex color, for example #FFD54A.", true); return false; }
            targetColor = color; swatch.BackColor = color;
            if (demo != null && !demo.IsDisposed) demo.TargetColor = color;
            return true;
        }

        private void ChooseColor(object sender, EventArgs e)
        {
            Stop();
            using (ColorDialog dialog = new ColorDialog { Color = targetColor, FullOpen = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) { hex.Text = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2"); ReadColor(false); }
        }

        private void PickRegion(object sender, EventArgs e)
        {
            Stop(); followDemo = false; Hide();
            try {
                using (RegionPicker picker = new RegionPicker()) if (picker.ShowDialog() == DialogResult.OK) {
                    if (picker.SelectedRegion.Width > 1024 || picker.SelectedRegion.Height > 1024) SetFooter("Choose a region no larger than 1024 x 1024 pixels.", true);
                    else { SetRegion(picker.SelectedRegion); SetFooter("Capture region selected. Start monitoring when ready.", false); }
                }
            }
            finally { Show(); Activate(); }
        }

        private void RegisterHotkeys()
        {
            List<string> failed = new List<string>();
            foreach (int id in new int[] { 6, 7, 8, 9 })
            {
                if (Native.RegisterHotKey(Handle, id, 0x4000, (uint)(0x70 + id - 1))) registeredHotkeys.Add(id);
                else failed.Add("F" + id);
            }
            if (failed.Count > 0) SetFooter("Hotkeys unavailable: " + string.Join(", ", failed.ToArray()) + ". Another app may own them.", true);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                int id = message.WParam.ToInt32();
                if (id == 6) Toggle();
                if (id == 7) { Stop(); SetFooter("Stopped by F7. Automatic clicks are inactive.", false); }
                if (id == 8) SampleColor();
                if (id == 9) BindWindow();
            }
            base.WndProc(ref message);
        }

        private void SampleColor()
        {
            Stop();
            try {
                using (Bitmap pixel = new Bitmap(1, 1, PixelFormat.Format32bppArgb)) using (Graphics graphics = Graphics.FromImage(pixel)) {
                    graphics.CopyFromScreen(Cursor.Position, Point.Empty, new Size(1, 1), CopyPixelOperation.SourceCopy);
                    Color color = pixel.GetPixel(0, 0);
                    hex.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); ReadColor(false);
                    SetFooter("Sampled " + hex.Text + ". Start again with F6 when ready.", false);
                }
            } catch (Exception ex) { SetFooter("Color sample failed: " + ex.Message, true); }
        }

        private void BindWindow()
        {
            Stop();
            IntPtr window = Native.GetForegroundWindow();
            if (window == Handle || window == IntPtr.Zero) { SetFooter("Focus your game or test scene and press F9 to bind it.", true); return; }
            Bind(window);
            followDemo = demo != null && !demo.IsDisposed && window == demo.Handle;
            Rectangle bounds = Native.ClientBounds(window);
            if (!bounds.IsEmpty) SetRegion(CenteredRegion(bounds, (int)regionWidth.Value, (int)regionHeight.Value));
            SetFooter("Window bound and capture region centered. F6 starts monitoring.", false);
        }
        private void Bind(IntPtr window)
        {
            targetWindow = window;
            Native.GetWindowThreadProcessId(window, out targetProcess);
            binding.Text = "Bound: " + Native.Title(window) + "\nF9 binds the active window and centers the region.";
        }

        private void OpenDemo(object sender, EventArgs e)
        {
            Stop(); ReadColor(false);
            if (demo == null || demo.IsDisposed)
            {
                demo = new DemoForm();
                demo.StartPosition = FormStartPosition.Manual;
                Rectangle work = Screen.FromControl(this).WorkingArea;
                demo.Location = new Point(Math.Max(work.Left, work.Right - demo.Width - 16), work.Top + 24);
                if (work.Width >= Width + demo.Width + 40) Location = new Point(work.Left + 16, work.Top + 24);
                demo.GeometryChanged += delegate { if (followDemo) { gate.ResetPresence(); SetRegion(demo.ScanRegion); } };
                demo.FormClosed += delegate { if (followDemo) { Stop(); followDemo = false; targetWindow = IntPtr.Zero; binding.Text = "Test scene closed. Bind another window with F9."; } };
            }
            demo.TargetColor = targetColor; demo.Show(); demo.Activate(); followDemo = true;
            Bind(demo.Handle); SetRegion(demo.ScanRegion);
            SetFooter("Test scene bound. F6 starts; Space pauses motion; T hides the target; D adds a decoy.", false);
        }

        private void Toggle()
        {
            if (running) { Stop(); return; }
            if (!ReadColor(true)) return;
            Rectangle region = CaptureRegion;
            if (!SystemInformation.VirtualScreen.Contains(region)) { SetFooter("The capture region must be inside the current desktop bounds.", true); return; }
            if (minimumPixels.Value > (long)region.Width * region.Height) { SetFooter("Minimum matching pixels exceeds the capture region's area.", true); return; }
            if (autoClick.Checked && (!registeredHotkeys.Contains(7) || !Native.IsWindow(targetWindow))) { SetFooter("Click mode needs an available F7 stop hotkey and a window bound with F9.", true); return; }
            gate.Reset(); frameCount = 0; lastRateTime = clock.Elapsed.TotalMilliseconds; lastUiTime = 0; scanRate = 0;
            running = true; timer.Interval = (int)interval.Value; timer.Start();
            start.Text = "Stop monitoring   F6"; start.BackColor = Color.FromArgb(241, 181, 101);
            status.Text = autoClick.Checked ? "MONITORING  /  AUTOMATIC CLICK" : "MONITORING  /  DETECTION ONLY"; status.ForeColor = Accent;
            SetFooter(autoClick.Checked ? "Click mode active only inside the focused bound window. F7 stops." : "Detection-only mode. No mouse input is sent. F7 stops.", false);
        }

        private void Stop()
        {
            timer.Stop(); running = false; gate.Reset();
            if (start == null) return;
            start.Text = "Start monitoring   F6"; start.BackColor = Accent;
            status.Text = "PAUSED  /  " + (autoClick.Checked ? "AUTOMATIC CLICK" : "DETECTION ONLY"); status.ForeColor = Muted;
            detection.Text = "Paused"; detection.ForeColor = ForeColor;
        }

        private void Scan(object sender, EventArgs e)
        {
            if (!running) return;
            try
            {
                // Poll the stop key before capture as well as receiving its global message.
                if ((Native.GetAsyncKeyState(0x76) & 0x8000) != 0) { Stop(); return; }
                Rectangle region = CaptureRegion;
                if (!SystemInformation.VirtualScreen.Contains(region)) { Stop(); SetFooter("Display layout changed. Select the capture region again.", true); return; }
                double now = clock.Elapsed.TotalMilliseconds;
                if (WindowState != FormWindowState.Minimized && Bounds.IntersectsWith(region))
                {
                    gate.ResetPresence(); detection.Text = "Move this window away"; detection.ForeColor = Color.Salmon;
                    SetFooter("The tester overlaps the capture area. Move or minimize it to avoid detecting its own preview.", true);
                    return;
                }
                Stopwatch scan = Stopwatch.StartNew();
                if (frame == null || frame.Size != region.Size)
                {
                    if (captureGraphics != null) captureGraphics.Dispose();
                    if (frame != null) frame.Dispose();
                    frame = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb); captureGraphics = Graphics.FromImage(frame);
                }
                captureGraphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                int count = PixelDetector.Count(frame, targetColor, (int)tolerance.Value);
                scan.Stop(); now = clock.Elapsed.TotalMilliseconds; frameCount++;
                bool matched = count >= minimumPixels.Value;
                string blocked = autoClick.Checked ? Native.ClickBlockReason(targetWindow, targetProcess, Cursor.Position) : null;
                double latency = 0;
                bool trigger = false;
                if (blocked != null) gate.ResetPresence();
                else trigger = gate.Evaluate(matched, now, (int)stableFrames.Value, (int)cooldown.Value, repeat.Checked, out latency);
                if (trigger)
                {
                    // Recheck eligibility immediately before dispatching global mouse input.
                    string action = "would_fire";
                    if (autoClick.Checked)
                    {
                        string reason = Native.ClickBlockReason(targetWindow, targetProcess, Cursor.Position);
                        if (reason != null) { gate.ResetPresence(); SetFooter(reason, false); return; }
                        if ((Native.GetAsyncKeyState(0x76) & 0x8000) != 0) { Stop(); return; }
                        bool sent = Native.LeftClick(); action = sent ? "click_sent" : "input_failed";
                        if (!sent) { Stop(); SetFooter("Windows did not accept the input pair. Test with both apps at the same privilege level.", true); }
                    }
                    // Presence time starts at the first eligible matching scan, not at the game's render time.
                    AddEvent(action, count, scan.Elapsed.TotalMilliseconds, latency, region);
                    lastTriggerTime = now;
                }
                if (now - lastRateTime >= 1000) { scanRate = frameCount * 1000.0 / (now - lastRateTime); frameCount = 0; lastRateTime = now; }
                // Decouple UI repaint from scanning; preview refreshes at most ten times per second.
                if (now - lastUiTime >= 100 || trigger)
                {
                    lastUiTime = now;
                    if (running) {
                        detection.Text = now - lastTriggerTime < 250 ? "TRIGGER" : matched ? "Color match" : "No color match";
                        detection.ForeColor = matched ? Accent : ForeColor;
                    }
                    metrics.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} matching pixels ({1:P1})   |   {2:F1} scans/s\nCapture + scan: {3:F2} ms   |   Triggers: {4}", count, count / (double)(region.Width * region.Height), scanRate, scan.Elapsed.TotalMilliseconds, triggerCount);
                    if (running) SetFooter(blocked ?? (autoClick.Checked ? "Click mode ready. F7 stops." : "Detection only. Events record when a click would fire."), false);
                    preview.Invalidate();
                }
            }
            catch (Exception ex) { Stop(); SetFooter("Capture stopped: " + ex.Message, true); }
        }

        private void PaintPreview(object sender, PaintEventArgs e)
        {
            if (frame == null) { TextRenderer.DrawText(e.Graphics, "Live capture appears here", Font, preview.ClientRectangle, Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
            float scale = Math.Min((float)preview.Width / frame.Width, (float)preview.Height / frame.Height);
            int width = (int)(frame.Width * scale), height = (int)(frame.Height * scale);
            Rectangle dest = new Rectangle((preview.Width - width) / 2, (preview.Height - height) / 2, width, height);
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor; e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(frame, dest, new Rectangle(Point.Empty, frame.Size), GraphicsUnit.Pixel);
        }

        private void AddEvent(string action, int count, double scanMs, double presenceMs, Rectangle region)
        {
            triggerCount++;
            TriggerEvent item = new TriggerEvent {
                Timestamp = DateTime.UtcNow, Action = action, Matches = count, Fraction = count / (double)(region.Width * region.Height),
                ScanMs = scanMs, PresenceMs = presenceMs, Region = region, Color = targetColor,
                Tolerance = (int)tolerance.Value, MinimumPixels = (int)minimumPixels.Value, StableFrames = (int)stableFrames.Value,
                CooldownMs = (int)cooldown.Value, IntervalMs = (int)interval.Value, Repeat = repeat.Checked
            };
            if (events.Count == 10000) events.RemoveAt(0); events.Add(item);
            ListViewItem row = new ListViewItem(new string[] { item.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"), action, count.ToString(), presenceMs.ToString("F1", CultureInfo.InvariantCulture) });
            eventList.Items.Insert(0, row); if (eventList.Items.Count > 250) eventList.Items.RemoveAt(250);
            eventCount.Text = events.Count + " events retained for CSV. Latest 250 displayed.";
        }

        private void ExportCsv(object sender, EventArgs e)
        {
            Stop();
            if (events.Count == 0) { SetFooter("There are no trigger events to export yet.", false); return; }
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "color-trigger-events-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try {
                        using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true))) { writer.WriteLine(TriggerEvent.CsvHeader); foreach (TriggerEvent item in events) writer.WriteLine(item.ToCsv()); }
                        SetFooter("Exported " + events.Count + " events to " + Path.GetFileName(dialog.FileName) + ".", false);
                    } catch (Exception ex) { SetFooter("Export failed: " + ex.Message, true); }
        }

        private sealed class PreviewPanel : Panel { public PreviewPanel() { DoubleBuffered = true; } }
    }
}

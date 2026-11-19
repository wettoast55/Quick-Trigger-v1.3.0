using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HexBot
{
    // This form handles controls and display only; ScanWorker owns the capture loop.
    internal sealed class MainForm : Form
    {
        private readonly Timer refreshTimer = new Timer();
        private readonly HashSet<int> hotkeys = new HashSet<int>();
        private readonly Queue<TriggerEvent> events = new Queue<TriggerEvent>();
        private NumericUpDown leftInput, topInput, widthInput, heightInput;
        private NumericUpDown toleranceInput, minimumInput, stableInput, cooldownInput, intervalInput;
        private TextBox colorInput;
        private CheckBox clickInput, repeatInput;
        private Label regionLabel, targetLabel, statusLabel, metricsLabel, messageLabel, eventCountLabel;
        private Button startButton, colorButton;
        private TabControl tabs;
        private ListView eventList;
        private FramePreview preview;
        private ScanWorker worker;
        private DemoForm demo;
        private IntPtr targetWindow;
        private uint targetProcess;
        private bool monitoring;
        private bool settingInputs;
        private bool followDemo;
        private bool eventsChanged;
        private bool resourcesReleased;
        private Color targetColor = Color.FromArgb(255, 213, 74);

        // The 100 ms timer refreshes the interface, never the detector.
        public MainForm()
        {
            Text = "HexBot";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Ui.Background;
            ForeColor = Ui.Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(800, 560);
            MinimumSize = new Size(790, 570);
            StartPosition = FormStartPosition.CenterScreen;
            BuildInterface();
            SetRegion(CenteredRegion(Screen.PrimaryScreen.Bounds, 120, 120));
            Shown += RegisterHotkeys;
            refreshTimer.Interval = 100;
            refreshTimer.Tick += RefreshResults;
            refreshTimer.Start();
        }

        // Setup stays short; coordinates and filters move to Advanced, logs to Events.
        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 12, 18, 10);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            Label title = Ui.Label("HexBot", 250, 42);
            title.Font = new Font("Segoe UI", 25, FontStyle.Bold);
            title.ForeColor = Ui.Text;
            startButton = Ui.Button("Start   F6", ToggleMonitoring);
            startButton.Dock = DockStyle.Fill;
            startButton.Margin = new Padding(0, 6, 0, 12);
            startButton.BackColor = Ui.Accent;
            startButton.ForeColor = Color.White;
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(startButton, 1, 0);
            root.Controls.Add(header, 0, 0);

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(BuildSetupPage());
            tabs.TabPages.Add(BuildAdvancedPage());
            tabs.TabPages.Add(BuildEventsPage());
            tabs.SelectedIndexChanged += OnTabChanged;
            root.Controls.Add(tabs, 0, 1);

            messageLabel = Ui.Label("F6 start/stop   |   F7 stop   |   F8 sample color   |   F9 bind window", 740, 30);
            messageLabel.Dock = DockStyle.Fill;
            messageLabel.Padding = new Padding(0, 8, 0, 0);
            messageLabel.AutoEllipsis = true;
            root.Controls.Add(messageLabel, 0, 2);
        }

        // All controls needed for a normal run fit on the first page.
        private TabPage BuildSetupPage()
        {
            TabPage page = NewPage("Setup");
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 324));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            FlowLayoutPanel settings = VerticalPanel();
            layout.Controls.Add(settings, 0, 0);
            settings.Controls.Add(Ui.Label("Scan area", 302, 22));
            settings.Controls.Add(Ui.Buttons(Ui.Button("Select area", SelectRegion), Ui.Button("Center", CenterRegion)));
            regionLabel = Ui.Label("", 302, 30);
            settings.Controls.Add(regionLabel);

            colorInput = new TextBox();
            colorInput.Text = PixelDetector.ToHex(targetColor);
            colorInput.AccessibleName = "Hex color";
            colorInput.TextChanged += OnSettingsChanged;
            colorInput.Leave += OnColorLeave;
            colorButton = Ui.Button("Choose", ChooseColor);
            colorButton.BackColor = targetColor;
            settings.Controls.Add(Ui.Pair("Hex color", colorInput, "Color picker", colorButton));
            toleranceInput = Ui.Number("Tolerance", 0, 255, 18);
            toleranceInput.ValueChanged += OnSettingsChanged;
            settings.Controls.Add(Ui.Pair("Tolerance", toleranceInput, "F8 samples under pointer", new Label()));

            // Only the integer type's natural range remains; 1 ms is valid in both fields.
            cooldownInput = Ui.Number("Cooldown ms", 1, int.MaxValue, 250);
            intervalInput = Ui.Number("Scan interval ms", 1, int.MaxValue, 33);
            cooldownInput.ValueChanged += OnSettingsChanged;
            intervalInput.ValueChanged += OnSettingsChanged;
            settings.Controls.Add(Ui.Pair("Cooldown (ms)", cooldownInput, "Scan interval (ms)", intervalInput));

            repeatInput = new CheckBox();
            repeatInput.Text = "Repeat while color is present";
            repeatInput.AutoSize = true;
            repeatInput.CheckedChanged += OnSettingsChanged;
            settings.Controls.Add(repeatInput);
            clickInput = new CheckBox();
            clickInput.Text = "Click on trigger";
            clickInput.AutoSize = true;
            clickInput.CheckedChanged += OnClickModeChanged;
            settings.Controls.Add(clickInput);
            targetLabel = Ui.Label("No window bound. Focus it and press F9.", 302, 40);
            targetLabel.AutoEllipsis = true;
            settings.Controls.Add(targetLabel);

            TableLayoutPanel live = new TableLayoutPanel();
            live.Dock = DockStyle.Fill;
            live.ColumnCount = 1;
            live.RowCount = 4;
            live.Padding = new Padding(10, 0, 0, 0);
            live.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            live.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            live.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            live.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.Controls.Add(live, 1, 0);
            statusLabel = Ui.Label("Paused", 340, 38);
            statusLabel.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            statusLabel.ForeColor = Ui.Text;
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            preview = new FramePreview();
            preview.Dock = DockStyle.Fill;
            metricsLabel = Ui.Label("0 matching pixels  |  0 triggers\nActual: -- scans/s  |  Capture: -- ms", 340, 70);
            metricsLabel.Dock = DockStyle.Fill;
            metricsLabel.Padding = new Padding(0, 10, 0, 0);
            live.Controls.Add(statusLabel, 0, 0);
            live.Controls.Add(preview, 0, 1);
            live.Controls.Add(metricsLabel, 0, 2);
            live.Controls.Add(Ui.Buttons(Ui.Button("Test scene", OpenDemo), Ui.Button("Export CSV", ExportEvents)), 0, 3);
            return page;
        }

        // Less-used controls remain available without crowding Setup.
        private TabPage BuildAdvancedPage()
        {
            TabPage page = NewPage("Advanced");
            FlowLayoutPanel fields = VerticalPanel();
            page.Controls.Add(fields);
            leftInput = Ui.Number("Left", -32768, 32767, 0);
            topInput = Ui.Number("Top", -32768, 32767, 0);
            widthInput = Ui.Number("Width", 1, 1024, 120);
            heightInput = Ui.Number("Height", 1, 1024, 120);
            fields.Controls.Add(Ui.Pair("Left", leftInput, "Top", topInput));
            fields.Controls.Add(Ui.Pair("Width", widthInput, "Height", heightInput));
            foreach (NumericUpDown input in new NumericUpDown[] { leftInput, topInput, widthInput, heightInput })
                input.ValueChanged += OnRegionChanged;
            minimumInput = Ui.Number("Minimum pixels", 1, 1048576, 8);
            stableInput = Ui.Number("Stable frames", 1, int.MaxValue, 2);
            minimumInput.ValueChanged += OnSettingsChanged;
            stableInput.ValueChanged += OnSettingsChanged;
            fields.Controls.Add(Ui.Pair("Minimum pixels", minimumInput, "Stable frames", stableInput));
            fields.Controls.Add(Ui.Label("Coordinates are physical screen pixels. Custom areas stay fixed if a window moves.\n\nStable frames means consecutive matching scans. Repeat mode fires again after the cooldown.\n\n1 ms is a requested interval. Capture, display refresh and Windows scheduling determine the actual rate. Changing settings pauses monitoring.", 630, 180));
            return page;
        }

        // The event list is rebuilt only while this page is visible.
        private TabPage BuildEventsPage()
        {
            TabPage page = NewPage("Events");
            eventList = new ListView();
            eventList.Dock = DockStyle.Fill;
            eventList.View = View.Details;
            eventList.FullRowSelect = true;
            eventList.Columns.Add("Time", 130);
            eventList.Columns.Add("Event", 120);
            eventList.Columns.Add("Pixels", 90);
            eventList.Columns.Add("Presence (ms)", 130);
            eventCountLabel = Ui.Label("Latest 100 shown; latest 10,000 retained for export.", 700, 30);
            eventCountLabel.Dock = DockStyle.Bottom;
            page.Controls.Add(eventList);
            page.Controls.Add(eventCountLabel);
            return page;
        }

        // Shared page styling keeps the three tabs visually consistent.
        private static TabPage NewPage(string title)
        {
            TabPage page = new TabPage(title);
            page.BackColor = Ui.Background;
            page.Padding = new Padding(12);
            return page;
        }

        // Scrolling is a fallback for smaller displays and higher DPI settings.
        private static FlowLayoutPanel VerticalPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            return panel;
        }

        // Center a rectangle using physical coordinates, including negative monitor origins.
        private static Rectangle CenteredRegion(Rectangle bounds, int width, int height)
        {
            return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }

        // Read a fresh immutable-in-use settings object when starting a session.
        private ScanSettings ReadSettings()
        {
            return new ScanSettings {
                Region = new Rectangle((int)leftInput.Value, (int)topInput.Value, (int)widthInput.Value, (int)heightInput.Value),
                TargetColor = targetColor, ColorTolerance = (int)toleranceInput.Value,
                MinimumPixels = (int)minimumInput.Value, StableFrames = (int)stableInput.Value,
                CooldownMs = (int)cooldownInput.Value, ScanIntervalMs = (int)intervalInput.Value,
                Repeat = repeatInput.Checked, SendClicks = clickInput.Checked,
                TargetWindow = targetWindow, TargetProcess = targetProcess, MainWindow = Handle
            };
        }

        // Suppress edit handlers while updating all four coordinate inputs together.
        private void SetRegion(Rectangle area)
        {
            settingInputs = true;
            leftInput.Value = Math.Max(leftInput.Minimum, Math.Min(leftInput.Maximum, area.X));
            topInput.Value = Math.Max(topInput.Minimum, Math.Min(topInput.Maximum, area.Y));
            widthInput.Value = Math.Max(1, Math.Min(1024, area.Width));
            heightInput.Value = Math.Max(1, Math.Min(1024, area.Height));
            settingInputs = false;
            UpdateRegionLabel();
        }

        // Show enough coordinate information without exposing four inputs on Setup.
        private void UpdateRegionLabel()
        {
            regionLabel.Text = widthInput.Value + " x " + heightInput.Value + " at " + leftInput.Value + ", " + topInput.Value;
        }

        // User edits end the current run; no worker sees a partially edited configuration.
        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (!settingInputs)
                StopMonitoring();
        }

        // A manual area edit also disables automatic following of the demo window.
        private void OnRegionChanged(object sender, EventArgs e)
        {
            if (settingInputs)
                return;
            followDemo = false;
            StopMonitoring();
            UpdateRegionLabel();
        }

        // Automatic clicking requires a working global emergency-stop key.
        private void OnClickModeChanged(object sender, EventArgs e)
        {
            StopMonitoring();
            if (clickInput.Checked && !hotkeys.Contains(7))
            {
                clickInput.Checked = false;
                ShowMessage("F7 is unavailable. Close the app using that hotkey, then restart HexBot.", true);
            }
        }

        // Refresh the swatch after typing a valid hex color.
        private void OnColorLeave(object sender, EventArgs e)
        {
            ReadColor();
        }

        // Reject malformed colors instead of silently keeping an earlier value.
        private bool ReadColor()
        {
            Color color;
            if (!PixelDetector.TryParseColor(colorInput.Text, out color))
            {
                ShowMessage("Enter six hex digits, for example #FFD54A.", true);
                return false;
            }
            targetColor = color;
            colorButton.BackColor = color;
            if (demo != null && !demo.IsDisposed)
                demo.TargetColor = color;
            return true;
        }

        // Use the standard Windows color dialog for manual selection.
        private void ChooseColor(object sender, EventArgs e)
        {
            StopMonitoring();
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = targetColor;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    colorInput.Text = PixelDetector.ToHex(dialog.Color);
                    ReadColor();
                }
            }
        }

        // Sample one visible screen pixel without saving an image.
        private void SampleColor()
        {
            StopMonitoring();
            try
            {
                using (Bitmap pixel = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(pixel))
                {
                    graphics.CopyFromScreen(WindowsApi.CursorPosition(), Point.Empty, pixel.Size);
                    colorInput.Text = PixelDetector.ToHex(pixel.GetPixel(0, 0));
                    ReadColor();
                }
                ShowMessage("Color sampled. F6 starts monitoring.", false);
            }
            catch (Exception error)
            {
                ShowMessage(error.Message, true);
            }
        }

        // Hide the main window during rectangle selection so it does not obstruct the view.
        private void SelectRegion(object sender, EventArgs e)
        {
            StopMonitoring();
            followDemo = false;
            Hide();
            try
            {
                using (RegionPicker picker = new RegionPicker())
                {
                    if (picker.ShowDialog() == DialogResult.OK)
                    {
                        Rectangle area = picker.SelectedRegion;
                        if (area.Width > 1024 || area.Height > 1024)
                            ShowMessage("Choose an area no larger than 1024 x 1024 pixels.", true);
                        else
                            SetRegion(area);
                    }
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        // Center on the bound window when available, otherwise on this monitor.
        private void CenterRegion(object sender, EventArgs e)
        {
            StopMonitoring();
            followDemo = false;
            Rectangle bounds = Screen.FromControl(this).Bounds;
            if (WindowsApi.IsWindow(targetWindow))
                bounds = WindowsApi.ClientBounds(targetWindow);
            SetRegion(CenteredRegion(bounds, (int)widthInput.Value, (int)heightInput.Value));
        }

        // Bind the active window, record its process identity and center the capture area.
        private void BindActiveWindow()
        {
            StopMonitoring();
            IntPtr window = WindowsApi.GetForegroundWindow();
            if (window == IntPtr.Zero || window == Handle)
            {
                ShowMessage("Focus your test window and press F9.", true);
                return;
            }
            BindWindow(window);
            followDemo = demo != null && !demo.IsDisposed && window == demo.Handle;
            SetRegion(CenteredRegion(WindowsApi.ClientBounds(window), (int)widthInput.Value, (int)heightInput.Value));
            ShowMessage("Window bound. F6 starts monitoring.", false);
        }

        // Saving the process ID also protects against Windows reusing a closed handle.
        private void BindWindow(IntPtr window)
        {
            targetWindow = window;
            WindowsApi.GetWindowThreadProcessId(window, out targetProcess);
            targetLabel.Text = "Bound: " + WindowsApi.WindowTitle(window);
        }

        // Open a self-contained scene so detection can be checked without another game.
        private void OpenDemo(object sender, EventArgs e)
        {
            StopMonitoring();
            if (demo == null || demo.IsDisposed)
            {
                demo = new DemoForm();
                Rectangle desktop = Screen.FromControl(this).WorkingArea;
                demo.StartPosition = FormStartPosition.Manual;
                demo.Location = new Point(Math.Max(desktop.Left, desktop.Right - demo.Width - 16), desktop.Top + 24);
                if (desktop.Width >= Width + demo.Width + 48)
                    Location = new Point(desktop.Left + 16, desktop.Top + 24);
                demo.GeometryChanged += OnDemoMoved;
                demo.FormClosed += OnDemoClosed;
            }
            demo.TargetColor = targetColor;
            demo.Show();
            demo.Activate();
            followDemo = true;
            BindWindow(demo.Handle);
            SetRegion(demo.ScanRegion);
            ShowMessage("F6 starts. In the scene: Space pauses motion, T hides the target, D adds a decoy.", false);
        }

        // Moving the scene updates the area and pauses the run to avoid stale coordinates.
        private void OnDemoMoved(object sender, EventArgs e)
        {
            if (followDemo)
            {
                StopMonitoring();
                SetRegion(demo.ScanRegion);
            }
        }

        // A closed demo must not remain a valid click target.
        private void OnDemoClosed(object sender, FormClosedEventArgs e)
        {
            if (!followDemo)
                return;
            StopMonitoring();
            followDemo = false;
            targetWindow = IntPtr.Zero;
            targetLabel.Text = "No window bound. Focus it and press F9.";
        }

        // Starting freezes the inputs into a snapshot; stopping cancels any timer wait.
        private void ToggleMonitoring(object sender, EventArgs e)
        {
            if (monitoring)
            {
                StopMonitoring();
                return;
            }
            if (worker != null)
            {
                ShowMessage("The previous scan is finishing. Try again in a moment.", false);
                return;
            }
            if (!ReadColor())
                return;
            ScanSettings settings = ReadSettings();
            string error = settings.Validate();
            if (error == null && !SystemInformation.VirtualScreen.Contains(settings.Region))
                error = "The scan area must be inside the current desktop.";
            if (error == null && settings.SendClicks && (!hotkeys.Contains(7) || !WindowsApi.IsWindow(targetWindow)))
                error = "Click mode needs F7 available and a window bound with F9.";
            if (error != null)
            {
                ShowMessage(error, true);
                return;
            }
            worker = new ScanWorker(settings);
            monitoring = true;
            startButton.Text = "Stop   F6";
            statusLabel.Text = settings.SendClicks ? "Click mode" : "Detection only";
            worker.Start();
        }

        // RequestStop blocks only during an input pair, not for the next scheduled scan.
        private void StopMonitoring()
        {
            monitoring = false;
            if (worker != null)
                worker.RequestStop();
            if (startButton == null || statusLabel == null)
                return;
            startButton.Text = "Start   F6";
            statusLabel.Text = "Paused";
        }

        // Update the preview and metrics at ten Hz; scanning is independent of this timer.
        private void RefreshResults(object sender, EventArgs e)
        {
            if (worker == null)
                return;
            ConsumeUpdate(worker.ReadUpdate());
            if (worker.IsFinished)
            {
                // Drain again after observing completion so no final event is lost.
                ScanUpdate finalUpdate = worker.ReadUpdate();
                ConsumeUpdate(finalUpdate);
                worker.Dispose();
                worker = null;
                monitoring = false;
                startButton.Text = "Start   F6";
                statusLabel.Text = finalUpdate.Status.Error ? "Stopped: error" : "Paused";
            }
        }

        // Retain a bounded event history while displaying only the latest hundred entries.
        private void ConsumeUpdate(ScanUpdate update)
        {
            if (update.Preview != null)
                preview.SetFrame(update.Preview);
            foreach (TriggerEvent entry in update.Events)
            {
                if (events.Count == 10000)
                    events.Dequeue();
                events.Enqueue(entry);
                eventsChanged = true;
            }
            ScanStatus status = update.Status;
            metricsLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "{0:N0} matching pixels  |  {1:N0} triggers\nActual: {2:F1} scans/s  |  Capture: {3:F2} ms\nTimer: {4}",
                status.MatchingPixels, status.Triggers, status.ScansPerSecond, status.ScanMs,
                status.HighResolution ? "high resolution" : "standard");
            if (monitoring)
            {
                statusLabel.Text = status.Matched ? "Color matched" : "Monitoring";
                ShowMessage(status.Message, status.Error);
            }
            if (status.Error)
                ShowMessage(status.Message, true);
            eventCountLabel.Text = events.Count + " events retained; latest 100 shown. Pending events dropped: " + update.DroppedEvents;
            if (tabs.SelectedIndex == 2 && eventsChanged)
                RefreshEventList();
        }

        // Fill the Events tab on demand; hidden logs do not slow down the scan loop.
        private void OnTabChanged(object sender, EventArgs e)
        {
            if (tabs.SelectedIndex == 2)
                RefreshEventList();
        }

        // Newest events appear first; timestamps shown here use the computer's local zone.
        private void RefreshEventList()
        {
            TriggerEvent[] entries = events.ToArray();
            eventList.BeginUpdate();
            eventList.Items.Clear();
            for (int index = entries.Length - 1; index >= Math.Max(0, entries.Length - 100); index--)
            {
                TriggerEvent entry = entries[index];
                eventList.Items.Add(new ListViewItem(new string[] {
                    entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"), entry.Action,
                    entry.MatchingPixels.ToString(), entry.PresenceMs.ToString("F2", CultureInfo.InvariantCulture)
                }));
            }
            eventList.EndUpdate();
            eventsChanged = false;
        }

        // Stop before export so no clicks can occur while the file dialog is open.
        private void ExportEvents(object sender, EventArgs e)
        {
            StopMonitoring();
            if (worker != null)
                ConsumeUpdate(worker.ReadUpdate());
            if (events.Count == 0)
            {
                ShowMessage("No trigger events to export yet.", false);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV files (*.csv)|*.csv";
                dialog.FileName = "HexBot-events-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                    {
                        writer.WriteLine(TriggerEvent.CsvHeader);
                        foreach (TriggerEvent entry in events)
                            writer.WriteLine(entry.ToCsv());
                    }
                    ShowMessage("Exported " + events.Count + " events.", false);
                }
                catch (Exception error)
                {
                    ShowMessage(error.Message, true);
                }
            }
        }

        // Register keys once, reporting conflicts instead of assuming they succeeded.
        private void RegisterHotkeys(object sender, EventArgs e)
        {
            List<string> unavailable = new List<string>();
            foreach (int number in new int[] { 6, 7, 8, 9 })
            {
                if (WindowsApi.RegisterHotKey(Handle, number, 0x4000, (uint)(0x70 + number - 1)))
                    hotkeys.Add(number);
                else
                    unavailable.Add("F" + number);
            }
            if (unavailable.Count > 0)
                ShowMessage("Hotkeys already in use: " + string.Join(", ", unavailable.ToArray()), true);
        }

        // Global hotkeys reach this message handler even while the form is minimized.
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                switch (message.WParam.ToInt32())
                {
                    case 6: ToggleMonitoring(this, EventArgs.Empty); break;
                    case 7: StopMonitoring(); ShowMessage("Stopped by F7.", false); break;
                    case 8: SampleColor(); break;
                    case 9: BindActiveWindow(); break;
                }
            }
            base.WndProc(ref message);
        }

        // Messages stay in one predictable place and never interrupt a run with a modal.
        private void ShowMessage(string text, bool error)
        {
            messageLabel.Text = text;
            messageLabel.ForeColor = error ? Color.Firebrick : Ui.Muted;
        }

        // Release timers, hotkeys and worker resources before the window exits.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ReleaseResources();
            base.OnFormClosing(e);
        }

        // Disposal also covers forms constructed by tests without ever being shown.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseResources();
            base.Dispose(disposing);
        }

        // Cleanup can be reached from both Close and Dispose, so perform it only once.
        private void ReleaseResources()
        {
            if (resourcesReleased)
                return;
            resourcesReleased = true;
            StopMonitoring();
            refreshTimer.Stop();
            refreshTimer.Dispose();
            foreach (int number in hotkeys)
                WindowsApi.UnregisterHotKey(Handle, number);
            if (demo != null && !demo.IsDisposed)
                demo.Close();
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }
        }
    }
}

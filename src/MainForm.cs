using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QuickTrigger
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
        private ComboBox displayInput;
        private CheckBox clickInput, repeatInput, holdInput, randomDelayInput;
        private Label regionLabel, targetLabel, statusLabel, metricsLabel, messageLabel, eventCountLabel;
        private Button startButton, colorButton, bindingButton;
        private TabControl tabs;
        private ListView eventList;
        private FramePreview preview;
        private ScanWorker worker;
        private DemoForm demo;
        private AreaOverlay areaOverlay;
        private BindingForm bindingDialog;
        private int holdKey = ActivationKey.Default;
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
            Text = "Quick Trigger";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Ui.Background;
            ForeColor = Ui.Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(800, 600);
            MinimumSize = new Size(790, 610);
            StartPosition = FormStartPosition.CenterScreen;
            BuildInterface();
            RefreshDisplays(false);
            SetRegion(DisplayLayout.Centered(SelectedDisplay.Bounds, 120, 120));
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
            Label title = Ui.Label("Quick Trigger", 320, 42);
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
            settings.Controls.Add(Ui.Label("Display", 302, 22));
            displayInput = new ComboBox();
            displayInput.Name = "DisplaySelector";
            displayInput.AccessibleName = "Display";
            displayInput.DropDownStyle = ComboBoxStyle.DropDownList;
            displayInput.Width = 302;
            displayInput.DropDownWidth = 440;
            displayInput.Margin = new Padding(0, 0, 0, 8);
            displayInput.SelectedIndexChanged += OnDisplayChanged;
            displayInput.DropDown += OnDisplayDropdown;
            settings.Controls.Add(displayInput);
            settings.Controls.Add(Ui.Label("Scan area", 302, 22));
            settings.Controls.Add(Ui.Buttons(Ui.Button("Select area", SelectRegion), Ui.Button("Center", CenterRegion), Ui.Button("Show area", ShowCurrentArea)));
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

            randomDelayInput = new CheckBox();
            randomDelayInput.Name = "RandomDelay";
            randomDelayInput.AccessibleName = "Random delay (1-50 ms)";
            randomDelayInput.Text = "Random delay (1-50 ms)";
            randomDelayInput.AutoSize = true;
            randomDelayInput.CheckedChanged += OnSettingsChanged;
            settings.Controls.Add(randomDelayInput);

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
            live.RowCount = 5;
            live.Padding = new Padding(10, 0, 0, 0);
            live.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            live.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
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
            holdInput = new CheckBox();
            holdInput.Name = "HoldToScan";
            holdInput.AccessibleName = "Hold to scan";
            holdInput.Text = "Hold to scan";
            holdInput.AutoSize = true;
            holdInput.CheckedChanged += OnHoldModeChanged;
            bindingButton = Ui.Button(ActivationKey.Name(holdKey), ChooseHoldButton);
            bindingButton.Name = "HoldBinding";
            bindingButton.AccessibleName = "Choose hold button";
            bindingButton.Enabled = false;
            live.Controls.Add(statusLabel, 0, 0);
            live.Controls.Add(Ui.Pair("Activation", holdInput, "Hold button", bindingButton), 0, 1);
            live.Controls.Add(preview, 0, 2);
            live.Controls.Add(metricsLabel, 0, 3);
            live.Controls.Add(Ui.Buttons(Ui.Button("Test scene", OpenDemo), Ui.Button("Export CSV", ExportEvents)), 0, 4);
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
            fields.Controls.Add(Ui.Label("Coordinates are physical desktop pixels and must stay on the chosen display. The green outline lasts three seconds after adjustments.\n\nStable frames means consecutive matching scans. Repeat mode fires again after the cooldown.\n\n1 ms is a requested interval. Capture, display refresh and Windows scheduling determine the actual rate. Changing settings pauses monitoring.", 630, 180));
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
            eventList.Columns.Add("Delay (ms)", 100);
            eventList.Columns.Add("Waited (ms)", 110);
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

        // The dropdown stores display snapshots rather than relying on changing array indexes.
        private DisplayChoice SelectedDisplay
        {
            get { return displayInput.SelectedItem as DisplayChoice; }
        }

        // Rebuild the list while preserving the chosen device when it is still connected.
        private void RefreshDisplays(bool adjustChangedDisplay)
        {
            DisplayChoice previous = SelectedDisplay;
            DisplayChoice[] choices = DisplayChoice.ConnectedDisplays();
            if (choices.Length == 0)
                throw new InvalidOperationException("No connected display is available.");
            int selectedIndex = 0;
            for (int index = 0; index < choices.Length; index++)
            {
                if (choices[index].IsPrimary)
                    selectedIndex = index;
            }
            if (previous != null)
            {
                for (int index = 0; index < choices.Length; index++)
                {
                    if (choices[index].DeviceName == previous.DeviceName)
                        selectedIndex = index;
                }
            }
            bool wasSettingInputs = settingInputs;
            settingInputs = true;
            displayInput.Items.Clear();
            displayInput.Items.AddRange(choices);
            displayInput.SelectedIndex = selectedIndex;
            settingInputs = wasSettingInputs;
            DisplayChoice current = SelectedDisplay;
            bool changed = previous == null || previous.DeviceName != current.DeviceName || previous.Bounds != current.Bounds;
            if (adjustChangedDisplay && changed)
                ApplyDisplaySelection();
        }

        // Opening the list also picks up newly attached monitors.
        private void OnDisplayDropdown(object sender, EventArgs e)
        {
            RefreshDisplays(true);
        }

        // User selection pauses capture and centers the current area on the chosen screen.
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!settingInputs && SelectedDisplay != null)
                ApplyDisplaySelection();
        }

        // A target on a different screen must be rebound before clicking resumes.
        private void ApplyDisplaySelection()
        {
            StopMonitoring();
            followDemo = false;
            if (targetWindow != IntPtr.Zero && !SelectedDisplay.Bounds.IntersectsWith(WindowsApi.ClientBounds(targetWindow)))
            {
                targetWindow = IntPtr.Zero;
                targetProcess = 0;
                targetLabel.Text = "Focus a window on this display and press F9.";
            }
            SetRegion(DisplayLayout.Centered(SelectedDisplay.Bounds, (int)widthInput.Value, (int)heightInput.Value));
        }

        // Binding a window automatically chooses its monitor before centering the scan area.
        private void SelectDisplayForWindow(IntPtr window)
        {
            RefreshDisplays(false);
            string deviceName = Screen.FromHandle(window).DeviceName;
            bool wasSettingInputs = settingInputs;
            settingInputs = true;
            for (int index = 0; index < displayInput.Items.Count; index++)
            {
                if (((DisplayChoice)displayInput.Items[index]).DeviceName == deviceName)
                {
                    displayInput.SelectedIndex = index;
                    break;
                }
            }
            settingInputs = wasSettingInputs;
        }

        // Windows reports layout changes on the UI thread; refresh after its message completes.
        private void OnDisplayLayoutChanged()
        {
            if (!resourcesReleased)
                RefreshDisplays(true);
        }

        // Read a fresh immutable-in-use settings object when starting a session.
        private ScanSettings ReadSettings()
        {
            return new ScanSettings {
                Region = new Rectangle((int)leftInput.Value, (int)topInput.Value, (int)widthInput.Value, (int)heightInput.Value),
                TargetColor = targetColor, ColorTolerance = (int)toleranceInput.Value,
                MinimumPixels = (int)minimumInput.Value, StableFrames = (int)stableInput.Value,
                CooldownMs = (int)cooldownInput.Value, ScanIntervalMs = (int)intervalInput.Value,
                RandomDelay = randomDelayInput.Checked,
                Repeat = repeatInput.Checked, SendClicks = clickInput.Checked,
                HoldToScan = holdInput.Checked, HoldKey = holdKey,
                TargetWindow = targetWindow, TargetProcess = targetProcess, MainWindow = Handle,
                DisplayDeviceName = SelectedDisplay.DeviceName, DisplayBounds = SelectedDisplay.Bounds
            };
        }

        // Suppress edit handlers while updating all four coordinate inputs together.
        private void SetRegion(Rectangle area)
        {
            if (SelectedDisplay != null)
                area = DisplayLayout.Fit(area, SelectedDisplay.Bounds);
            settingInputs = true;
            leftInput.Value = Math.Max(leftInput.Minimum, Math.Min(leftInput.Maximum, area.X));
            topInput.Value = Math.Max(topInput.Minimum, Math.Min(topInput.Maximum, area.Y));
            widthInput.Value = Math.Max(1, Math.Min(1024, area.Width));
            heightInput.Value = Math.Max(1, Math.Min(1024, area.Height));
            settingInputs = false;
            UpdateRegionLabel();
            if (Visible)
                ShowCurrentArea(this, EventArgs.Empty);
        }

        // Show enough coordinate information without exposing four inputs on Setup.
        private void UpdateRegionLabel()
        {
            regionLabel.Text = widthInput.Value + " x " + heightInput.Value + " at " + leftInput.Value + ", " + topInput.Value;
        }

        // Previewing always pauses monitoring; the green border is never deliberately scanned.
        private void ShowCurrentArea(object sender, EventArgs e)
        {
            StopMonitoring();
            if (!Visible || SelectedDisplay == null)
                return;
            Rectangle area = new Rectangle((int)leftInput.Value, (int)topInput.Value, (int)widthInput.Value, (int)heightInput.Value);
            if (!SelectedDisplay.Bounds.Contains(area))
            {
                CloseAreaOverlay();
                ShowMessage("The scan area must fit on the selected display.", true);
                return;
            }
            if (areaOverlay == null || areaOverlay.IsDisposed)
                areaOverlay = new AreaOverlay();
            areaOverlay.ShowArea(area);
        }

        // Remove the indicator before starting capture, sampling a color or choosing a new area.
        private void CloseAreaOverlay()
        {
            if (areaOverlay != null && !areaOverlay.IsDisposed)
                areaOverlay.Close();
            areaOverlay = null;
        }

        // User edits end the current run; no worker sees a partially edited configuration.
        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (!settingInputs)
                StopMonitoring();
        }

        // Enabling hold mode keeps the app paused until the user explicitly arms it.
        private void OnHoldModeChanged(object sender, EventArgs e)
        {
            StopMonitoring();
            bindingButton.Enabled = holdInput.Checked;
            if (holdInput.Checked)
                ShowMessage("Press F6 to arm, then hold " + ActivationKey.Name(holdKey) + " to scan.", false);
            else
                ShowMessage("Hold mode off. F6 starts continuous monitoring.", false);
        }

        // Rebinding always stops the session and keeps the old binding if cancelled.
        private void ChooseHoldButton(object sender, EventArgs e)
        {
            StopMonitoring();
            CloseAreaOverlay();
            using (BindingForm dialog = new BindingForm(clickInput.Checked))
            {
                bindingDialog = dialog;
                try
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        holdKey = dialog.SelectedKey;
                        bindingButton.Text = ActivationKey.Name(holdKey);
                        ShowMessage("Press F6 to arm, then hold " + ActivationKey.Name(holdKey) + " to scan.", false);
                    }
                }
                finally
                {
                    bindingDialog = null;
                }
            }
        }

        // A manual area edit also disables automatic following of the demo window.
        private void OnRegionChanged(object sender, EventArgs e)
        {
            if (settingInputs)
                return;
            followDemo = false;
            StopMonitoring();
            UpdateRegionLabel();
            ShowCurrentArea(this, EventArgs.Empty);
        }

        // Automatic clicking requires a working global emergency-stop key.
        private void OnClickModeChanged(object sender, EventArgs e)
        {
            StopMonitoring();
            if (clickInput.Checked && !hotkeys.Contains(7))
            {
                clickInput.Checked = false;
                ShowMessage("F7 is unavailable. Close the app using that hotkey, then restart Quick Trigger.", true);
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
            CloseAreaOverlay();
            try
            {
                if (!WindowsApi.FlushDesktopDrawing())
                    throw new InvalidOperationException("The desktop is still updating. Try sampling again.");
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
            CloseAreaOverlay();
            followDemo = false;
            Hide();
            try
            {
                using (RegionPicker picker = new RegionPicker(SelectedDisplay.Bounds))
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
            ShowCurrentArea(this, EventArgs.Empty);
        }

        // Center uses the chosen display; F9 separately centers on the bound window.
        private void CenterRegion(object sender, EventArgs e)
        {
            StopMonitoring();
            followDemo = false;
            SetRegion(DisplayLayout.Centered(SelectedDisplay.Bounds, (int)widthInput.Value, (int)heightInput.Value));
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
            SelectDisplayForWindow(window);
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
                demo.StartPosition = FormStartPosition.Manual;
                demo.GeometryChanged += OnDemoMoved;
                demo.FormClosed += OnDemoClosed;
            }
            followDemo = false;
            Rectangle desktop = Screen.FromRectangle(SelectedDisplay.Bounds).WorkingArea;
            demo.Location = new Point(Math.Max(desktop.Left, desktop.Right - demo.Width - 16), desktop.Top + 24);
            if (Screen.FromControl(this).DeviceName == SelectedDisplay.DeviceName && desktop.Width >= Width + demo.Width + 48)
                Location = new Point(desktop.Left + 16, desktop.Top + 24);
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
                SelectDisplayForWindow(demo.Handle);
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
            if (error == null && !DisplayChoice.StillConnected(settings.DisplayDeviceName, settings.DisplayBounds))
                error = "The selected display changed. Select it again from the Display menu.";
            if (error == null && settings.SendClicks && (!hotkeys.Contains(7) || !WindowsApi.IsWindow(targetWindow)))
                error = "Click mode needs F7 available and a window bound with F9.";
            if (error != null)
            {
                ShowMessage(error, true);
                return;
            }
            CloseAreaOverlay();
            if (!WindowsApi.FlushDesktopDrawing())
            {
                ShowMessage("The desktop is still updating. Press F6 again to start.", true);
                return;
            }
            worker = new ScanWorker(settings);
            monitoring = true;
            startButton.Text = settings.HoldToScan ? "Disarm   F6" : "Stop   F6";
            statusLabel.Text = settings.HoldToScan ? "Waiting for hold" : (settings.SendClicks ? "Click mode" : "Detection only");
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
            startButton.Text = holdInput != null && holdInput.Checked ? "Arm   F6" : "Start   F6";
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
                startButton.Text = holdInput.Checked ? "Arm   F6" : "Start   F6";
                statusLabel.Text = finalUpdate.Status.Error ? "Stopped: error" : "Paused";
            }
        }

        // Retain a bounded event history while displaying only the latest hundred entries.
        private void ConsumeUpdate(ScanUpdate update)
        {
            if (update.ClearPreview)
                preview.SetFrame(null);
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
                statusLabel.Text = status.WaitingForHold ? "Waiting for hold" : (status.Matched ? "Color matched" : "Monitoring");
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
                    entry.MatchingPixels.ToString(), entry.PresenceMs.ToString("F2", CultureInfo.InvariantCulture),
                    entry.RequestedDelayMs.ToString(CultureInfo.InvariantCulture),
                    entry.ActualDelayMs.ToString("F2", CultureInfo.InvariantCulture)
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
                dialog.FileName = "QuickTrigger-events-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
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
            ShowCurrentArea(this, EventArgs.Empty);
        }

        // Global hotkeys reach this message handler even while the form is minimized.
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x007E && displayInput != null && IsHandleCreated && !resourcesReleased)
                BeginInvoke(new Action(OnDisplayLayoutChanged));
            if (message.Msg == 0x0312)
            {
                // Do not start scans, sample or bind a target while choosing an activation key.
                if (bindingDialog != null)
                {
                    if (message.WParam.ToInt32() == 7)
                    {
                        StopMonitoring();
                        bindingDialog.CancelBinding();
                    }
                    return;
                }
                switch (message.WParam.ToInt32())
                {
                    case 6: ToggleMonitoring(this, EventArgs.Empty); break;
                    case 7: StopMonitoring(); CloseAreaOverlay(); ShowMessage("Stopped by F7.", false); break;
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
            CloseAreaOverlay();
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

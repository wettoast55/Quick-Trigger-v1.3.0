using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace QuickTrigger
{
    // A status snapshot contains values only; the UI never reads worker-owned controls.
    internal sealed class ScanStatus
    {
        public string Message = "Starting...";
        public bool Matched;
        public bool Error;
        public bool HighResolution;
        public bool WaitingForHold;
        public int MatchingPixels;
        public long Triggers;
        public double ScanMs;
        public double ScansPerSecond;
    }

    // ReadUpdate transfers ownership of Preview to the UI, which must dispose it.
    internal sealed class ScanUpdate
    {
        public ScanStatus Status;
        public Bitmap Preview;
        public bool ClearPreview;
        public TriggerEvent[] Events;
        public long DroppedEvents;
    }

    // One background thread captures, counts and triggers in order; scans never overlap.
    internal sealed class ScanWorker : IDisposable
    {
        private readonly ScanSettings settings;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly object inputLock = new object();
        private readonly object resultLock = new object();
        private readonly Queue<TriggerEvent> pendingEvents = new Queue<TriggerEvent>();
        private readonly ScanControl control;
        private readonly Func<int, bool> keyDown;
        private readonly Action<Graphics> captureForTest;
        private readonly TriggerDelay triggerDelay;
        private Thread thread;
        private Bitmap pendingPreview;
        private bool clearPreview;
        private ScanStatus latestStatus = new ScanStatus();
        private long droppedEvents;
        private volatile bool stopping;
        private volatile bool finished;

        // Store the run's settings once; the form creates a new worker for the next run.
        public ScanWorker(ScanSettings settings) : this(settings, WindowsApi.KeyIsDown, null)
        {
        }

        // Tests can provide held-button state and synthetic pixels without desktop capture.
        internal ScanWorker(ScanSettings settings, Func<int, bool> keyDown, Action<Graphics> captureForTest)
            : this(settings, keyDown, captureForTest, new Random())
        {
        }

        // A scripted random source lets tests exercise exact delay endpoints in the real worker.
        internal ScanWorker(ScanSettings settings, Func<int, bool> keyDown,
            Action<Graphics> captureForTest, Random delayRandom)
        {
            string error = settings.Validate();
            if (error != null)
                throw new ArgumentException(error);
            this.settings = settings;
            this.keyDown = keyDown;
            this.captureForTest = captureForTest;
            triggerDelay = new TriggerDelay(delayRandom);
            control = new ScanControl(settings, keyDown);
        }

        public bool IsFinished { get { return finished; } }

        // Background threads do not prevent the application from exiting.
        public void Start()
        {
            if (thread != null)
                throw new InvalidOperationException("This scan session has already started.");
            thread = new Thread(Run);
            thread.Name = "Quick Trigger scanner";
            thread.IsBackground = true;
            thread.Start();
        }

        // Sharing this lock with input submission prevents a new click after Stop returns.
        public void RequestStop()
        {
            lock (inputLock)
            {
                stopping = true;
                stop.Set();
            }
        }

        // The UI drains results on its own slower refresh timer, with no callback backlog.
        public ScanUpdate ReadUpdate()
        {
            lock (resultLock)
            {
                ScanUpdate update = new ScanUpdate();
                update.Status = latestStatus;
                update.Preview = pendingPreview;
                update.ClearPreview = clearPreview;
                update.Events = pendingEvents.ToArray();
                update.DroppedEvents = droppedEvents;
                pendingPreview = null;
                clearPreview = false;
                pendingEvents.Clear();
                return update;
            }
        }

        // Publish a small snapshot on every scan; clone the preview at most ten times a second.
        private void Publish(ScanStatus status, Bitmap preview, bool discardPreview = false)
        {
            lock (resultLock)
            {
                latestStatus = status;
                if (discardPreview)
                {
                    if (pendingPreview != null)
                        pendingPreview.Dispose();
                    pendingPreview = null;
                    clearPreview = true;
                }
                if (preview != null)
                {
                    if (pendingPreview != null)
                        pendingPreview.Dispose();
                    pendingPreview = (Bitmap)preview.Clone();
                }
            }
        }

        // Keep memory bounded if the window is busy; report any discarded pending events.
        private void QueueEvent(TriggerEvent entry)
        {
            lock (resultLock)
            {
                if (pendingEvents.Count == 10000)
                {
                    pendingEvents.Dequeue();
                    droppedEvents++;
                }
                pendingEvents.Enqueue(entry);
            }
        }

        // Called during delay slices as well as at submission; F7 and release discard pending work.
        private bool CanContinueTrigger()
        {
            if (stopping)
                return false;
            if (keyDown((int)Keys.F7))
            {
                RequestStop();
                return false;
            }
            return control.IsActive();
        }

        // Capture uses one reusable bitmap and ordinary desktop screen-copy operations.
        private void Run()
        {
            Stopwatch clock = Stopwatch.StartNew();
            double rateWindowStart = 0;
            double previewTime = double.NegativeInfinity;
            double scanRate = 0;
            double displayCheckTime = double.NegativeInfinity;
            long scansInWindow = 0;
            long triggers = 0;
            bool wasWaitingForHold = false;
            try
            {
                using (ScanTimer timer = new ScanTimer())
                using (Bitmap bitmap = new Bitmap(settings.Region.Width, settings.Region.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    Func<double> readClock = delegate { return clock.Elapsed.TotalMilliseconds; };
                    Func<double, bool> waitForDelay = delegate(double milliseconds) { return timer.Wait(milliseconds, stop); };
                    Func<bool> canContinueTrigger = CanContinueTrigger;
                    while (!stopping)
                    {
                        double scanStarted = clock.Elapsed.TotalMilliseconds;
                        // Detect monitor changes even if the main window has not processed them yet.
                        if (scanStarted - displayCheckTime >= 500 && !string.IsNullOrEmpty(settings.DisplayDeviceName))
                        {
                            if (!DisplayChoice.StillConnected(settings.DisplayDeviceName, settings.DisplayBounds))
                                throw new InvalidOperationException("The selected display changed or disconnected. Select it again.");
                            displayCheckTime = scanStarted;
                        }
                        if (keyDown((int)Keys.F7))
                        {
                            RequestStop();
                            break;
                        }

                        // Poll the hold state while waiting, but do not read screen pixels when released.
                        if (!control.IsScanDue(scanStarted))
                        {
                            if (control.WaitingForHold && !wasWaitingForHold)
                            {
                                Publish(new ScanStatus {
                                    Message = "Hold " + ActivationKey.Name(settings.HoldKey) + " to scan. Release to pause.",
                                    WaitingForHold = true, Triggers = triggers, HighResolution = timer.IsHighResolution
                                }, null, true);
                                scanRate = 0;
                                scansInWindow = 0;
                                previewTime = double.NegativeInfinity;
                                wasWaitingForHold = true;
                            }
                            if (!timer.Wait(control.WaitMilliseconds(clock.Elapsed.TotalMilliseconds), stop))
                                break;
                            continue;
                        }
                        if (wasWaitingForHold)
                            rateWindowStart = scanStarted;
                        wasWaitingForHold = false;
                        control.ScheduleNextScan(scanStarted);

                        // Covering the scan area with the control window would create visual feedback.
                        bool overlaps = !WindowsApi.IsIconic(settings.MainWindow) &&
                            WindowsApi.WindowBounds(settings.MainWindow).IntersectsWith(settings.Region);
                        if (overlaps)
                        {
                            control.ResetPresence();
                            Publish(new ScanStatus { Message = "Move or minimize Quick Trigger; it covers the scan area.", Triggers = triggers }, null);
                            continue;
                        }
                        if (!SystemInformation.VirtualScreen.Contains(settings.Region))
                            throw new InvalidOperationException("Display layout changed. Select the scan area again.");

                        // Recheck immediately before and after capture; a released hold invalidates this frame.
                        if (!control.IsActive())
                            continue;
                        if (captureForTest == null)
                            graphics.CopyFromScreen(settings.Region.Location, Point.Empty, settings.Region.Size, CopyPixelOperation.SourceCopy);
                        else
                            captureForTest(graphics);
                        if (!control.IsActive())
                            continue;
                        int matches = PixelDetector.Count(bitmap, settings.TargetColor, settings.ColorTolerance);
                        double now = clock.Elapsed.TotalMilliseconds;
                        double captureAndCountMs = now - scanStarted;
                        bool matched = matches >= settings.MinimumPixels;
                        string blocked = null;
                        if (settings.SendClicks)
                            blocked = WindowsApi.ClickBlockReason(settings.TargetWindow, settings.TargetProcess, WindowsApi.CursorPosition());

                        double presenceMs = 0;
                        bool shouldTrigger = false;
                        if (blocked != null)
                            control.ResetPresence();
                        else
                            shouldTrigger = control.TryTrigger(matched, now, out presenceMs);

                        string inputError = null;
                        if (shouldTrigger)
                        {
                            int requestedDelayMs = triggerDelay.NextMilliseconds(settings.RandomDelay);
                            double actualDelayMs;
                            if (!TriggerDelay.Wait(requestedDelayMs, readClock, waitForDelay,
                                canContinueTrigger, out actualDelayMs))
                            {
                                control.ResetPresence();
                                continue;
                            }
                            lock (inputLock)
                            {
                                if (stopping || keyDown((int)Keys.F7))
                                    break;

                                if (settings.SendClicks)
                                    // Focus and pointer position can change during capture or delay; check again.
                                    blocked = WindowsApi.ClickBlockReason(settings.TargetWindow, settings.TargetProcess, WindowsApi.CursorPosition());
                                // Recheck at submission time for both clicks and detection-only events.
                                if (blocked == null && control.IsActive())
                                {
                                    string action = "would_fire";
                                    if (settings.SendClicks)
                                    {
                                        bool sent = WindowsApi.SendLeftClick();
                                        action = sent ? "click_sent" : "input_failed";
                                        if (!sent)
                                            inputError = "Windows rejected the click. Monitoring stopped.";
                                    }
                                    control.RecordTrigger(clock.Elapsed.TotalMilliseconds);
                                    triggers++;
                                    QueueEvent(new TriggerEvent {
                                        TimestampUtc = DateTime.UtcNow, Action = action,
                                        MatchingPixels = matches, ScanMs = captureAndCountMs,
                                        PresenceMs = presenceMs, Settings = settings,
                                        RequestedDelayMs = requestedDelayMs, ActualDelayMs = actualDelayMs
                                    });
                                }
                                else
                                    control.ResetPresence();
                            }
                        }

                        if (control.WaitingForHold)
                            continue;

                        // Actual throughput includes capture, filtering, input and scheduler delays.
                        now = clock.Elapsed.TotalMilliseconds;
                        scansInWindow++;
                        if (now - rateWindowStart >= 500)
                        {
                            scanRate = scansInWindow * 1000.0 / (now - rateWindowStart);
                            rateWindowStart = now;
                            scansInWindow = 0;
                        }
                        string message = matched ? "Color matched" : "No color match";
                        if (blocked != null)
                            message = blocked;
                        if (inputError != null)
                            message = inputError;
                        bool refreshPreview = now - previewTime >= 100;
                        Publish(new ScanStatus {
                            Message = message, Matched = matched, Error = inputError != null,
                            MatchingPixels = matches, Triggers = triggers, ScanMs = captureAndCountMs,
                            ScansPerSecond = scanRate, HighResolution = timer.IsHighResolution
                        }, refreshPreview ? bitmap : null);
                        if (refreshPreview)
                            previewTime = now;
                        if (inputError != null)
                            break;

                        // The next loop checks the deadline and hold state before waiting or scanning again.
                    }
                }
            }
            catch (Exception error)
            {
                Publish(new ScanStatus { Message = error.Message, Error = true, Triggers = triggers }, null);
            }
            finally
            {
                finished = true;
            }
        }

        // The UI disposes completed sessions. Closing waits briefly without holding result locks.
        public void Dispose()
        {
            RequestStop();
            if (thread != null && !thread.Join(2000))
                return;
            lock (resultLock)
            {
                if (pendingPreview != null)
                    pendingPreview.Dispose();
                pendingPreview = null;
            }
            stop.Dispose();
        }
    }
}

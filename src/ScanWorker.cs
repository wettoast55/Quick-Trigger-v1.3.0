using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace HexBot
{
    // A status snapshot contains values only; the UI never reads worker-owned controls.
    internal sealed class ScanStatus
    {
        public string Message = "Starting...";
        public bool Matched;
        public bool Error;
        public bool HighResolution;
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
        private readonly TriggerGate triggerGate = new TriggerGate();
        private Thread thread;
        private Bitmap pendingPreview;
        private ScanStatus latestStatus = new ScanStatus();
        private long droppedEvents;
        private volatile bool stopping;
        private volatile bool finished;

        // Store the run's settings once; the form creates a new worker for the next run.
        public ScanWorker(ScanSettings settings)
        {
            string error = settings.Validate();
            if (error != null)
                throw new ArgumentException(error);
            this.settings = settings;
        }

        public bool IsFinished { get { return finished; } }

        // Background threads do not prevent the application from exiting.
        public void Start()
        {
            if (thread != null)
                throw new InvalidOperationException("This scan session has already started.");
            thread = new Thread(Run);
            thread.Name = "HexBot scanner";
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
                update.Events = pendingEvents.ToArray();
                update.DroppedEvents = droppedEvents;
                pendingPreview = null;
                pendingEvents.Clear();
                return update;
            }
        }

        // Publish a small snapshot on every scan; clone the preview at most ten times a second.
        private void Publish(ScanStatus status, Bitmap preview)
        {
            lock (resultLock)
            {
                latestStatus = status;
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

        // Capture uses one reusable bitmap and ordinary desktop screen-copy operations.
        private void Run()
        {
            Stopwatch clock = Stopwatch.StartNew();
            double rateWindowStart = 0;
            double previewTime = double.NegativeInfinity;
            double scanRate = 0;
            long scansInWindow = 0;
            long triggers = 0;
            try
            {
                using (ScanTimer timer = new ScanTimer())
                using (Bitmap bitmap = new Bitmap(settings.Region.Width, settings.Region.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    while (!stopping)
                    {
                        double scanStarted = clock.Elapsed.TotalMilliseconds;
                        if (WindowsApi.StopKeyPressed())
                        {
                            RequestStop();
                            break;
                        }

                        // Moving HexBot over its own region would otherwise create visual feedback.
                        bool overlaps = !WindowsApi.IsIconic(settings.MainWindow) &&
                            WindowsApi.WindowBounds(settings.MainWindow).IntersectsWith(settings.Region);
                        if (overlaps)
                        {
                            triggerGate.ResetPresence();
                            Publish(new ScanStatus { Message = "Move or minimize HexBot; it covers the scan area.", Triggers = triggers }, null);
                            if (!timer.Wait(settings.ScanIntervalMs, stop))
                                break;
                            continue;
                        }
                        if (!SystemInformation.VirtualScreen.Contains(settings.Region))
                            throw new InvalidOperationException("Display layout changed. Select the scan area again.");

                        graphics.CopyFromScreen(settings.Region.Location, Point.Empty, settings.Region.Size, CopyPixelOperation.SourceCopy);
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
                            triggerGate.ResetPresence();
                        else
                            shouldTrigger = triggerGate.Evaluate(matched, now, settings.StableFrames, settings.CooldownMs, settings.Repeat, out presenceMs);

                        string inputError = null;
                        if (shouldTrigger)
                        {
                            lock (inputLock)
                            {
                                if (stopping || WindowsApi.StopKeyPressed())
                                    break;

                                string action = "would_fire";
                                if (settings.SendClicks)
                                {
                                    // Focus and pointer position can change during capture; check again.
                                    blocked = WindowsApi.ClickBlockReason(settings.TargetWindow, settings.TargetProcess, WindowsApi.CursorPosition());
                                    if (blocked == null)
                                    {
                                        bool sent = WindowsApi.SendLeftClick();
                                        action = sent ? "click_sent" : "input_failed";
                                        if (!sent)
                                            inputError = "Windows rejected the click. Monitoring stopped.";
                                    }
                                }
                                if (blocked == null)
                                {
                                    triggers++;
                                    QueueEvent(new TriggerEvent {
                                        TimestampUtc = DateTime.UtcNow, Action = action,
                                        MatchingPixels = matches, ScanMs = captureAndCountMs,
                                        PresenceMs = presenceMs, Settings = settings
                                    });
                                }
                                else
                                    triggerGate.ResetPresence();
                            }
                        }

                        // Actual throughput includes capture, filtering, input and scheduler delays.
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

                        // Skip missed deadlines instead of queuing a burst of late scans or clicks.
                        double workMs = clock.Elapsed.TotalMilliseconds - scanStarted;
                        if (!timer.Wait(settings.ScanIntervalMs - workMs, stop))
                            break;
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

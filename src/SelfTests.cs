using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QuickTrigger
{
    // Tests use synthetic pixels and timing inputs. They never submit mouse input.
    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static int passed;
        private static int failed;

        // Collect readable results instead of stopping at the first failed assertion.
        private static void Check(string name, bool condition)
        {
            results.Add((condition ? "PASS " : "FAIL ") + name);
            if (condition)
                passed++;
            else
                failed++;
        }

        // Run the groups once and use the process exit code for build scripts.
        public static int Run(string reportPath)
        {
            results.Clear();
            passed = 0;
            failed = 0;
            try
            {
                TestPixels();
                TestTriggerRules();
                TestSettings();
                TestHoldControl();
                TestHoldWorker();
                TestRandomDelay();
                TestDelayWorker();
                TestCsv();
                TestTimer();
                TestDisplayGeometry();
                TestAreaOverlay();
                TestInterface();
                Check("Windows input structure has the correct size",
                    Marshal.SizeOf(typeof(WindowsApi.MouseInputEvent)) == (IntPtr.Size == 8 ? 40 : 28));
                Check("An unbound click target is rejected",
                    WindowsApi.ClickBlockReason(IntPtr.Zero, 0, Point.Empty) != null);
            }
            catch (Exception error)
            {
                failed++;
                results.Add("FAIL Unexpected exception: " + error);
            }
            results.Add("");
            results.Add(passed + " passed; " + failed + " failed.");
            File.WriteAllLines(reportPath, results.ToArray());
            return failed == 0 ? 0 : 1;
        }

        // Boundary cases check channel order, inclusivity and bitmap edges.
        private static void TestPixels()
        {
            Color target = Color.FromArgb(100, 150, 200);
            Check("Exact RGB color matches", PixelDetector.Matches(100, 150, 200, target, 0));
            Check("Zero tolerance rejects off-by-one", !PixelDetector.Matches(101, 150, 200, target, 0));
            Check("Tolerance boundary is inclusive", PixelDetector.Matches(110, 140, 210, target, 10));
            Check("Red outside tolerance is rejected", !PixelDetector.Matches(111, 150, 200, target, 10));
            Check("Green outside tolerance is rejected", !PixelDetector.Matches(100, 139, 200, target, 10));
            Check("Blue outside tolerance is rejected", !PixelDetector.Matches(100, 150, 211, target, 10));
            Check("Channel arithmetic does not wrap", !PixelDetector.Matches(255, 0, 0, Color.Black, 1));
            Check("Full tolerance accepts every color", PixelDetector.Matches(0, 255, 255, Color.Red, 255));
            Color parsed;
            Check("Mixed-case hex parses", PixelDetector.TryParseColor(" #fFd54A ", out parsed) && parsed.R == 255 && parsed.G == 213 && parsed.B == 74);
            Check("Hex without # parses", PixelDetector.TryParseColor("FFD54A", out parsed));
            Check("Invalid hex is rejected", !PixelDetector.TryParseColor("#xx0055", out parsed));
            Check("Short hex is rejected", !PixelDetector.TryParseColor("#FFF", out parsed));
            Check("Eight-digit hex is rejected", !PixelDetector.TryParseColor("FFFFD54A", out parsed));
            Check("Repeated # is rejected", !PixelDetector.TryParseColor("##FFD54A", out parsed));
            Check("Hex output has six uppercase digits", PixelDetector.ToHex(Color.FromArgb(1, 2, 255)) == "#0102FF");
            using (Bitmap bitmap = new Bitmap(7, 5, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.Clear(Color.Black);
                bitmap.SetPixel(0, 0, target);
                bitmap.SetPixel(6, 4, target);
                bitmap.SetPixel(3, 2, Color.FromArgb(105, 145, 205));
                Check("Count includes first and last bitmap pixels", PixelDetector.Count(bitmap, target, 0) == 2);
                Check("Count respects tolerance", PixelDetector.Count(bitmap, target, 5) == 3);
                Check("Count covers the entire bitmap", PixelDetector.Count(bitmap, target, 255) == 35);
                bool rejected = false;
                try
                {
                    PixelDetector.Count(bitmap, target, -1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    rejected = true;
                }
                Check("Negative tolerance is rejected", rejected);
            }
        }

        // A fake monotonic clock makes 1 ms boundaries deterministic and reproducible.
        private static void TestTriggerRules()
        {
            TriggerGate gate = new TriggerGate();
            double presence;
            Check("Two-frame filter waits for the second scan", !gate.Evaluate(true, 0, 2, 250, false, out presence));
            Check("Second matching scan fires", gate.Evaluate(true, 33, 2, 250, false, out presence) && presence == 33);
            gate.RecordTrigger(33);
            Check("Continuous match fires once without repeat", !gate.Evaluate(true, 1000, 2, 250, false, out presence));
            Check("Nonmatch clears the presence", !gate.Evaluate(false, 1010, 2, 250, false, out presence));
            Check("New presence must stabilize", !gate.Evaluate(true, 1040, 2, 250, false, out presence));
            Check("New stabilized presence fires", gate.Evaluate(true, 1073, 2, 250, false, out presence));
            gate.RecordTrigger(1073);
            gate.Reset();
            Check("1 ms cooldown allows the first trigger", gate.Evaluate(true, 0, 1, 1, true, out presence));
            gate.RecordTrigger(0);
            Check("1 ms cooldown rejects 0.999 ms", !gate.Evaluate(true, 0.999, 1, 1, true, out presence));
            Check("1 ms cooldown fires exactly at 1 ms", gate.Evaluate(true, 1, 1, 1, true, out presence));
            gate.RecordTrigger(1);
            Check("Repeated trigger still requires another 1 ms", !gate.Evaluate(true, 1.999, 1, 1, true, out presence));
            Check("Repeated trigger fires at 2 ms", gate.Evaluate(true, 2, 1, 1, true, out presence));
            gate.RecordTrigger(2);
            gate.ResetPresence();
            Check("Focus interruption preserves cooldown", !gate.Evaluate(true, 2.5, 1, 1, true, out presence));
            gate.Reset();
            Check("New session clears cooldown", gate.Evaluate(true, 2.5, 1, 1, false, out presence));
            gate.Reset();
            gate.Evaluate(true, 0, 3, 1, false, out presence);
            gate.Evaluate(false, 1, 3, 1, false, out presence);
            gate.Evaluate(true, 2, 3, 1, false, out presence);
            Check("Separated matches are not consecutive", !gate.Evaluate(true, 3, 3, 1, false, out presence));
            Check("Third consecutive frame fires", gate.Evaluate(true, 4, 3, 1, false, out presence) && presence == 2);

            // A delayed or cancelled qualification must not shorten the gap between actual events.
            gate.Reset();
            Check("A match can qualify before its delayed submission", gate.Evaluate(true, 0, 1, 100, true, out presence));
            gate.RecordTrigger(50);
            Check("Cooldown starts after the delay, not when the match qualified", !gate.Evaluate(true, 100, 1, 100, true, out presence));
            Check("Delayed cooldown rejects its final fractional millisecond", !gate.Evaluate(true, 149.999, 1, 100, true, out presence));
            Check("Delayed cooldown expires at the actual submission boundary", gate.Evaluate(true, 150, 1, 100, true, out presence));
            gate.Reset();
            gate.Evaluate(true, 0, 1, int.MaxValue, false, out presence);
            gate.ResetPresence();
            Check("Cancelling a pending delay consumes neither cooldown nor presence", gate.Evaluate(true, 1, 1, int.MaxValue, false, out presence));
        }

        // The old lower and upper timing caps must not reappear in configuration validation.
        private static void TestSettings()
        {
            ScanSettings settings = new ScanSettings();
            settings.Region = new Rectangle(-120, 0, 120, 120);
            settings.CooldownMs = 1;
            settings.ScanIntervalMs = 1;
            Check("Both timing settings accept 1 ms", settings.Validate() == null);
            settings.CooldownMs = 20000;
            settings.ScanIntervalMs = 3000;
            Check("Values beyond the former upper caps are accepted", settings.Validate() == null);
            settings.CooldownMs = int.MaxValue;
            settings.ScanIntervalMs = int.MaxValue;
            Check("Full positive integer timing range is accepted", settings.Validate() == null);
            settings.ScanIntervalMs = 0;
            Check("Zero scan interval is rejected", settings.Validate() != null);
            settings.ScanIntervalMs = 1;
            settings.CooldownMs = 0;
            Check("Zero cooldown is rejected", settings.Validate() != null);
            settings.CooldownMs = 1;
            settings.MinimumPixels = 14401;
            Check("Minimum pixels cannot exceed area", settings.Validate() != null);
            settings.MinimumPixels = 8;
            settings.HoldToScan = true;
            foreach (Keys key in new Keys[] { Keys.A, Keys.Tab, Keys.LShiftKey, Keys.RControlKey, Keys.LMenu,
                Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2 })
            {
                settings.HoldKey = (int)key;
                Check("Hold binding accepts " + ActivationKey.Name(settings.HoldKey), settings.Validate() == null);
            }
            foreach (Keys key in new Keys[] { Keys.Escape, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.None })
            {
                settings.HoldKey = (int)key;
                Check("Hold binding rejects reserved or missing key " + key, settings.Validate() != null);
            }
            settings.HoldKey = 7;
            Check("Hold binding rejects an undefined virtual key", settings.Validate() != null);
            settings.HoldKey = (int)Keys.LButton;
            settings.SendClicks = true;
            Check("Left mouse hold cannot conflict with generated left clicks", settings.Validate() != null);
            settings.HoldKey = (int)Keys.XButton1;
            Check("Side mouse hold works with click mode settings", settings.Validate() == null);
            settings.HoldToScan = false;
            settings.HoldKey = 0;
            Check("An unused binding does not block continuous mode", settings.Validate() == null);
        }

        // Fake time and held-button state make release, cooldown and deadline boundaries exact.
        private static void TestHoldControl()
        {
            bool held = false;
            ScanSettings settings = new ScanSettings {
                HoldToScan = true, HoldKey = (int)Keys.XButton1,
                StableFrames = 2, CooldownMs = 100, ScanIntervalMs = int.MaxValue
            };
            ScanControl control = new ScanControl(settings, delegate(int key) { return held && key == settings.HoldKey; });
            double presence;
            Check("Released hold never schedules a scan", !control.IsScanDue(0) && control.WaitingForHold);
            Check("Released hold cannot trigger even with a matching frame", !control.TryTrigger(true, 0, out presence));
            held = true;
            Check("Pressing the hold button permits the first scan", control.IsScanDue(10));
            control.ScheduleNextScan(10);
            Check("Holding does not bypass a long scan interval", !control.IsScanDue(11));
            Check("Long intervals still poll release within five requested milliseconds", control.WaitMilliseconds(11) == 5);
            Check("First held frame must stabilize", !control.TryTrigger(true, 10, out presence));
            held = false;
            Check("Release interrupts the interval", !control.IsScanDue(11));
            held = true;
            Check("Repress starts a fresh scan without waiting for the old deadline", control.IsScanDue(12));
            Check("A released partial match cannot carry into the next hold", !control.TryTrigger(true, 12, out presence));
            Check("Fresh consecutive matches qualify", control.TryTrigger(true, 13, out presence) && presence == 1);
            control.RecordTrigger(13);
            held = false;
            control.IsActive();
            held = true;
            control.TryTrigger(true, 14, out presence);
            Check("Repress cannot bypass the existing cooldown", !control.TryTrigger(true, 15, out presence));
            Check("A new hold can trigger when the preserved cooldown expires", control.TryTrigger(true, 113, out presence));
            control.RecordTrigger(113);
            Check("The same held presence still fires only once", !control.TryTrigger(true, 300, out presence));
            held = false;
            Check("Releasing before submission blocks a qualified frame", !control.IsActive());
            settings.ScanIntervalMs = 1;
            held = true;
            control.IsScanDue(400);
            control.ScheduleNextScan(400);
            Check("Hold polling preserves sub-five-millisecond scan intervals", control.WaitMilliseconds(400.25) == 0.75);
            Check("An overdue held scan does not wait an extra polling interval", control.WaitMilliseconds(402) == 0);

            // Continuous mode must not query or depend on the configured hold button.
            settings.HoldToScan = false;
            settings.StableFrames = 1;
            ScanControl continuous = new ScanControl(settings, delegate(int key) { throw new InvalidOperationException("Unexpected hold query."); });
            Check("Continuous mode scans without a held key", continuous.IsScanDue(0));
            Check("Continuous mode still produces trigger events", continuous.TryTrigger(true, 0, out presence));
        }

        // Exercise the actual worker using synthetic pixels; no desktop is read and no input is sent.
        private static void TestHoldWorker()
        {
            ScanSettings settings = new ScanSettings {
                Region = new Rectangle(Screen.PrimaryScreen.Bounds.Location, new Size(16, 16)),
                TargetColor = Color.Gold, HoldToScan = true, HoldKey = (int)Keys.XButton1,
                StableFrames = 1, MinimumPixels = 1, CooldownMs = 1, ScanIntervalMs = int.MaxValue
            };
            int held = 0;
            int captures = 0;
            Func<int, bool> keyDown = delegate(int key) {
                return key == settings.HoldKey && Interlocked.CompareExchange(ref held, 0, 0) != 0;
            };
            using (ScanWorker worker = new ScanWorker(settings, keyDown, delegate(Graphics graphics) {
                graphics.Clear(settings.TargetColor);
                Interlocked.Increment(ref captures);
            }))
            {
                worker.Start();
                ScanUpdate idle = WaitForStatus(worker, delegate(ScanStatus status) { return status.WaitingForHold; });
                Thread.Sleep(30);
                Check("Armed worker captures nothing while the button is released", Interlocked.CompareExchange(ref captures, 0, 0) == 0);
                Check("Idle worker has no triggers and clears the preview", idle.Status.Triggers == 0 && idle.ClearPreview);
                Interlocked.Exchange(ref held, 1);
                WaitForStatus(worker, delegate(ScanStatus status) { return status.Triggers == 1; });
                Thread.Sleep(30);
                Check("Held worker honors the requested long scan interval", Interlocked.CompareExchange(ref captures, 0, 0) == 1);
                Interlocked.Exchange(ref held, 0);
                idle = WaitForStatus(worker, delegate(ScanStatus status) { return status.WaitingForHold; });
                Check("Release clears match metrics and stale preview", idle.Status.MatchingPixels == 0 && idle.Status.ScansPerSecond == 0 && idle.ClearPreview);
                Interlocked.Exchange(ref held, 1);
                WaitForStatus(worker, delegate(ScanStatus status) { return status.Triggers == 2; });
                Check("Repress resumes the real worker before its old long deadline", Interlocked.CompareExchange(ref captures, 0, 0) == 2);
                worker.RequestStop();
                Stopwatch stopTime = Stopwatch.StartNew();
                while (!worker.IsFinished && stopTime.ElapsedMilliseconds < 2000)
                    Thread.Sleep(1);
                Check("Disarming interrupts a held worker's long interval", worker.IsFinished);
            }

            // Release in the capture callback to reproduce a button-up during an in-flight frame.
            held = 1;
            captures = 0;
            using (ScanWorker worker = new ScanWorker(settings, keyDown, delegate(Graphics graphics) {
                graphics.Clear(settings.TargetColor);
                Interlocked.Increment(ref captures);
                Interlocked.Exchange(ref held, 0);
            }))
            {
                worker.Start();
                ScanUpdate idle = WaitForStatus(worker, delegate(ScanStatus status) { return status.WaitingForHold; });
                Check("Releasing during capture discards the matching frame", captures == 1 && idle.Status.Triggers == 0 && idle.Events.Length == 0);
            }
        }

        // Bound each asynchronous check and dispose the preview copies the test consumes.
        private static ScanUpdate WaitForStatus(ScanWorker worker, Func<ScanStatus, bool> condition)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 2000)
            {
                ScanUpdate update = worker.ReadUpdate();
                if (update.Preview != null)
                    update.Preview.Dispose();
                if (update.Status.Error)
                    throw new InvalidOperationException(update.Status.Message);
                if (condition(update.Status))
                    return update;
                Thread.Sleep(1);
            }
            throw new InvalidOperationException("Timed out waiting for the synthetic scan worker.");
        }

        // Fake clock/timer callbacks exercise early wakeups, overshoot and cancellation exactly.
        private static void TestRandomDelay()
        {
            TriggerDelay picker = new TriggerDelay(new Random(4721));
            HashSet<int> samples = new HashSet<int>();
            bool inRange = true;
            for (int index = 0; index < 10000; index++)
            {
                int sample = picker.NextMilliseconds(true);
                inRange &= sample >= 1 && sample <= 50;
                samples.Add(sample);
            }
            Check("Every random delay is inside the inclusive 1-50 ms range", inRange);
            Check("Random sampling reaches both endpoints and all 50 values", samples.Count == 50 && samples.Contains(1) && samples.Contains(50));
            TriggerDelay disabled = new TriggerDelay(new ScriptedDelayRandom(delegate { throw new InvalidOperationException("Disabled delay sampled randomness."); }));
            Check("Disabled random delay requests zero without sampling", disabled.NextMilliseconds(false) == 0);

            double now = 0;
            double elapsed;
            double longestSlice = 0;
            bool active = true;
            Func<double> clock = delegate { return now; };
            Func<bool> canContinue = delegate { return active; };
            Func<double, bool> earlyTimer = delegate(double ms) {
                longestSlice = Math.Max(longestSlice, ms);
                now += Math.Min(ms, 0.5);
                return true;
            };
            Check("A 1 ms request cannot fire on an early timer wakeup", TriggerDelay.Wait(1, clock, earlyTimer, canContinue, out elapsed) && elapsed == 1);
            now = 0;
            Check("The full 50 ms delay completes without firing early", TriggerDelay.Wait(50, clock, earlyTimer, canContinue, out elapsed) && elapsed == 50);
            Check("Delay slices poll cancellation at most every five requested milliseconds", longestSlice <= 5);
            now = 0;
            Func<double, bool> lateTimer = delegate(double ms) { now += ms + 12.5; return true; };
            Check("Measured wait retains scheduler overshoot", TriggerDelay.Wait(1, clock, lateTimer, canContinue, out elapsed) && elapsed == 13.5);
            now = 0;
            Func<double, bool> releaseDuringWait = delegate(double ms) { now += ms; active = false; return true; };
            Check("Hold release cancels a delay after it has started", !TriggerDelay.Wait(50, clock, releaseDuringWait, canContinue, out elapsed) && elapsed == 5);
            active = true;
            now = 0;
            Func<double, bool> stopDuringWait = delegate(double ms) { now += 2; return false; };
            Check("The stop signal interrupts an in-flight delay", !TriggerDelay.Wait(50, clock, stopDuringWait, canContinue, out elapsed) && elapsed == 2);
            active = false;
            Func<double, bool> unexpectedWait = delegate(double ms) { throw new InvalidOperationException("Unexpected wait."); };
            Check("An already cancelled trigger never waits", !TriggerDelay.Wait(50, clock, unexpectedWait, canContinue, out elapsed));
            active = true;
            Check("Disabled delay does not invoke the timer", TriggerDelay.Wait(0, clock, unexpectedWait, canContinue, out elapsed) && elapsed == 0);
        }

        // Exercise the full scanner with synthetic frames and exact delay choices, without clicks.
        private static void TestDelayWorker()
        {
            ScanSettings settings = new ScanSettings {
                Region = new Rectangle(Screen.PrimaryScreen.Bounds.Location, new Size(16, 16)),
                TargetColor = Color.Gold, StableFrames = 1, MinimumPixels = 1,
                CooldownMs = 15, ScanIntervalMs = 1, Repeat = true, RandomDelay = true
            };
            int choices = 0;
            Action<Graphics> capture = delegate(Graphics graphics) { graphics.Clear(settings.TargetColor); };
            using (ScanWorker worker = new ScanWorker(settings, delegate(int key) { return false; }, capture,
                new ScriptedDelayRandom(delegate { return Interlocked.Increment(ref choices) % 2 == 1 ? 50 : 1; })))
            {
                worker.Start();
                TriggerEvent[] entries = WaitForEvents(worker, 2);
                worker.RequestStop();
                Check("Each repeated trigger draws a fresh delay, including both endpoints", entries[0].RequestedDelayMs == 50 && entries[1].RequestedDelayMs == 1 && choices >= 2);
                Check("The real worker waits at least the chosen delay before logging", entries[0].ActualDelayMs >= 50 && entries[1].ActualDelayMs >= 1);
                Check("Delay affects detection-only events too", entries[0].Action == "would_fire" && entries[1].Action == "would_fire");
            }

            settings.RandomDelay = false;
            settings.Repeat = false;
            settings.ScanIntervalMs = int.MaxValue;
            using (ScanWorker worker = new ScanWorker(settings, delegate(int key) { return false; }, capture,
                new ScriptedDelayRandom(delegate { throw new InvalidOperationException("Disabled worker sampled delay."); })))
            {
                worker.Start();
                TriggerEvent entry = WaitForEvents(worker, 1)[0];
                Check("Turning delay off preserves immediate triggering and zero delay logs", entry.RequestedDelayMs == 0 && entry.ActualDelayMs == 0);
            }

            // Release on the second hold poll after selection, once the first delay slice has run.
            settings.RandomDelay = true;
            settings.HoldToScan = true;
            settings.CooldownMs = int.MaxValue;
            int selected = 0;
            int held = 1;
            int releaseDuringDelay = 1;
            int holdPolls = 0;
            Func<int, bool> heldKey = delegate(int key) {
                if (key != settings.HoldKey)
                    return false;
                if (Interlocked.CompareExchange(ref selected, 0, 0) != 0 &&
                    Interlocked.CompareExchange(ref releaseDuringDelay, 0, 0) != 0 &&
                    Interlocked.Increment(ref holdPolls) >= 2)
                    Interlocked.Exchange(ref held, 0);
                return Interlocked.CompareExchange(ref held, 0, 0) != 0;
            };
            using (ScanWorker worker = new ScanWorker(settings, heldKey, capture,
                new ScriptedDelayRandom(delegate { Interlocked.Exchange(ref selected, 1); return 50; })))
            {
                worker.Start();
                ScanUpdate released = WaitForStatus(worker, delegate(ScanStatus status) { return status.WaitingForHold; });
                Check("The real worker discards a trigger when hold is released during its delay", released.Status.Triggers == 0 && released.Events.Length == 0);
                Interlocked.Exchange(ref releaseDuringDelay, 0);
                Interlocked.Exchange(ref held, 1);
                TriggerEvent entry = WaitForEvents(worker, 1)[0];
                Check("A cancelled delay does not consume even the maximum cooldown", entry.Action == "would_fire" && entry.RequestedDelayMs == 50);
            }

            // F7 is polled between slices even when the UI has not processed its hotkey message.
            settings.HoldToScan = false;
            selected = 0;
            int stopPolls = 0;
            Func<int, bool> emergencyKey = delegate(int key) {
                return key == (int)Keys.F7 && Interlocked.CompareExchange(ref selected, 0, 0) != 0 &&
                    Interlocked.Increment(ref stopPolls) >= 2;
            };
            using (ScanWorker worker = new ScanWorker(settings, emergencyKey, capture,
                new ScriptedDelayRandom(delegate { Interlocked.Exchange(ref selected, 1); return 50; })))
            {
                worker.Start();
                WaitForWorkerExit(worker);
                ScanUpdate stopped = worker.ReadUpdate();
                if (stopped.Preview != null)
                    stopped.Preview.Dispose();
                Check("F7 during an actual delay stops without submitting a pending event", !stopped.Status.Error && stopped.Status.Triggers == 0 && stopped.Events.Length == 0);
            }

            // RequestStop may come from the UI while a trigger is pending; it must never queue later.
            ScanWorker stoppingWorker = null;
            using (stoppingWorker = new ScanWorker(settings, delegate(int key) { return false; }, capture,
                new ScriptedDelayRandom(delegate { stoppingWorker.RequestStop(); return 50; })))
            {
                stoppingWorker.Start();
                WaitForWorkerExit(stoppingWorker);
                ScanUpdate stopped = stoppingWorker.ReadUpdate();
                if (stopped.Preview != null)
                    stopped.Preview.Dispose();
                Check("RequestStop discards a qualified pending trigger", !stopped.Status.Error && stopped.Events.Length == 0 && stopped.Status.Triggers == 0);
            }
        }

        // Preserve events across status polls so a fast producer cannot race the test reader.
        private static TriggerEvent[] WaitForEvents(ScanWorker worker, int count)
        {
            List<TriggerEvent> entries = new List<TriggerEvent>();
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 3000)
            {
                ScanUpdate update = worker.ReadUpdate();
                if (update.Preview != null)
                    update.Preview.Dispose();
                if (update.Status.Error)
                    throw new InvalidOperationException(update.Status.Message);
                entries.AddRange(update.Events);
                if (entries.Count >= count)
                    return entries.ToArray();
                Thread.Sleep(1);
            }
            throw new InvalidOperationException("Timed out waiting for delayed trigger events.");
        }

        // Bound cancellation tests so a regression cannot hang the test process.
        private static void WaitForWorkerExit(ScanWorker worker)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!worker.IsFinished && clock.ElapsedMilliseconds < 2000)
                Thread.Sleep(1);
            if (!worker.IsFinished)
                throw new InvalidOperationException("The stopped delay worker did not exit.");
        }

        // Script exact samples and reject a range that accidentally excludes either endpoint.
        private sealed class ScriptedDelayRandom : Random
        {
            private readonly Func<int> next;

            public ScriptedDelayRandom(Func<int> next)
            {
                this.next = next;
            }

            public override int Next(int minValue, int maxValue)
            {
                if (minValue != 1 || maxValue != 51)
                    throw new InvalidOperationException("Unexpected random delay bounds.");
                return next();
            }
        }

        // Export formatting must stay valid on computers that use a decimal comma.
        private static void TestCsv()
        {
            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                TriggerEvent entry = new TriggerEvent();
                entry.TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                entry.Action = "would_fire";
                entry.MatchingPixels = 1800;
                entry.ScanMs = 1.25;
                entry.PresenceMs = 33.5;
                entry.Settings = new ScanSettings {
                    Region = new Rectangle(-120, 0, 120, 120),
                    TargetColor = Color.FromArgb(255, 213, 74),
                    CooldownMs = 1, ScanIntervalMs = 1
                };
                string csv = entry.ToCsv();
                Check("CSV columns match the header", csv.Split(',').Length == TriggerEvent.CsvHeader.Split(',').Length);
                Check("CSV uses decimal points in every locale", csv.Contains("0.125000,1.250,33.500"));
                Check("CSV preserves UTC and negative coordinates", csv.Contains("Z,would_fire") && csv.Contains(",-120,0,120,120,#FFD54A,"));
                Check("CSV retains the requested 1 ms values", csv.EndsWith(",1,1,false,false,5,false,0,0.000"));
                entry.Settings.HoldToScan = true;
                entry.Settings.HoldKey = (int)Keys.RButton;
                Check("CSV records hold mode and the selected button", entry.ToCsv().EndsWith(",false,true,2,false,0,0.000"));
                entry.Settings.RandomDelay = true;
                entry.RequestedDelayMs = 50;
                entry.ActualDelayMs = 52.125;
                Check("CSV records the delay toggle, sample and measured wait with decimal points", entry.ToCsv().EndsWith(",true,50,52.125"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        // Measure timer waits separately from capture, and verify cancellation of long waits.
        private static void TestTimer()
        {
            using (ScanTimer timer = new ScanTimer())
            using (ManualResetEvent stop = new ManualResetEvent(false))
            {
                List<double> samples = new List<double>();
                Stopwatch clock = Stopwatch.StartNew();
                for (int index = 0; index < 40; index++)
                {
                    double before = clock.Elapsed.TotalMilliseconds;
                    if (!timer.Wait(1, stop))
                        throw new InvalidOperationException("Unexpected timer cancellation.");
                    samples.Add(clock.Elapsed.TotalMilliseconds - before);
                }
                samples.Sort();
                results.Add(string.Format(CultureInfo.InvariantCulture,
                    "INFO 1 ms timer-only waits: median {0:F3} ms, p95 {1:F3} ms; high resolution: {2}",
                    samples[20], samples[38], timer.IsHighResolution));
                Check("1 ms timer waits complete", samples.Count == 40);
                stop.Set();
                clock.Restart();
                Check("Cancellation interrupts the maximum interval", !timer.Wait(int.MaxValue, stop) && clock.ElapsedMilliseconds < 1000);
                Check("Cancellation also interrupts an overdue scan", !timer.Wait(0, stop));
            }
        }

        // Construct the real form without showing it and exercise its numeric controls.
        private static void TestInterface()
        {
            using (MainForm form = new MainForm())
            {
                NumericUpDown cooldown = (NumericUpDown)form.Controls.Find("Cooldownms", true)[0];
                NumericUpDown interval = (NumericUpDown)form.Controls.Find("Scanintervalms", true)[0];
                cooldown.Value = 1;
                interval.Value = 1;
                Check("UI accepts 1 ms for both settings", cooldown.Value == 1 && interval.Value == 1);
                Check("UI timing fields have no former upper caps", cooldown.Maximum == int.MaxValue && interval.Maximum == int.MaxValue);
                Check("Main window is named Quick Trigger", form.Text == "Quick Trigger");
                CheckBox randomDelay = (CheckBox)form.Controls.Find("RandomDelay", true)[0];
                Check("Random delay starts disabled", !randomDelay.Checked);
                randomDelay.Checked = true;
                System.Reflection.MethodInfo readSettings = typeof(MainForm).GetMethod("ReadSettings",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Check("The delay checkbox reaches the scan settings", ((ScanSettings)readSettings.Invoke(form, null)).RandomDelay);
                randomDelay.Checked = false;
                Check("Turning the delay checkbox off reaches the scan settings", !((ScanSettings)readSettings.Invoke(form, null)).RandomDelay);
                CheckBox hold = (CheckBox)form.Controls.Find("HoldToScan", true)[0];
                Button binding = (Button)form.Controls.Find("HoldBinding", true)[0];
                Check("Hold mode starts disabled and retains the default mouse side button", !hold.Checked && !binding.Enabled && binding.Text == "Mouse 4 (Back)");
                hold.Checked = true;
                Check("Enabling hold mode exposes the binding control", binding.Enabled);
                hold.Checked = false;
                Check("Disabling hold mode preserves its selected binding", !binding.Enabled && binding.Text == "Mouse 4 (Back)");
                ComboBox display = (ComboBox)form.Controls.Find("DisplaySelector", true)[0];
                Check("Display selector lists every connected display", display.Items.Count == Screen.AllScreens.Length);
                Check("Display selector starts on the primary monitor", ((DisplayChoice)display.SelectedItem).IsPrimary);
                Check("Display selector requires a listed choice", display.DropDownStyle == ComboBoxStyle.DropDownList);
                if (display.Items.Count > 1)
                {
                    display.SelectedIndex = (display.SelectedIndex + 1) % display.Items.Count;
                    DisplayChoice chosen = (DisplayChoice)display.SelectedItem;
                    NumericUpDown left = (NumericUpDown)form.Controls.Find("Left", true)[0];
                    NumericUpDown top = (NumericUpDown)form.Controls.Find("Top", true)[0];
                    Rectangle expected = DisplayLayout.Centered(chosen.Bounds, 120, 120);
                    Check("Changing the selected display updates physical coordinates", left.Value == expected.Left && top.Value == expected.Top);
                }
            }
        }

        // Synthetic layouts cover negative origins, uneven alignment and edge clipping.
        private static void TestDisplayGeometry()
        {
            Rectangle leftDisplay = new Rectangle(-1920, 0, 1920, 1080);
            Rectangle upperRightDisplay = new Rectangle(1920, -240, 2560, 1440);
            Check("Area centers correctly on a left-hand monitor",
                DisplayLayout.Centered(leftDisplay, 120, 120) == new Rectangle(-1020, 480, 120, 120));
            Check("Area centers correctly on an offset monitor",
                DisplayLayout.Centered(upperRightDisplay, 120, 120) == new Rectangle(3140, 420, 120, 120));
            Check("An area cannot cross the selected display's right edge",
                DisplayLayout.Fit(new Rectangle(-50, 200, 120, 120), leftDisplay) == new Rectangle(-120, 200, 120, 120));
            Check("An area outside the left edge moves onto its display",
                DisplayLayout.Fit(new Rectangle(-5000, -200, 120, 120), leftDisplay) == new Rectangle(-1920, 0, 120, 120));
            Check("A small display reduces an oversized area",
                DisplayLayout.Centered(new Rectangle(0, 0, 800, 600), 1024, 1024) == new Rectangle(0, 0, 800, 600));
            Check("A valid region keeps its position",
                DisplayLayout.Fit(new Rectangle(-1500, 200, 120, 120), leftDisplay) == new Rectangle(-1500, 200, 120, 120));
            ScanSettings settings = new ScanSettings {
                Region = new Rectangle(-1500, 200, 120, 120), DisplayDeviceName = "TestDisplay", DisplayBounds = leftDisplay
            };
            Check("Settings accept an area inside the selected display", settings.Validate() == null);
            settings.Region = new Rectangle(-50, 200, 120, 120);
            Check("Settings reject an area spanning two displays", settings.Validate() != null);
            settings.Region = new Rectangle(200, 200, 120, 120);
            Check("Settings reject an area wholly on another display", settings.Validate() != null);
            DisplayChoice choice = new DisplayChoice("TestDisplay", leftDisplay, true);
            Check("Display labels include resolution and primary status", choice.ToString().Contains("1920 x 1080") && choice.ToString().Contains("primary"));
            Check("Unknown display identity is rejected", !DisplayChoice.StillConnected("Missing test display", leftDisplay));
            using (RegionPicker picker = new RegionPicker(leftDisplay))
                Check("Area picker is restricted to the selected monitor", picker.Bounds == leftDisplay);
        }

        // Verify fade boundaries and border geometry without showing a desktop overlay.
        private static void TestAreaOverlay()
        {
            Check("Overlay starts bright", AreaOverlay.OpacityAt(0) == AreaOverlay.BrightOpacity);
            Check("Overlay stays bright for two seconds", AreaOverlay.OpacityAt(2000) == AreaOverlay.BrightOpacity);
            Check("Overlay is halfway faded at 2.5 seconds", Math.Abs(AreaOverlay.OpacityAt(2500) - AreaOverlay.BrightOpacity / 2) < 0.00001);
            Check("Overlay remains visible just before three seconds", AreaOverlay.OpacityAt(2999) > 0);
            Check("Overlay disappears at three seconds", AreaOverlay.OpacityAt(3000) == 0);
            Check("Overlay remains gone after its lifetime", AreaOverlay.OpacityAt(5000) == 0);
            using (Region border = AreaOverlay.BorderShape(new Size(120, 120)))
            {
                Check("Overlay border includes the outer corner", border.IsVisible(0, 0));
                Check("Overlay border includes the far edge", border.IsVisible(119, 60));
                Check("Overlay center is completely open", !border.IsVisible(60, 60));
                Check("Overlay does not extend beyond the scan area", !border.IsVisible(120, 60));
            }
            using (Region border = AreaOverlay.BorderShape(new Size(1, 1)))
                Check("A one-pixel scan area still has an indicator", border.IsVisible(0, 0));
            using (AreaOverlay overlay = new AreaOverlay())
            {
                Check("Overlay uses bright green", overlay.BackColor == Color.Lime);
                Check("Overlay has no taskbar entry", !overlay.ShowInTaskbar);
                Check("Overlay preserves physical pixel coordinates", overlay.AutoScaleMode == AutoScaleMode.None);
            }
        }
    }
}

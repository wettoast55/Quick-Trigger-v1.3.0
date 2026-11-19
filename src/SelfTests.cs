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

namespace HexBot
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
                TestCsv();
                TestTimer();
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
            Check("Continuous match fires once without repeat", !gate.Evaluate(true, 1000, 2, 250, false, out presence));
            Check("Nonmatch clears the presence", !gate.Evaluate(false, 1010, 2, 250, false, out presence));
            Check("New presence must stabilize", !gate.Evaluate(true, 1040, 2, 250, false, out presence));
            Check("New stabilized presence fires", gate.Evaluate(true, 1073, 2, 250, false, out presence));
            gate.Reset();
            Check("1 ms cooldown allows the first trigger", gate.Evaluate(true, 0, 1, 1, true, out presence));
            Check("1 ms cooldown rejects 0.999 ms", !gate.Evaluate(true, 0.999, 1, 1, true, out presence));
            Check("1 ms cooldown fires exactly at 1 ms", gate.Evaluate(true, 1, 1, 1, true, out presence));
            Check("Repeated trigger still requires another 1 ms", !gate.Evaluate(true, 1.999, 1, 1, true, out presence));
            Check("Repeated trigger fires at 2 ms", gate.Evaluate(true, 2, 1, 1, true, out presence));
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
                Check("CSV retains the requested 1 ms values", csv.EndsWith(",1,1,false"));
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
                Check("Main window is named HexBot", form.Text == "HexBot");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ColorTriggerLab
{
    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static int failures;
        private static void Check(string name, bool condition) { results.Add((condition ? "PASS " : "FAIL ") + name); if (!condition) failures++; }

        // These deterministic tests never capture another application or send input.
        public static int Run(string reportPath)
        {
            try
            {
                Color target = Color.FromArgb(100, 150, 200);
                Check("exact RGB match", PixelDetector.Matches(100, 150, 200, target, 0));
                Check("zero tolerance rejects off-by-one", !PixelDetector.Matches(101, 150, 200, target, 0));
                Check("inclusive tolerance in all channels", PixelDetector.Matches(110, 140, 210, target, 10));
                Check("red outside tolerance rejected", !PixelDetector.Matches(111, 150, 200, target, 10));
                Check("green outside tolerance rejected", !PixelDetector.Matches(100, 139, 200, target, 10));
                Check("blue outside tolerance rejected", !PixelDetector.Matches(100, 150, 211, target, 10));
                Check("channel limits do not wrap", !PixelDetector.Matches(255, 0, 0, Color.Black, 1));
                Check("full tolerance accepts every color", PixelDetector.Matches(0, 255, 255, Color.Red, 255));
                Color parsed;
                Check("hex color parsed", PixelDetector.TryParseColor(" #fFd54A ", out parsed) && parsed.R == 255 && parsed.G == 213 && parsed.B == 74);
                Check("invalid hex rejected", !PixelDetector.TryParseColor("#xx0055", out parsed));
                Check("short hex rejected", !PixelDetector.TryParseColor("#FFF", out parsed));
                Check("alpha hex rejected", !PixelDetector.TryParseColor("FFFFD54A", out parsed));

                using (Bitmap frame = new Bitmap(7, 5, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(frame)) g.Clear(Color.Black);
                    frame.SetPixel(0, 0, target); frame.SetPixel(6, 4, target); frame.SetPixel(3, 2, Color.FromArgb(105, 145, 205));
                    Check("bitmap BGRA channels, last row and last column", PixelDetector.Count(frame, target, 0) == 2);
                    Check("bitmap tolerance count", PixelDetector.Count(frame, target, 5) == 3);
                    Check("bitmap full region count", PixelDetector.Count(frame, target, 255) == 35);
                    bool badTolerance = false;
                    try { PixelDetector.Count(frame, target, -1); } catch (ArgumentOutOfRangeException) { badTolerance = true; }
                    Check("negative tolerance rejected", badTolerance);
                }

                TriggerGate gate = new TriggerGate();
                double latency;
                Check("one frame is insufficient for two-frame threshold", !gate.Evaluate(true, 0, 2, 250, false, out latency));
                Check("second matching frame fires and reports observed delay", gate.Evaluate(true, 33, 2, 250, false, out latency) && latency == 33);
                Check("continuous presence fires only once", !gate.Evaluate(true, 1000, 2, 250, false, out latency));
                Check("negative frame clears presence", !gate.Evaluate(false, 1010, 2, 250, false, out latency));
                Check("new presence must stabilize again", !gate.Evaluate(true, 1040, 2, 250, false, out latency));
                Check("new stable presence fires", gate.Evaluate(true, 1073, 2, 250, false, out latency));
                gate.Reset();
                Check("first frame may fire at threshold one", gate.Evaluate(true, 0, 1, 250, true, out latency));
                Check("repeat respects cooldown", !gate.Evaluate(true, 249, 1, 250, true, out latency));
                Check("repeat fires at exact cooldown boundary", gate.Evaluate(true, 250, 1, 250, true, out latency));
                gate.ResetPresence();
                Check("focus interruption preserves cooldown", !gate.Evaluate(true, 300, 1, 250, true, out latency));
                gate.Reset();
                Check("new monitoring session resets cooldown", gate.Evaluate(true, 301, 1, 250, false, out latency));
                gate.Reset();
                gate.Evaluate(true, 0, 3, 250, false, out latency);
                gate.Evaluate(false, 33, 3, 250, false, out latency);
                gate.Evaluate(true, 66, 3, 250, false, out latency);
                Check("separated matching frames are not consecutive", !gate.Evaluate(true, 99, 3, 250, false, out latency));
                Check("third consecutive frame fires after interruption", gate.Evaluate(true, 132, 3, 250, false, out latency) && latency == 66);
                gate.ResetPresence();
                Check("focus loss clears stable frame history", !gate.Evaluate(true, 1000, 3, 250, false, out latency));
                Check("Windows INPUT layout matches process architecture", Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28));
                Check("no click target is blocked", Native.ClickBlockReason(IntPtr.Zero, 0, Point.Empty) != null);

                CultureInfo previous = Thread.CurrentThread.CurrentCulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    TriggerEvent item = new TriggerEvent { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Action = "would_fire", Matches = 3, Fraction = 0.125, ScanMs = 1.25, PresenceMs = 33.5, Region = new Rectangle(-120, 0, 120, 120), Color = Color.FromArgb(255, 213, 74), Tolerance = 18, MinimumPixels = 8, StableFrames = 2, CooldownMs = 250, IntervalMs = 33, Repeat = false };
                    string csv = item.ToCsv();
                    Check("CSV has stable schema independent of locale", csv.Split(',').Length == TriggerEvent.CsvHeader.Split(',').Length && csv.Contains("0.125000,1.250,33.500"));
                    Check("CSV preserves UTC, negative region and color", csv.Contains("Z,would_fire") && csv.Contains(",-120,0,120,120,#FFD54A,"));
                }
                finally { Thread.CurrentThread.CurrentCulture = previous; }
            }
            catch (Exception ex) { failures++; results.Add("FAIL unexpected exception: " + ex); }
            results.Add(""); results.Add((results.Count - failures - 1) + " passed; " + failures + " failed.");
            File.WriteAllLines(reportPath, results.ToArray());
            return failures == 0 ? 0 : 1;
        }
    }
}

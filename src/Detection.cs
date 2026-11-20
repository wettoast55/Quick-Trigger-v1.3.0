using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace PopQuiz
{
    // One run uses one settings snapshot. Editing the form stops that run.
    internal sealed class ScanSettings
    {
        public Rectangle Region;
        public Color TargetColor;
        public int ColorTolerance = 18;
        public int MinimumPixels = 8;
        public int StableFrames = 2;
        public int CooldownMs = 250;
        public int ScanIntervalMs = 33;
        public bool RandomDelay;
        public bool Repeat;
        public bool SendClicks;
        public bool HoldToScan;
        public int HoldKey = ActivationKey.Default;
        public IntPtr TargetWindow;
        public uint TargetProcess;
        public IntPtr MainWindow;
        public string DisplayDeviceName;
        public Rectangle DisplayBounds;

        // Timing accepts every positive 32-bit integer; there are no custom caps.
        public string Validate()
        {
            if (HoldToScan)
            {
                string bindingError = ActivationKey.Validate(HoldKey, SendClicks);
                if (bindingError != null)
                    return bindingError;
            }
            if (CooldownMs < 1 || ScanIntervalMs < 1)
                return "Cooldown and scan interval must be at least 1 ms.";
            if (Region.Width < 1 || Region.Height < 1 || Region.Width > 1024 || Region.Height > 1024)
                return "Choose an area between 1 x 1 and 1024 x 1024 pixels.";
            if (!string.IsNullOrEmpty(DisplayDeviceName) && !DisplayBounds.Contains(Region))
                return "The scan area must fit entirely on the selected display.";
            if (MinimumPixels < 1 || MinimumPixels > (long)Region.Width * Region.Height)
                return "Minimum pixels must fit inside the scan area.";
            if (StableFrames < 1)
                return "Stable frames must be at least 1.";
            if (ColorTolerance < 0 || ColorTolerance > 255)
                return "Color tolerance must be between 0 and 255.";
            return null;
        }
    }

    // Pixel matching is independent of windows, input and timing.
    internal static class PixelDetector
    {
        // All three channels must be inside the inclusive tolerance.
        public static bool Matches(byte red, byte green, byte blue, Color target, int tolerance)
        {
            return Math.Abs(red - target.R) <= tolerance &&
                   Math.Abs(green - target.G) <= tolerance &&
                   Math.Abs(blue - target.B) <= tolerance;
        }

        // Read the bitmap in BGRA order. LockBits avoids a slow GetPixel call per pixel.
        public static unsafe int Count(Bitmap bitmap, Color target, int tolerance)
        {
            if (tolerance < 0 || tolerance > 255)
                throw new ArgumentOutOfRangeException("tolerance");

            Rectangle area = new Rectangle(Point.Empty, bitmap.Size);
            BitmapData pixels = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int matches = 0;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    byte* row = (byte*)pixels.Scan0 + y * pixels.Stride;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        byte* pixel = row + x * 4;
                        if (Matches(pixel[2], pixel[1], pixel[0], target, tolerance))
                            matches++;
                    }
                }
                return matches;
            }
            finally
            {
                bitmap.UnlockBits(pixels);
            }
        }

        // Accept six hexadecimal digits with one optional leading #.
        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            string hex = (text ?? "").Trim();
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            int value;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return false;

            color = Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
            return true;
        }

        // Keep the same hex format in the editor and exported logs.
        public static string ToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }

    // This small state machine filters flicker and enforces time between triggers.
    internal sealed class TriggerGate
    {
        private int matchingFrames;
        private bool firedDuringPresence;
        private double firstMatchMs;
        private double lastFireMs = double.NegativeInfinity;

        // Qualify a match without consuming it; a cancelled delay must not start cooldown.
        // nowMs comes from Stopwatch, so clock corrections cannot break cooldowns.
        public bool Evaluate(bool matched, double nowMs, int stableFrames, int cooldownMs, bool repeat, out double presenceMs)
        {
            presenceMs = 0;
            if (!matched)
            {
                ResetPresence();
                return false;
            }

            if (matchingFrames == 0)
                firstMatchMs = nowMs;
            if (matchingFrames < stableFrames)
                matchingFrames++;

            if (matchingFrames < stableFrames || (firedDuringPresence && !repeat))
                return false;
            if (nowMs - lastFireMs < cooldownMs)
                return false;

            presenceMs = nowMs - firstMatchMs;
            return true;
        }

        // Start cooldown at actual submission, after any requested trigger delay.
        public void RecordTrigger(double nowMs)
        {
            lastFireMs = nowMs;
            firedDuringPresence = true;
        }

        // Losing the color or focus clears the streak but preserves the cooldown.
        public void ResetPresence()
        {
            matchingFrames = 0;
            firedDuringPresence = false;
        }

        // Starting a new session also clears the previous trigger time.
        public void Reset()
        {
            ResetPresence();
            lastFireMs = double.NegativeInfinity;
        }
    }

    // Each event retains its settings so exported runs can be compared later.
    internal sealed class TriggerEvent
    {
        public DateTime TimestampUtc;
        public string Action;
        public int MatchingPixels;
        public double ScanMs;
        public double PresenceMs;
        public int RequestedDelayMs;
        public double ActualDelayMs;
        public ScanSettings Settings;

        public const string CsvHeader = "timestamp_utc,action,matching_pixels,matching_fraction,scan_ms,eligible_presence_ms,left,top,width,height,color_hex,tolerance,min_pixels,stable_frames,cooldown_ms,requested_interval_ms,repeat,hold_to_scan,hold_key_code,random_delay,requested_delay_ms,actual_delay_ms";

        // Invariant formatting prevents decimal commas from breaking the CSV columns.
        public string ToCsv()
        {
            Rectangle area = Settings.Region;
            double fraction = MatchingPixels / (double)(area.Width * area.Height);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:o},{1},{2},{3:F6},{4:F3},{5:F3},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21:F3}",
                TimestampUtc, Action, MatchingPixels, fraction, ScanMs, PresenceMs,
                area.X, area.Y, area.Width, area.Height, PixelDetector.ToHex(Settings.TargetColor),
                Settings.ColorTolerance, Settings.MinimumPixels, Settings.StableFrames,
                Settings.CooldownMs, Settings.ScanIntervalMs, Settings.Repeat ? "true" : "false",
                Settings.HoldToScan ? "true" : "false", Settings.HoldKey,
                Settings.RandomDelay ? "true" : "false", RequestedDelayMs, ActualDelayMs);
        }
    }
}

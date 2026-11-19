using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace ColorTriggerLab
{
    // A deliberately simple model: every RGB channel must be within tolerance.
    // Counts cover the whole rectangle, not just its center or a connected shape.
    internal static class PixelDetector
    {
        public static bool Matches(byte r, byte g, byte b, Color target, int tolerance)
        {
            return Math.Abs(r - target.R) <= tolerance &&
                   Math.Abs(g - target.G) <= tolerance &&
                   Math.Abs(b - target.B) <= tolerance;
        }

        public static unsafe int Count(Bitmap frame, Color target, int tolerance)
        {
            if (tolerance < 0 || tolerance > 255) throw new ArgumentOutOfRangeException("tolerance");
            Rectangle bounds = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData data = frame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int count = 0;
                for (int y = 0; y < frame.Height; y++)
                {
                    byte* row = (byte*)data.Scan0 + y * data.Stride;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        byte* p = row + x * 4;
                        if (Matches(p[2], p[1], p[0], target, tolerance)) count++;
                    }
                }
                return count;
            }
            finally { frame.UnlockBits(data); }
        }

        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            string hex = (text ?? "").Trim().TrimStart('#');
            int value;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
            color = Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
            return true;
        }
    }

    // Separate temporal filtering from capture and input so timing is testable.
    // The caller supplies monotonic elapsed milliseconds, never wall-clock time.
    internal sealed class TriggerGate
    {
        private int consecutive;
        private bool firedForPresence;
        private double firstMatch;
        private double lastFire = double.NegativeInfinity;

        public bool Evaluate(bool match, double now, int requiredFrames, int cooldownMs, bool repeat, out double latencyMs)
        {
            latencyMs = 0;
            if (!match) { ResetPresence(); return false; }
            if (consecutive == 0) firstMatch = now;
            consecutive = Math.Min(consecutive + 1, requiredFrames);
            if (consecutive < requiredFrames || (firedForPresence && !repeat) || now - lastFire < cooldownMs) return false;
            latencyMs = now - firstMatch;
            firedForPresence = true;
            lastFire = now;
            return true;
        }

        public void ResetPresence() { consecutive = 0; firedForPresence = false; }
        public void Reset() { ResetPresence(); lastFire = double.NegativeInfinity; }
    }

    internal sealed class TriggerEvent
    {
        public DateTime Timestamp;
        public string Action;
        public int Matches;
        public double Fraction;
        public double ScanMs;
        public double PresenceMs;
        public Rectangle Region;
        public Color Color;
        public int Tolerance;
        public int MinimumPixels;
        public int StableFrames;
        public int CooldownMs;
        public int IntervalMs;
        public bool Repeat;

        public const string CsvHeader = "timestamp_utc,action,matching_pixels,matching_fraction,scan_ms,eligible_presence_ms,left,top,width,height,color_hex,tolerance,min_pixels,stable_frames,cooldown_ms,requested_interval_ms,repeat";

        public string ToCsv()
        {
            return string.Join(",", new string[] {
                Timestamp.ToString("o", CultureInfo.InvariantCulture), Action, Matches.ToString(CultureInfo.InvariantCulture),
                Fraction.ToString("F6", CultureInfo.InvariantCulture), ScanMs.ToString("F3", CultureInfo.InvariantCulture),
                PresenceMs.ToString("F3", CultureInfo.InvariantCulture), Region.X.ToString(CultureInfo.InvariantCulture),
                Region.Y.ToString(CultureInfo.InvariantCulture), Region.Width.ToString(CultureInfo.InvariantCulture),
                Region.Height.ToString(CultureInfo.InvariantCulture), "#" + Color.R.ToString("X2") + Color.G.ToString("X2") + Color.B.ToString("X2"),
                Tolerance.ToString(CultureInfo.InvariantCulture), MinimumPixels.ToString(CultureInfo.InvariantCulture),
                StableFrames.ToString(CultureInfo.InvariantCulture), CooldownMs.ToString(CultureInfo.InvariantCulture),
                IntervalMs.ToString(CultureInfo.InvariantCulture), Repeat ? "true" : "false"
            });
        }
    }
}

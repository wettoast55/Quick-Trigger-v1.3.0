using System;
using System.Windows.Forms;

namespace QuickTrigger
{
    // A binding is one Windows key code, including the five standard mouse buttons.
    internal static class ActivationKey
    {
        public const int Default = (int)Keys.XButton1;

        // Keep application shortcuts and the binding dialog's cancel key available.
        public static string Validate(int key, bool sendClicks)
        {
            if (key < 1 || key > 254 || key == (int)Keys.Cancel || !Enum.IsDefined(typeof(Keys), key))
                return "Choose a keyboard key or a standard mouse button.";
            if (key == (int)Keys.Escape || (key >= (int)Keys.F6 && key <= (int)Keys.F9))
                return "Esc and F6-F9 are reserved. Choose another key or mouse button.";
            if (sendClicks && key == (int)Keys.LButton)
                return "Click mode needs a hold button other than the left mouse button.";
            return null;
        }

        // Prefer left/right modifier identities when listening for a new binding.
        public static bool CanListenFor(int key)
        {
            return key != (int)Keys.ShiftKey && key != (int)Keys.ControlKey && key != (int)Keys.Menu &&
                key >= 1 && key <= 254 && Enum.IsDefined(typeof(Keys), key);
        }

        // Show familiar names instead of Windows' mouse and modifier enum names.
        public static string Name(int key)
        {
            switch ((Keys)key)
            {
                case Keys.LButton: return "Left mouse";
                case Keys.RButton: return "Right mouse";
                case Keys.MButton: return "Middle mouse";
                case Keys.XButton1: return "Mouse 4 (Back)";
                case Keys.XButton2: return "Mouse 5 (Forward)";
                case Keys.LShiftKey: return "Left Shift";
                case Keys.RShiftKey: return "Right Shift";
                case Keys.LControlKey: return "Left Ctrl";
                case Keys.RControlKey: return "Right Ctrl";
                case Keys.LMenu: return "Left Alt";
                case Keys.RMenu: return "Right Alt";
                case Keys.LWin: return "Left Windows";
                case Keys.RWin: return "Right Windows";
                default: return new KeysConverter().ConvertToString((Keys)key);
            }
        }
    }

    // Coordinate hold state, scan deadlines and trigger history on the scanner thread.
    internal sealed class ScanControl
    {
        public const int HoldPollMs = 5;
        private readonly ScanSettings settings;
        private readonly Func<int, bool> keyDown;
        private readonly TriggerGate triggerGate = new TriggerGate();
        private double nextScanMs = double.NegativeInfinity;
        public bool WaitingForHold { get; private set; }

        // The input reader is replaceable so tests can simulate releases without sending keys.
        public ScanControl(ScanSettings settings, Func<int, bool> keyDown)
        {
            this.settings = settings;
            this.keyDown = keyDown;
        }

        // A release discards partial matches and the old scan deadline, but keeps cooldown.
        public bool IsActive()
        {
            WaitingForHold = settings.HoldToScan && !keyDown(settings.HoldKey);
            if (WaitingForHold)
            {
                triggerGate.ResetPresence();
                nextScanMs = double.NegativeInfinity;
            }
            return !WaitingForHold;
        }

        // A new hold can scan immediately even if the previous interval was very long.
        public bool IsScanDue(double nowMs)
        {
            return IsActive() && nowMs >= nextScanMs;
        }

        // The interval is measured from scan start, without accumulating missed deadlines.
        public void ScheduleNextScan(double scanStartedMs)
        {
            nextScanMs = scanStartedMs + settings.ScanIntervalMs;
        }

        // While armed, check releases between scan deadlines without capturing extra frames.
        public double WaitMilliseconds(double nowMs)
        {
            if (WaitingForHold)
                return HoldPollMs;
            double remaining = Math.Max(0, nextScanMs - nowMs);
            return settings.HoldToScan ? Math.Min(HoldPollMs, remaining) : remaining;
        }

        // Check hold state again after capture, before a match can qualify as a trigger.
        public bool TryTrigger(bool matched, double nowMs, out double presenceMs)
        {
            presenceMs = 0;
            return IsActive() && triggerGate.Evaluate(matched, nowMs, settings.StableFrames,
                settings.CooldownMs, settings.Repeat, out presenceMs);
        }

        // Focus changes and blocked clicks also require a fresh matching streak.
        public void ResetPresence()
        {
            triggerGate.ResetPresence();
        }
    }
}

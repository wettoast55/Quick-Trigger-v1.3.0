using System;

namespace PopQuiz
{
    // One generator per scan session supplies a fresh, uniformly chosen whole-millisecond delay.
    internal sealed class TriggerDelay
    {
        public const int MinimumMs = 1;
        public const int MaximumMs = 50;
        private readonly Random random;

        // Tests can supply a seeded or scripted generator without changing production timing.
        public TriggerDelay(Random random)
        {
            if (random == null)
                throw new ArgumentNullException("random");
            this.random = random;
        }

        // Random.Next excludes its upper bound, so 51 includes the requested 50 ms endpoint.
        public int NextMilliseconds(bool enabled)
        {
            return enabled ? random.Next(MinimumMs, MaximumMs + 1) : 0;
        }

        // Wait on the scanner thread without holding the input lock. A monotonic deadline
        // prevents short timer wakeups from firing early; brief slices keep hold/F7 polling active.
        // The timer's stop handle can interrupt a slice immediately. No screen capture occurs here.
        public static bool Wait(int milliseconds, Func<double> readClock,
            Func<double, bool> wait, Func<bool> canContinue, out double elapsedMs)
        {
            elapsedMs = 0;
            if (milliseconds == 0)
                return canContinue();
            double started = readClock();
            while (canContinue())
            {
                elapsedMs = readClock() - started;
                double remaining = milliseconds - elapsedMs;
                if (remaining <= 0)
                    return true;
                if (!wait(Math.Min(ScanControl.HoldPollMs, remaining)))
                    break;
            }
            elapsedMs = readClock() - started;
            return false;
        }
    }
}

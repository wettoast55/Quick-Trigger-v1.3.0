using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HexBot
{
    // Short waits use a high-resolution Windows timer rather than the form's UI timer.
    internal sealed class ScanTimer : IDisposable
    {
        private readonly TimerHandle timer;
        public bool IsHighResolution { get; private set; }

        // Older systems fall back to a normal waitable timer, without spinning the CPU.
        public ScanTimer()
        {
            const uint highResolution = 0x00000002;
            const uint modifyAndSynchronize = 0x00100002;
            IntPtr handle = WindowsApi.CreateWaitableTimerEx(IntPtr.Zero, null, highResolution, modifyAndSynchronize);
            IsHighResolution = handle != IntPtr.Zero;
            if (handle == IntPtr.Zero)
                handle = WindowsApi.CreateWaitableTimerEx(IntPtr.Zero, null, 0, modifyAndSynchronize);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the scan timer.");
            timer = new TimerHandle(handle);
        }

        // Return false on cancellation. Stop always interrupts even a very long interval.
        public bool Wait(double milliseconds, WaitHandle stop)
        {
            if (milliseconds <= 0)
                return !stop.WaitOne(0);

            // Negative due times are relative delays measured in 100-nanosecond units.
            long dueTime = -Math.Max(1, (long)Math.Ceiling(milliseconds * 10000.0));
            bool scheduled = WindowsApi.SetWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle(), ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false);
            if (!scheduled)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not schedule the next scan.");
            return WaitHandle.WaitAny(new WaitHandle[] { stop, timer }) == 1;
        }

        // SafeWaitHandle closes the native timer when its wrapper is disposed.
        public void Dispose()
        {
            timer.Dispose();
        }

        // Wrapping the timer as a WaitHandle lets it share a wait with the stop signal.
        private sealed class TimerHandle : WaitHandle
        {
            public TimerHandle(IntPtr handle)
            {
                SafeWaitHandle = new SafeWaitHandle(handle, true);
            }
        }
    }
}

﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unosquare.FFplaySharp;

namespace FFplaySharp.Ava
{
    internal class ThreadedTimer
    {
        private readonly Thread Worker;
        private readonly CancellationTokenSource Cts = new();

        private bool IsDisposed;
        public event EventHandler? Elapsed;

        public ThreadedTimer(int intervalMillis = 1, int resolution = 1)
        {
            Worker = new Thread(WorkerLoop) { IsBackground = true };
            Interval = TimeSpan.FromMilliseconds(intervalMillis);
            var (Minimum, Maximum) = NativeMethods.GetTimerPeriod();
            Resolution = resolution.Clamp(Minimum.Clamp(1, Minimum), Maximum.Clamp(1, Maximum));
        }

        public bool IsRunning { get; private set; }

        public TimeSpan Interval { get; }

        public int Resolution { get; }

        public void Start()
        {
            if (IsRunning)
                return;

            IsRunning = true;
            Worker.Start();
        }

        private void WorkerLoop()
        {
            const double Bias = 0.0005;
            var token = Cts.Token;
            var cycleClock = new MultimediaStopwatch();
            var resolutionMillis = (uint)Resolution;

            var st = Stopwatch.StartNew();
            try
            {
                cycleClock.Restart();
                // _ = NativeMethods.BeginTimerResolution(resolutionMillis);
                while (!token.IsCancellationRequested)
                {
                    Elapsed?.Invoke(this, EventArgs.Empty);
                    while (!token.IsCancellationRequested)
                    {
                        if (cycleClock.ElapsedSeconds >= Interval.TotalSeconds - Bias)
                            break;
                        st.Restart();
                        Thread.Sleep(Resolution);
                        // TODO  ffmpeg.av_usleep(999 + 1);
                        st.Stop();
                        Console.WriteLine($"{Resolution}:{DateTimeOffset.UtcNow.Millisecond}------>{st.ElapsedMilliseconds}");
                    }

                    cycleClock.Restart();
                }
            }
            finally
            {
                // _ = NativeMethods.EndTimerResolution(resolutionMillis);
                IsRunning = false;
            }
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Cts.Cancel();

            if (IsRunning && Environment.CurrentManagedThreadId != Worker.ManagedThreadId)
                Worker.Join();

            Cts.Dispose();
        }

        private static class NativeMethods
        {
            private const string MultimediaDll = "Winmm.dll";
            private static readonly int TimerCapsSize = Marshal.SizeOf(typeof(TimerCaps));

            /// <summary>
            /// Represents information about the multimedia Timer's capabilities.
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            private struct TimerCaps
            {
                /// <summary>
                /// Minimum supported period in milliseconds.
                /// </summary>
                public int periodMin;

                /// <summary>
                /// Maximum supported period in milliseconds.
                /// </summary>
                public int periodMax;
            }

            [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeGetDevCaps")]
            private static extern int TimeGetDevCaps(ref TimerCaps caps, int sizeOfTimerCaps);

            [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeBeginPeriod")]
            public static extern uint BeginTimerResolution(uint value);

            [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeEndPeriod")]
            public static extern uint EndTimerResolution(uint value);

            public static int Clamp(int number, int min, int max) => number < min ? min : number > max ? max : number;

            public static (int Minimum, int Maximum) GetTimerPeriod()
            {
                TimerCaps caps = default;
                _ = TimeGetDevCaps(ref caps, TimerCapsSize);
                return (caps.periodMin, caps.periodMax);
            }
        }
    }
}
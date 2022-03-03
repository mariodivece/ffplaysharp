using System.Runtime.InteropServices;
using Unosquare.FFplaySharp.Wpf.Audio;
using static Unosquare.FFplaySharp.Wpf.Audio.WaveInterop;

namespace Unosquare.FFplaySharp.Wpf
{
    public class WavePlayer : IWaveProvider
    {
        private const int DesiredLatency = 100;
        private readonly WaveOutBuffer[] Buffers = new WaveOutBuffer[2];
        private readonly int BufferSize;
        private readonly object SyncLock = new();
        private readonly AutoResetEvent OnSamplesPlayed = new(true);
        private readonly Thread PlaybackThread;
        private readonly WaveCallback WaveMessageCallback;
        private IntPtr DeviceHandle = IntPtr.Zero;
        private readonly CancellationTokenSource Cts = new();

        public WavePlayer()
        {
            BufferSize = WaveFormat.ConvertLatencyToByteSize(DesiredLatency);
            PlaybackThread = new(PerformContinuousPlayback) { IsBackground = true };
            WaveMessageCallback = OnDeviceMessage;
        }

        public WaveFormat WaveFormat { get; } = new();

        public void Close()
        {
            Cts.Cancel();
        }

        private void PerformContinuousPlayback(object? state)
        {
            var deviceNumber = -1;
            var openResult = waveOutOpen(
                out DeviceHandle,
                (IntPtr)deviceNumber,
                WaveFormat,
                WaveMessageCallback,
                IntPtr.Zero,
                WaveInOutOpenFlags.CallbackFunction);

            if (openResult != MmResult.NoError)
                throw new MmException(openResult, nameof(PerformContinuousPlayback));

            // Create the buffers
            for (var n = 0; n < Buffers.Length; n++)
                Buffers[n] = new WaveOutBuffer(DeviceHandle, BufferSize, this, SyncLock);

            var queued = 0;

            try
            {
                while (!Cts.IsCancellationRequested)
                {
                    if (!OnSamplesPlayed.WaitOne(DesiredLatency))
                        continue;

                    foreach (var buffer in Buffers)
                    {
                        if (buffer.InQueue || buffer.OnDone())
                            queued++;
                    }

                    // Detect an end of playback
                    if (queued <= 0)
                        break;
                }
            }
            finally
            {
                if (DeviceHandle != IntPtr.Zero)
                    waveOutClose(DeviceHandle);

                foreach (var buffer in Buffers)
                {
                    if (buffer is not null)
                        buffer.Dispose();
                }

                Cts.Dispose();
            }
        }

        public unsafe void Start()
        {
            PlaybackThread.Start();
        }

        private void OnDeviceMessage(IntPtr deviceHandle, WaveMessage message, IntPtr instance, WaveHeader header, IntPtr reserved)
        {
            if (message is WaveMessage.WaveOutDone)
                OnSamplesPlayed.Set();
        }

        private ulong CurrentSampleNumber;

        private double GetNextSineValue(double amplitude = short.MaxValue / 2d, double frequency = 1000d)
        {
            const double TwoPi = 2.0 * Math.PI;
            var secondsPerSample = 1d / WaveFormat.SampleRate;
            var t = CurrentSampleNumber * secondsPerSample;
            var result = amplitude * Math.Sin(TwoPi * frequency * t);

            CurrentSampleNumber++;

            return result;
        }

        int IWaveProvider.Read(byte[] buffer, int offset, int count)
        {
            var sampleByteSize = WaveFormat.BitsPerSample / 8;
            var writeBlockSize = WaveFormat.Channels * sampleByteSize;
            var writeCount = 0;

            Span<byte> sampleBytes = stackalloc byte[writeBlockSize];

            while (writeCount + writeBlockSize <= count)
            {
                var sampleValue = Convert.ToInt16(GetNextSineValue().Clamp(short.MinValue, short.MaxValue));
                sampleBytes[0] = (byte)(sampleValue & 0x00FF);
                sampleBytes[1] = (byte)(sampleValue >> 8);
                sampleBytes[2] = sampleBytes[0];
                sampleBytes[3] = sampleBytes[1];

                var bufferSpan = buffer.AsSpan(offset + writeCount, sampleBytes.Length);
                sampleBytes.CopyTo(bufferSpan);
                writeCount += writeBlockSize;
            }

            return writeCount;
        }
    }
}

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
        private IntPtr DeviceHandle = IntPtr.Zero;


        public WavePlayer()
        {
            BufferSize = WaveFormat.ConvertLatencyToByteSize(DesiredLatency);
        }

        public WaveFormat WaveFormat { get; } = new();


        public unsafe void Start()
        {
            var thread = new Thread(() =>
            {
                var deviceNumber = -1;
                var openResult = waveOutOpen(
                    out DeviceHandle,
                    (IntPtr)deviceNumber,
                    WaveFormat,
                    OnDeviceMessage,
                    IntPtr.Zero,
                    WaveInOutOpenFlags.CallbackFunction);

                // Create the buffers
                for (var n = 0; n < Buffers.Length; n++)
                    Buffers[n] = new WaveOutBuffer(DeviceHandle, BufferSize, this, SyncLock);

                var queued = 0;

                while (true)
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

            })
            {
                IsBackground = true,
                Name = nameof(WavePlayer),
            };

            thread.Start();
        }

        private void OnDeviceMessage(IntPtr deviceHandle, WaveMessage message, IntPtr instance, WaveHeader header, IntPtr reserved)
        {
            if (message is WaveMessage.WaveOutDone)
                OnSamplesPlayed.Set();
        }


        private ulong CurrentSampleNumber;
        private int ZeroCrossings = 0;

        private double GetNextSineValue(double amplitude = short.MaxValue / 2d, double frequency = 1000d)
        {
            const double TwoPi = 2.0 * Math.PI;
            var secondsPerSample = 1d / WaveFormat.SampleRate;
            var t = CurrentSampleNumber * secondsPerSample;
            var result = amplitude * Math.Sin(TwoPi * frequency * t);
            CurrentSampleNumber++;

            if (result >= -double.Epsilon && result <= double.Epsilon)
                ZeroCrossings++;

            return result;
        }

        public unsafe int Read(byte[] buffer, int offset, int count)
        {
            var sampleByteSize = WaveFormat.BitsPerSample / 8;
            var writeBlockSize = WaveFormat.Channels * sampleByteSize;
            var writeCount = 0;
            var sampleBytes = stackalloc byte[writeBlockSize];
            fixed (byte* bufferAddress = &buffer[0])
            {
                while (writeCount + writeBlockSize <= count)
                {
                    var sampleValue = Convert.ToInt16(GetNextSineValue().Clamp(short.MinValue, short.MaxValue));
                    sampleBytes[0] = (byte)(sampleValue & 0x00FF);
                    sampleBytes[1] = (byte)(sampleValue >> 8);
                    sampleBytes[2] = sampleBytes[0];
                    sampleBytes[3] = sampleBytes[1];

                    Buffer.MemoryCopy(sampleBytes, bufferAddress + offset + writeCount, writeBlockSize, writeBlockSize);
                    writeCount += writeBlockSize;
                }
            }

            return writeCount;
        }
    }
}

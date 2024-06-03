using FFmpeg.AutoGen.Abstractions;
using Unosquare.FFplaySharp.Interop;
using Unosquare.FFplaySharp.Primitives;
using Unosquare.FFplaySharp.WinWave.Audio;
using static Unosquare.FFplaySharp.WinWave.Audio.WaveInterop;

namespace Unosquare.FFplaySharp.WinWave;

public class WavePlayer : IWaveProvider
{
    private const int DesiredLatency = 100;
    private readonly WaveOutBuffer[] Buffers = new WaveOutBuffer[2];

    private readonly object SyncLock = new();
    private readonly AutoResetEvent OnSamplesPlayed = new(true);
    private readonly Thread PlaybackThread;
    private readonly WaveCallback WaveMessageCallback;
    private IntPtr DeviceHandle = IntPtr.Zero;
    private readonly CancellationTokenSource Cts = new();

    private readonly IPresenter Presenter;

    public WavePlayer(IPresenter presenter)
    {
        BufferSize = WaveFormat.ConvertLatencyToByteSize(DesiredLatency);
        PlaybackThread = new(PerformContinuousPlayback) { IsBackground = true };
        WaveMessageCallback = OnDeviceMessage;
        Presenter = presenter;
        AudioParams = new()
        {
            BufferSize = this.BufferSize,
            Channels = WaveFormat.Channels,
            SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16,
            SampleRate = WaveFormat.SampleRate,
            ChannelLayout = AudioParams.DefaultChannelLayoutFor(WaveFormat.Channels),
        };
    }

    private MediaContainer Container => Presenter.Container;

    public int BufferSize { get; }

    public WaveFormat WaveFormat { get; } = new();

    public AudioParams AudioParams { get; }

    public bool HasStarted { get; private set; }

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
                buffer?.Dispose();

            Cts.Dispose();
        }
    }

    public void Start()
    {
        HasStarted = true;
        PlaybackThread.Start();
    }

    public void Pause()
    {
        // TODO: Implement pause
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

    private int WriteSineWave(byte[] buffer, int offset, int count)
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

    private int ReadBufferIndex, ReadBufferSize;
    private BufferReference ReadBuffer;

    unsafe int IWaveProvider.Read(byte[] buffer, int offset, int count)
    {
        if (Container is null || !Container.HasAudio)
            return WriteSineWave(buffer, offset, count);

        using var bufferHandle = buffer.AsMemory(offset).Pin();
        var outputStreamPointer = (byte*)bufferHandle.Pointer;

        // prepare a new audio buffer
        Presenter.LastAudioCallbackTime = Clock.SystemTime;
        var pendingByteCount = count;
        while (pendingByteCount > 0)
        {
            if (ReadBufferIndex >= ReadBufferSize)
            {
                ReadBuffer = Container.Audio.RefillOutputBuffer();
                if (ReadBuffer.Length < 0)
                {
                    // if error, just output silence.
                    ReadBuffer = BufferReference.NullBuffer;
                    ReadBufferSize = Convert.ToInt32(Container.Audio.HardwareSpec.FrameSize *
                        (double)count / Container.Audio.HardwareSpec.FrameSize);
                }
                else
                {
                    ReadBufferSize = Convert.ToInt32(ReadBuffer.Length);
                }

                ReadBufferIndex = 0;
            }

            var readByteCount = ReadBufferSize - ReadBufferIndex;
            if (readByteCount > pendingByteCount)
                readByteCount = pendingByteCount;

            var inputStreamPointer = ReadBuffer.Reference + ReadBufferIndex;

            if (!Container.IsMuted && ReadBuffer.IsValid())
            {
                Buffer.MemoryCopy(inputStreamPointer, outputStreamPointer, readByteCount, readByteCount);
            }
            else
            {
                // Clear the output stream.
                for (var i = 0; i < readByteCount; i++)
                    outputStreamPointer[i] = 0;
            }

            pendingByteCount -= readByteCount;
            outputStreamPointer += readByteCount;
            ReadBufferIndex += readByteCount;
        }

        // Let's assume the audio driver that is used by SDL has two periods.
        if (!Container.Audio.FrameTime.IsNaN)
        {
            var readBufferAvailable = ReadBufferSize - ReadBufferIndex;
            var bufferDuration = (2d * Container.Audio.HardwareSpec.BufferSize + readBufferAvailable) / Container.Audio.HardwareSpec.BytesPerSecond;
            Container.AudioClock.Set(Container.Audio.FrameTime - bufferDuration, Container.Audio.GroupIndex, Presenter.LastAudioCallbackTime);
            Container.ExternalClock.SyncToSlave(Container.AudioClock);
        }

        return count;
    }
}

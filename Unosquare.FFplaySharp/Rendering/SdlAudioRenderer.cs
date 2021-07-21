namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class SdlAudioRenderer : IAudioRenderer
    {
        private readonly SDL.SDL_AudioCallback AudioCallback;
        private uint AudioDeviceId;
        private int ReadBufferSize;
        private int ReadBufferIndex;
        private BufferReference ReadBuffer;

        public double AudioCallbackTime { get; private set; }

        public MediaContainer Container => Presenter.Container;

        public IPresenter Presenter { get; private set; }

        public SdlAudioRenderer()
        {
            AudioCallback = new(OnAudioDeviceCallback);
        }

        public void Initialize(IPresenter presenter)
        {
            Presenter = presenter;

            var parent = Presenter as SdlPresenter;
            var o = Presenter.Container.Options;
            if (o.IsAudioDisabled)
            {
                parent.SdlInitFlags &= ~SDL.SDL_INIT_AUDIO;
            }
            else
            {
                const string AlsaBufferSizeName = "SDL_AUDIO_ALSA_SET_BUFFER_SIZE";
                // Try to work around an occasional ALSA buffer underflow issue when the
                // period size is NPOT due to ALSA resampling by forcing the buffer size.
                if (Environment.GetEnvironmentVariable(AlsaBufferSizeName) == null)
                    Environment.SetEnvironmentVariable(AlsaBufferSizeName, "1", EnvironmentVariableTarget.Process);
            }
        }

        public int Volume { get; set; }


        public AudioParams Open(AudioParams wantedSpec) =>
            Open(wantedSpec.ChannelLayout, wantedSpec.Channels, wantedSpec.SampleRate);

        private AudioParams Open(long wantedChannelLayout, int wantedChannelCount, int wantedSampleRate)
        {
            Volume = Container.Options.StartupVolume;

            var audioDeviceSpec = new AudioParams();
            var probeChannelCount = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var probeSampleRates = new[] { 0, 44100, 48000, 96000, 192000 };
            var probeSampleRateIndex = probeSampleRates.Length - 1;

            const string ChannelCountEnvVariable = "SDL_AUDIO_CHANNELS";
            var env = Environment.GetEnvironmentVariable(ChannelCountEnvVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                wantedChannelCount = int.Parse(env);
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedChannelCount);
            }

            if (wantedChannelLayout == 0 || wantedChannelCount != AudioParams.ChannelCountFor(wantedChannelLayout))
            {
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedChannelCount);
                wantedChannelLayout &= ~ffmpeg.AV_CH_LAYOUT_STEREO_DOWNMIX;
            }

            wantedChannelCount = AudioParams.ChannelCountFor(wantedChannelLayout);

            var wantedSpec = new SDL.SDL_AudioSpec
            {
                channels = (byte)wantedChannelCount,
                freq = wantedSampleRate
            };

            if (wantedSpec.freq <= 0 || wantedSpec.channels <= 0)
            {
                ("Invalid sample rate or channel count!").LogError();
                return audioDeviceSpec;
            }

            while (probeSampleRateIndex != 0 && probeSampleRates[probeSampleRateIndex] >= wantedSpec.freq)
                probeSampleRateIndex--;

            wantedSpec.format = SDL.AUDIO_S16SYS;
            wantedSpec.silence = 0;
            wantedSpec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wantedSpec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wantedSpec.callback = AudioCallback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);

            const int AudioDeviceFlags = (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE);
            SDL.SDL_AudioSpec deviceSpec;
            while ((AudioDeviceId = SDL.SDL_OpenAudioDevice(null, 0, ref wantedSpec, out deviceSpec, AudioDeviceFlags)) == 0)
            {
                ($"SDL_OpenAudio ({wantedSpec.channels} channels, {wantedSpec.freq} Hz): {SDL.SDL_GetError()}.").LogWarning();
                wantedSpec.channels = (byte)probeChannelCount[Math.Min(7, (int)wantedSpec.channels)];
                if (wantedSpec.channels == 0)
                {
                    wantedSpec.freq = probeSampleRates[probeSampleRateIndex--];
                    wantedSpec.channels = (byte)wantedChannelCount;
                    if (wantedSpec.freq == 0)
                    {
                        ("No more combinations to try, audio open failed").LogError();
                        return audioDeviceSpec;
                    }
                }

                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedSpec.channels);
            }

            if (deviceSpec.format != SDL.AUDIO_S16SYS)
            {
                ($"SDL advised audio format {deviceSpec.format} is not supported!").LogError();
                return audioDeviceSpec;
            }

            if (deviceSpec.channels != wantedSpec.channels)
            {
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(deviceSpec.channels);
                if (wantedChannelLayout == 0)
                {
                    ($"SDL advised channel count {deviceSpec.channels} is not supported!").LogError();
                    return audioDeviceSpec;
                }
            }

            audioDeviceSpec.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audioDeviceSpec.SampleRate = deviceSpec.freq;
            audioDeviceSpec.ChannelLayout = wantedChannelLayout;
            audioDeviceSpec.Channels = deviceSpec.channels;

            if (audioDeviceSpec.BytesPerSecond <= 0 || audioDeviceSpec.FrameSize <= 0)
            {
                ("av_samples_get_buffer_size failed").LogError();
                return audioDeviceSpec;
            }

            ReadBufferIndex = 0;
            ReadBufferSize = 0;

            audioDeviceSpec.BufferSize = (int)deviceSpec.size;
            return audioDeviceSpec;
        }

        public void Close() =>
            SDL.SDL_CloseAudioDevice(AudioDeviceId);

        public void Pause() =>
            SDL.SDL_PauseAudioDevice(AudioDeviceId, 0);

        /// <summary>
        /// Port of sdl_audio_callback
        /// </summary>
        /// <param name="opaque"></param>
        /// <param name="audioStream"></param>
        /// <param name="pendingByteCount"></param>
        private void OnAudioDeviceCallback(IntPtr opaque, IntPtr audioStream, int pendingByteCount)
        {
            // prepare a new audio buffer
            AudioCallbackTime = Clock.SystemTime;

            while (pendingByteCount > 0)
            {
                if (ReadBufferIndex >= ReadBufferSize)
                {
                    ReadBuffer = Container.Audio.RefillOutputBuffer();
                    if (ReadBuffer.Length < 0)
                    {
                        // if error, just output silence.
                        ReadBuffer.Update(null);
                        ReadBufferSize = Convert.ToInt32(Container.Audio.HardwareSpec.FrameSize *
                            (double)Constants.SDL_AUDIO_MIN_BUFFER_SIZE / Container.Audio.HardwareSpec.FrameSize);
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

                var outputStreamPointer = (byte*)audioStream.ToPointer();
                var inputStreamPointer = ReadBuffer.Pointer + ReadBufferIndex;

                if (!Container.IsMuted && ReadBuffer != null && Volume == SDL.SDL_MIX_MAXVOLUME)
                {
                    Buffer.MemoryCopy(inputStreamPointer, outputStreamPointer, readByteCount, readByteCount);
                }
                else
                {
                    Parallel.For(0, readByteCount, (i) => {
                        outputStreamPointer[i] = 0;
                    });

                    if (!Container.IsMuted && ReadBuffer != null)
                        SDL.SDL_MixAudioFormat(
                            outputStreamPointer,
                            inputStreamPointer,
                            SDL.AUDIO_S16SYS,
                            (uint)readByteCount,
                            Volume);
                }

                pendingByteCount -= readByteCount;
                audioStream += readByteCount;
                ReadBufferIndex += readByteCount;
            }

            // Let's assume the audio driver that is used by SDL has two periods.
            if (!Container.Audio.FrameTime.IsNaN())
            {
                var readBufferAvailable = ReadBufferSize - ReadBufferIndex;
                var bufferDuration = (2d * Container.Audio.HardwareSpec.BufferSize + readBufferAvailable) / Container.Audio.HardwareSpec.BytesPerSecond;
                Container.AudioClock.Set(Container.Audio.FrameTime - bufferDuration, Container.Audio.GroupIndex, AudioCallbackTime);
                Container.ExternalClock.SyncToSlave(Container.AudioClock);
            }
        }

        public void UpdateVolume(int sign, double step)
        {
            var volumeLevel = Volume > 0 ? (20 * Math.Log(Volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volumeLevel + sign * step) / 20.0), 0);
            Volume = (Volume == new_volume ? (Volume + sign) : new_volume).Clamp(0, SDL.SDL_MIX_MAXVOLUME);
        }
    }
}

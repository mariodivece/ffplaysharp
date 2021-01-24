namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class SdlAudioRenderer : IAudioRenderer
    {
        private readonly SDL.SDL_AudioCallback AudioCallback;
        private uint AudioDeviceId;
        private int ReadBufferSize; /* in bytes */
        private int ReadBufferIndex; /* in bytes */

        public double AudioCallbackTime { get; private set; }

        public MediaContainer Container => Presenter.Container;

        public IPresenter Presenter { get; private set; }

        public SdlAudioRenderer()
        {
            AudioCallback = new(sdl_audio_callback);
        }

        public void Initialize(IPresenter presenter)
        {
            Presenter = presenter;

            var parent = Presenter as SdlPresenter;
            var o = Presenter.Container.Options;
            if (o.audio_disable)
            {
                parent.SdlInitFlags &= ~SDL.SDL_INIT_AUDIO;
            }
            else
            {
                const string AlsaBufferSizeName = "SDL_AUDIO_ALSA_SET_BUFFER_SIZE";
                /* Try to work around an occasional ALSA buffer underflow issue when the
                 * period size is NPOT due to ALSA resampling by forcing the buffer size. */
                if (Environment.GetEnvironmentVariable(AlsaBufferSizeName) == null)
                    Environment.SetEnvironmentVariable(AlsaBufferSizeName, "1", EnvironmentVariableTarget.Process);
            }
        }

        public int audio_volume { get; set; }


        public int audio_open(AudioParams wantedSpec, out AudioParams audioDeviceSpec) =>
            audio_open(wantedSpec.Layout, wantedSpec.Channels, wantedSpec.Frequency, out audioDeviceSpec);

        public int audio_open(long wantedChannelLayout, int wantedChannelCount, int wantedSampleRate, out AudioParams audioDeviceSpec)
        {
            audioDeviceSpec = new AudioParams();
            var next_nb_channels = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var next_sample_rates = new[] { 0, 44100, 48000, 96000, 192000 };
            int next_sample_rate_idx = next_sample_rates.Length - 1;

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
                Helpers.LogError("Invalid sample rate or channel count!\n");
                return -1;
            }

            while (next_sample_rate_idx != 0 && next_sample_rates[next_sample_rate_idx] >= wantedSpec.freq)
                next_sample_rate_idx--;

            wantedSpec.format = SDL.AUDIO_S16SYS;
            wantedSpec.silence = 0;
            wantedSpec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wantedSpec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wantedSpec.callback = AudioCallback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);

            const int AudioDeviceFlags = (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE);
            SDL.SDL_AudioSpec deviceSpec;
            while ((AudioDeviceId = SDL.SDL_OpenAudioDevice(null, 0, ref wantedSpec, out deviceSpec, AudioDeviceFlags)) == 0)
            {
                Helpers.LogWarning($"SDL_OpenAudio ({wantedSpec.channels} channels, {wantedSpec.freq} Hz): {SDL.SDL_GetError()}\n");
                wantedSpec.channels = (byte)next_nb_channels[Math.Min(7, (int)wantedSpec.channels)];
                if (wantedSpec.channels == 0)
                {
                    wantedSpec.freq = next_sample_rates[next_sample_rate_idx--];
                    wantedSpec.channels = (byte)wantedChannelCount;
                    if (wantedSpec.freq == 0)
                    {
                        Helpers.LogError("No more combinations to try, audio open failed\n");
                        return -1;
                    }
                }

                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedSpec.channels);
            }

            if (deviceSpec.format != SDL.AUDIO_S16SYS)
            {
                Helpers.LogError($"SDL advised audio format {deviceSpec.format} is not supported!\n");
                return -1;
            }

            if (deviceSpec.channels != wantedSpec.channels)
            {
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(deviceSpec.channels);
                if (wantedChannelLayout == 0)
                {
                    Helpers.LogError($"SDL advised channel count {deviceSpec.channels} is not supported!\n");
                    return -1;
                }
            }

            audioDeviceSpec.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audioDeviceSpec.Frequency = deviceSpec.freq;
            audioDeviceSpec.Layout = wantedChannelLayout;
            audioDeviceSpec.Channels = deviceSpec.channels;

            if (audioDeviceSpec.BytesPerSecond <= 0 || audioDeviceSpec.FrameSize <= 0)
            {
                Helpers.LogError("av_samples_get_buffer_size failed\n");
                return -1;
            }

            ReadBufferIndex = 0;
            ReadBufferSize = 0;

            return (int)deviceSpec.size;
        }

        public void CloseAudio()
        {
            SDL.SDL_CloseAudioDevice(AudioDeviceId);
        }

        public void PauseAudio()
        {
            SDL.SDL_PauseAudioDevice(AudioDeviceId, 0);
        }

        /* prepare a new audio buffer */
        private void sdl_audio_callback(IntPtr opaque, IntPtr audioStream, int pendingByteCount)
        {
            AudioCallbackTime = Clock.SystemTime;

            while (pendingByteCount > 0)
            {
                if (ReadBufferIndex >= ReadBufferSize)
                {
                    var audio_size = Container.Audio.RefillOutputBuffer();
                    if (audio_size < 0)
                    {
                        // if error, just output silence.
                        Container.Audio.OutputBuffer = null;
                        ReadBufferSize = Constants.SDL_AUDIO_MIN_BUFFER_SIZE / Container.Audio.HardwareSpec.FrameSize * Container.Audio.HardwareSpec.FrameSize;
                    }
                    else
                    {
                        ReadBufferSize = audio_size;
                    }

                    ReadBufferIndex = 0;
                }

                var readByteCount = ReadBufferSize - ReadBufferIndex;
                if (readByteCount > pendingByteCount)
                    readByteCount = pendingByteCount;

                var outputStream = (byte*)audioStream;
                var inputStream = Container.Audio.OutputBuffer + ReadBufferIndex;

                if (!Container.IsMuted && Container.Audio.OutputBuffer != null && audio_volume == SDL.SDL_MIX_MAXVOLUME)
                {
                    for (var b = 0; b < readByteCount; b++)
                        outputStream[b] = inputStream[b];
                }
                else
                {
                    for (var b = 0; b < readByteCount; b++)
                        outputStream[b] = 0;

                    if (!Container.IsMuted && Container.Audio.OutputBuffer != null)
                        SDL.SDL_MixAudioFormat(outputStream, inputStream, SDL.AUDIO_S16SYS, (uint)readByteCount, audio_volume);
                }

                pendingByteCount -= readByteCount;
                audioStream += readByteCount;
                ReadBufferIndex += readByteCount;
            }

            // Let's assume the audio driver that is used by SDL has two periods.
            if (!Container.Audio.FrameTime.IsNaN())
            {
                var readBufferAvailable = ReadBufferSize - ReadBufferIndex;
                var bufferDuration = (2d * Container.Audio.HardwareBufferSize + readBufferAvailable) / Container.Audio.HardwareSpec.BytesPerSecond;
                Container.AudioClock.Set(Container.Audio.FrameTime - bufferDuration, Container.Audio.FrameSerial, AudioCallbackTime);
                Container.ExternalClock.SyncToSlave(Container.AudioClock);
            }
        }

        public void update_volume(int sign, double step)
        {
            var volume_level = audio_volume > 0 ? (20 * Math.Log(audio_volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volume_level + sign * step) / 20.0), 0);
            audio_volume = (audio_volume == new_volume ? (audio_volume + sign) : new_volume).Clamp(0, SDL.SDL_MIX_MAXVOLUME);
        }
    }
}

namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Threading;

    public unsafe class MediaContainer
    {
        public Thread read_tid;
        public AVInputFormat* iformat;
        public bool abort_request;
        public bool force_refresh;
        public bool paused;
        public bool last_paused;
        public bool queue_attachments_req;
        public bool seek_req;
        public int seek_flags;
        public long seek_pos;
        public long seek_rel;
        public int read_pause_return;
        public AVFormatContext* ic;
        public bool realtime;

        public Clock AudioClock;
        public Clock VideoClock;
        public Clock ExternalClock;

        public AudioComponent Audio { get; } = new();

        public VideoComponent Video { get; } = new();

        public SubtitleComponent Subtitle { get; } = new();

        public ClockSync ClockSyncMode;

        public double audio_clock;
        public int audio_clock_serial;
        public double audio_diff_cum; /* used for AV difference average computation */
        public double audio_diff_avg_coef;
        public double audio_diff_threshold;
        public int audio_diff_avg_count;


        public int audio_hw_buf_size;
        public byte* audio_buf;
        public byte* audio_buf1;
        public uint audio_buf_size; /* in bytes */
        public uint audio_buf1_size;
        public int audio_buf_index; /* in bytes */
        public int audio_write_buf_size;
        public int audio_volume;
        public bool muted;


        public int frame_drops_early;
        public int frame_drops_late;

        public ShowMode show_mode;

        public short[] sample_array = new short[Constants.SAMPLE_ARRAY_SIZE];
        public int sample_array_index;
        public int last_i_start;
        // RDFTContext* rdft;
        // int rdft_bits;
        // FFTSample* rdft_data;
        public int xpos;
        public double last_vis_time;
        public IntPtr vis_texture;
        public IntPtr sub_texture;
        public IntPtr vid_texture;

        public double frame_timer;
        public double frame_last_returned_time;
        public double frame_last_filter_delay;

        public double max_frame_duration;      // maximum duration of a frame - above this, we consider the jump a timestamp discontinuity

        public bool eof;

        public string filename;
        public int width = 1;
        public int height = 1;
        public int xleft;
        public int ytop;
        public int step;

        public int vfilter_idx;

        public AVFilterGraph* agraph;              // audio filter graph

        public AutoResetEvent continue_read_thread = new(false);

        public MediaContainer(ProgramOptions options)
        {
            Options = options ?? new();
        }

        public ProgramOptions Options { get; }

        public ClockSync MasterSyncMode
        {
            get
            {
                if (ClockSyncMode == ClockSync.Video)
                {
                    if (Video.Stream != null)
                        return ClockSync.Video;
                    else
                        return ClockSync.Audio;
                }
                else if (ClockSyncMode == ClockSync.Audio)
                {
                    if (Audio.Stream != null)
                        return ClockSync.Audio;
                    else
                        return ClockSync.External;
                }
                else
                {
                    return ClockSync.External;
                }
            }
        }

        /* get the current master clock value */
        public double MasterTime
        {
            get
            {
                switch (MasterSyncMode)
                {
                    case ClockSync.Video:
                        return VideoClock.Time;
                    case ClockSync.Audio:
                        return AudioClock.Time;
                    default:
                        return ExternalClock.Time;
                }
            }
        }



        public void check_external_clock_speed()
        {
            if (Video.StreamIndex >= 0 && Video.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES ||
                Audio.StreamIndex >= 0 && Audio.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES)
            {
                ExternalClock.SetSpeed(Math.Max(Constants.EXTERNAL_CLOCK_SPEED_MIN, ExternalClock.SpeedRatio - Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else if ((Video.StreamIndex < 0 || Video.Packets.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES) &&
                     (Audio.StreamIndex < 0 || Audio.Packets.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES))
            {
                ExternalClock.SetSpeed(Math.Min(Constants.EXTERNAL_CLOCK_SPEED_MAX, ExternalClock.SpeedRatio + Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else
            {
                var speed = ExternalClock.SpeedRatio;
                if (speed != 1.0)
                    ExternalClock.SetSpeed(speed + Constants.EXTERNAL_CLOCK_SPEED_STEP * (1.0 - speed) / Math.Abs(1.0 - speed));
            }
        }

        public void stream_toggle_pause()
        {
            if (paused)
            {
                frame_timer += ffmpeg.av_gettime_relative() / 1000000.0 - VideoClock.LastUpdated;
                if (read_pause_return != ffmpeg.AVERROR(38))
                {
                    VideoClock.IsPaused = false;
                }
                VideoClock.Set(VideoClock.Time, VideoClock.Serial);
            }

            ExternalClock.Set(ExternalClock.Time, ExternalClock.Serial);
            paused = AudioClock.IsPaused = VideoClock.IsPaused = ExternalClock.IsPaused = !paused;
        }
    }
}

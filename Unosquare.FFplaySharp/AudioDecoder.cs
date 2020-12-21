namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;

    public unsafe sealed class AudioDecoder : MediaDecoder
    {
        public AudioDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Audio, codecContext)
        {
            Component = container.Audio;
            var ic = container.InputContext;

            if ((ic->iformat->flags & (ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK)) != 0 &&
                ic->iformat->read_seek.Pointer == IntPtr.Zero)
            {
                StartPts = Component.Stream->start_time;
                StartPtsTimeBase = Component.Stream->time_base;
            }
        }

        public new AudioComponent Component { get; }

        public override void Start() => Start(AudioWorkerThreadMethod, nameof(AudioDecoder));

        private void AudioWorkerThreadMethod()
        {
            FrameHolder af;
            var last_serial = -1;
            int got_frame = 0;
            int ret = 0;

            var frame = ffmpeg.av_frame_alloc();

            const int bufLength = 1024;
            var buf1 = stackalloc byte[bufLength];
            var buf2 = stackalloc byte[bufLength];

            do
            {
                if ((got_frame = DecodeFrame(out frame, out _)) < 0)
                    goto the_end;

                if (got_frame != 0)
                {
                    var tb = new AVRational() { num = 1, den = frame->sample_rate };
                    var dec_channel_layout = (long)Helpers.get_valid_channel_layout(frame->channel_layout, frame->channels);

                    var reconfigure =
                        Helpers.cmp_audio_fmts(Component.FilterSpec.SampleFormat, Component.FilterSpec.Channels,
                                       (AVSampleFormat)frame->format, frame->channels) ||
                        Component.FilterSpec.Layout != dec_channel_layout ||
                        Component.FilterSpec.Frequency != frame->sample_rate ||
                        Component.Decoder.PacketSerial != last_serial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(buf1, bufLength, -1, (ulong)Component.FilterSpec.Layout);
                        ffmpeg.av_get_channel_layout_string(buf2, bufLength, -1, (ulong)dec_channel_layout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{Component.FilterSpec.Frequency} ch:{Component.FilterSpec.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(Component.FilterSpec.SampleFormat)} layout:{Helpers.PtrToString(buf1)} serial:{last_serial} to " +
                           $"rate:{frame->sample_rate} ch:{frame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)} layout:{Helpers.PtrToString(buf2)} serial:{Component.Decoder.PacketSerial}\n");

                        Component.FilterSpec.SampleFormat = (AVSampleFormat)frame->format;
                        Component.FilterSpec.Channels = frame->channels;
                        Component.FilterSpec.Layout = dec_channel_layout;
                        Component.FilterSpec.Frequency = frame->sample_rate;
                        last_serial = Component.Decoder.PacketSerial;

                        if ((ret = Component.configure_audio_filters(true)) < 0)
                            goto the_end;
                    }

                    if ((ret = ffmpeg.av_buffersrc_add_frame(Component.InputFilter, frame)) < 0)
                        goto the_end;

                    while ((ret = ffmpeg.av_buffersink_get_frame_flags(Component.OutputFilter, frame, 0)) >= 0)
                    {
                        tb = ffmpeg.av_buffersink_get_time_base(Component.OutputFilter);

                        if ((af = Component.Frames.PeekWriteable()) == null)
                            goto the_end;

                        af.Pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                        af.Position = frame->pkt_pos;
                        af.Serial = Component.Decoder.PacketSerial;
                        af.Duration = ffmpeg.av_q2d(new AVRational() { num = frame->nb_samples, den = frame->sample_rate });

                        ffmpeg.av_frame_move_ref(af.FramePtr, frame);
                        Component.Frames.Push();

                        if (Component.Packets.Serial != Component.Decoder.PacketSerial)
                            break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                        Component.Decoder.HasFinished = Component.Decoder.PacketSerial;
                }
            } while (ret >= 0 || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF);
        the_end:
            var agraph = Component.FilterGraph;
            ffmpeg.avfilter_graph_free(&agraph);
            agraph = null;
            ffmpeg.av_frame_free(&frame);
        }
    }
}

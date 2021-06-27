namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFCodecContext : UnmanagedCountedReference<AVCodecContext>
    {
        public FFCodecContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avcodec_alloc_context3(null));
        }

        public AVRational PacketTimeBase
        {
            get => Pointer->pkt_timebase;
            set => Pointer->pkt_timebase = value;
        }

        public AVCodecID CodecId
        {
            get => Pointer->codec_id;
            set => Pointer->codec_id = value;
        }

        public string CodecName => FFCodec.GetName(CodecId);

        public int LowResFactor
        {
            get => Pointer->lowres;
            set => Pointer->lowres = value;
        }

        public int Flags2
        {
            get => Pointer->flags2;
            set => Pointer->flags2 = value;
        }

        public AVMediaType CodecType => Pointer->codec_type;

        public int Width => Pointer->width;

        public int Height => Pointer->height;

        public long FaultyPtsCount => Pointer->pts_correction_num_faulty_pts;

        public long FaultyDtsCount => Pointer->pts_correction_num_faulty_dts;

        public int SampleRate => Pointer->sample_rate;

        public int Channels => Pointer->channels;

        public long ChannelLayout => Convert.ToInt64(Pointer->channel_layout);

        public AVSampleFormat SampleFormat => Pointer->sample_fmt;

        public void ApplyStreamParameters(FFStream stream)
        {
            var resultCode = ffmpeg.avcodec_parameters_to_context(Pointer, stream.CodecParameters.Pointer);
            if (resultCode < 0)
                throw new FFmpegException(resultCode, "Unable to apply stream parameters");
        }
        
        public int ReceiveFrame(FFFrame frame) =>
            ffmpeg.avcodec_receive_frame(Pointer, frame.Pointer);

        public int DecodeSubtitle(FFSubtitle frame, FFPacket packet, ref int gotSubtitle)
        {
            int gotResult;
            var resultCode = ffmpeg.avcodec_decode_subtitle2(
                Pointer,
                frame.Pointer,
                &gotResult,
                packet.Pointer);

            gotSubtitle = gotResult;
            return resultCode;
        }

        public void FlushBuffers() => ffmpeg.avcodec_flush_buffers(Pointer);

        public int SendPacket(FFPacket packet) => ffmpeg.avcodec_send_packet(Pointer, packet.Pointer);

        public void Open(FFCodec codec, FFDictionary codecOptions)
        {
            var codecOptionsArg = codecOptions.Pointer;
            var resultCode = ffmpeg.avcodec_open2(Pointer, codec.Pointer, &codecOptionsArg);
            codecOptions.Update(codecOptionsArg);

            if (resultCode < 0)
                throw new FFmpegException(resultCode, $"Could not open codec '{codec.Name}'");
        }

        protected override unsafe void ReleaseInternal(AVCodecContext* pointer) =>
            ffmpeg.avcodec_free_context(&pointer);
    }
}

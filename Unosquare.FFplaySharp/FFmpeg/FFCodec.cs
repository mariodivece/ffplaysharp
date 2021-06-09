namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFCodec : UnmanagedReference<AVCodec>
    {
        public FFCodec(AVCodec* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public AVCodecID Id => Pointer->id;

        public FFMediaClass PrivateClass =>
            FFMediaClass.FromPrivateClass(Pointer->priv_class);

        public int MaxLowResFactor => Pointer->max_lowres;

        public static string GetName(AVCodecID codecId) => ffmpeg.avcodec_get_name(codecId);

        public static FFCodec FromDecoderId(AVCodecID codecId)
        {
            var pointer = ffmpeg.avcodec_find_decoder(codecId);
            return pointer != null ? new(pointer) : null;
        }

        public static FFCodec FromEncoderId(AVCodecID codecId)
        {
            var pointer = ffmpeg.avcodec_find_decoder(codecId);
            return pointer != null ? new(pointer) : null;
        }

        public static FFCodec FromDecoderName(string name)
        {
            var pointer = ffmpeg.avcodec_find_decoder_by_name(name);
            return pointer != null ? new(pointer) : null;
        }
    }
}

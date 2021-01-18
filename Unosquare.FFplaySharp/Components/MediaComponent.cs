namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using System.Threading;
    using Unosquare.FFplaySharp.Primitives;

    public abstract unsafe class MediaComponent
    {
        private readonly int ReorderPts;
        private PacketHolder PendingPacket;
        private bool IsPacketPending;
        private Thread Worker;

        protected MediaComponent(MediaContainer container)
        {
            Container = container;
            Packets = new(this);
            Frames = CreateFrameQueue();
            ReorderPts = Container.Options.decoder_reorder_pts;
        }

        public MediaContainer Container { get; }

        public PacketQueue Packets { get; }

        public FrameQueue Frames { get; }

        public AVCodecContext* CodecContext { get; private set; }

        public AVStream* Stream;

        public int StreamIndex;

        public int LastStreamIndex;

        public abstract AVMediaType MediaType { get; }

        public string MediaTypeString => ffmpeg.av_get_media_type_string(MediaType);

        public bool IsAudio => MediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;

        public bool IsVideo => MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;

        public bool IsSubtitle => MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public abstract string WantedCodecName { get; }

        public bool IsPictureAttachmentStream =>
            MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO &&
            Stream != null &&
            Stream->disposition.HasFlag(ffmpeg.AV_DISPOSITION_ATTACHED_PIC);

        public bool HasEnoughPackets
        {
            get
            {
                var duration = Packets.DurationUnits > 0
                    ? Stream->time_base.ToFactor() * Packets.DurationUnits
                    : 0d;

                return StreamIndex < 0
                    || Packets.IsClosed
                    || IsPictureAttachmentStream
                    || Packets.Count > Constants.MinPacketCount
                    && (duration <= 0 || duration > 1.0);
            }
        }

        public int PacketSerial { get; private set; }

        public int FinalSerial { get; protected set; }

        public bool HasFinishedDecoding => Stream == null || (FinalSerial == Packets.Serial && Frames.PendingCount == 0);

        public virtual void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            AbortDecoder();
            DisposeDecoder();
            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }

        protected int DecodeFrame(out AVFrame* decodedFrame, out AVSubtitle* decodedSubtitle)
        {
            var resultCode = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            decodedSubtitle = null;
            decodedFrame = null;

            while (true)
            {
                PacketHolder currentPacket = null;

                if (Packets.Serial == PacketSerial)
                {
                    do
                    {
                        if (Packets.IsClosed)
                            return -1;

                        switch (CodecContext->codec_type)
                        {
                            case AVMediaType.AVMEDIA_TYPE_VIDEO:
                                if (decodedFrame == null)
                                    decodedFrame = ffmpeg.av_frame_alloc();

                                resultCode = ffmpeg.avcodec_receive_frame(CodecContext, decodedFrame);
                                if (resultCode >= 0)
                                {
                                    if (ReorderPts == -1)
                                        decodedFrame->pts = decodedFrame->best_effort_timestamp;
                                    else if (ReorderPts == 0)
                                        decodedFrame->pts = decodedFrame->pkt_dts;
                                }

                                break;
                            case AVMediaType.AVMEDIA_TYPE_AUDIO:
                                if (decodedFrame == null) decodedFrame = ffmpeg.av_frame_alloc();

                                resultCode = ffmpeg.avcodec_receive_frame(CodecContext, decodedFrame);
                                break;
                        }

                        if (resultCode == ffmpeg.AVERROR_EOF)
                        {
                            FinalSerial = PacketSerial;
                            ffmpeg.avcodec_flush_buffers(CodecContext);
                            return 0;
                        }

                        if (resultCode >= 0)
                            return 1;

                    } while (resultCode != ffmpeg.AVERROR(ffmpeg.EAGAIN));
                }

                do
                {
                    if (Packets.Count == 0)
                        Container.NeedsMorePacketsEvent.Set();

                    if (IsPacketPending)
                    {
                        currentPacket = PendingPacket;
                        IsPacketPending = false;
                    }
                    else
                    {
                        currentPacket = Packets.Get(true);
                        if (Packets.IsClosed)
                            return -1;

                        if (currentPacket != null)
                            PacketSerial = currentPacket.Serial;
                    }

                    if (Packets.Serial == PacketSerial)
                        break;

                    currentPacket?.Dispose();

                } while (true);

                if (currentPacket.IsFlushPacket)
                {
                    FlushCodecBuffers();
                }
                else
                {
                    if (CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        var gotSubtitle = 0;

                        // TODO: ensure subtatile gets freed. Pretty sure there is a memory leak around here.
                        if (decodedSubtitle == null)
                            decodedSubtitle = (AVSubtitle*)ffmpeg.av_malloc((ulong)sizeof(AVSubtitle));

                        resultCode = ffmpeg.avcodec_decode_subtitle2(CodecContext, decodedSubtitle, &gotSubtitle, currentPacket.PacketPtr);

                        if (resultCode < 0)
                        {
                            resultCode = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                        }
                        else
                        {
                            if (gotSubtitle != 0 && currentPacket.PacketPtr->data == null)
                            {
                                IsPacketPending = true;
                                PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                            }

                            resultCode = gotSubtitle != 0
                                ? 0
                                : currentPacket.PacketPtr->data != null
                                ? ffmpeg.AVERROR(ffmpeg.EAGAIN)
                                : ffmpeg.AVERROR_EOF;
                        }
                    }
                    else
                    {
                        if (ffmpeg.avcodec_send_packet(CodecContext, currentPacket.PacketPtr) == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            Helpers.LogError(CodecContext, "Receive_frame and send_packet both returned EAGAIN, which is an API violation.\n");
                            IsPacketPending = true;
                            PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                        }
                    }

                    currentPacket?.Dispose();
                }
            }
        }

        protected virtual void FlushCodecBuffers()
        {
            ffmpeg.avcodec_flush_buffers(CodecContext);
            FinalSerial = 0;
        }

        public virtual int InitializeDecoder(AVCodecContext* codecContext, int streamIndex)
        {
            StreamIndex = streamIndex;
            Stream = Container.InputContext->streams[streamIndex];
            CodecContext = codecContext;
            PacketSerial = -1;
            return 0;
        }

        public void DisposeDecoder()
        {
            PendingPacket?.Dispose();
            var codecContext = CodecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            CodecContext = null;
        }

        public void AbortDecoder()
        {
            Packets.Close();
            Frames.SignalChanged();
            Worker.Join();
            Worker = null;
            Packets.Clear();
        }

        private void Start(ThreadStart workerMethod, string threadName)
        {
            Packets.Open();
            Worker = new Thread(workerMethod) { Name = threadName, IsBackground = true };
            Worker.Start();
        }

        public virtual void Start() => Start(DecodingThreadMethod, $"{GetType().Name}Worker");

        protected abstract FrameQueue CreateFrameQueue();

        protected abstract void DecodingThreadMethod();
    }
}

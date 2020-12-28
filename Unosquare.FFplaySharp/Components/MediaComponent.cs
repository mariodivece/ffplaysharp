namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using System.Threading;
    using Unosquare.FFplaySharp.Primitives;

    public abstract unsafe class MediaComponent
    {
        private readonly int ReorderPts;
        private readonly AutoResetEvent EmptyQueueEvent;

        private PacketHolder PendingPacket;
        private bool IsPacketPending;
        private long NextPts;
        private AVRational NextPtsTimeBase;
        private Thread Worker;

        protected MediaComponent(MediaContainer container)
        {
            Container = container;
            Packets = new(this);
            Frames = CreateFrameQueue();
            EmptyQueueEvent = Container.continue_read_thread;
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

        public bool IsAudio => MediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;

        public bool IsVideo => MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;

        public bool IsSubtitle => MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public bool IsPictureAttachmentStream =>
            MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO &&
            Stream != null &&
            (Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0;

        public bool HasEnoughPackets
        {
            get
            {
                return StreamIndex < 0 ||
                   Packets.IsClosed ||
                   IsPictureAttachmentStream ||
                   Packets.Count > Constants.MIN_FRAMES && (Packets.Duration == 0 ||
                   ffmpeg.av_q2d(Stream->time_base) * Packets.Duration > 1.0);
            }
        }

        public int PacketSerial { get; private set; }

        public int HasFinished { get; set; }

        public long StartPts { get; set; }

        public AVRational StartPtsTimeBase { get; set; }

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

            for (; ; )
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
                                if (resultCode >= 0)
                                {
                                    var decoderTimeBase = new AVRational
                                    {
                                        num = 1,
                                        den = decodedFrame->sample_rate
                                    };

                                    if (decodedFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                        decodedFrame->pts = ffmpeg.av_rescale_q(decodedFrame->pts, CodecContext->pkt_timebase, decoderTimeBase);
                                    else if (NextPts != ffmpeg.AV_NOPTS_VALUE)
                                        decodedFrame->pts = ffmpeg.av_rescale_q(NextPts, NextPtsTimeBase, decoderTimeBase);

                                    if (decodedFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                    {
                                        NextPts = decodedFrame->pts + decodedFrame->nb_samples;
                                        NextPtsTimeBase = decoderTimeBase;
                                    }
                                }
                                break;
                        }
                        if (resultCode == ffmpeg.AVERROR_EOF)
                        {
                            HasFinished = PacketSerial;
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
                        EmptyQueueEvent.Set();

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
                    ffmpeg.avcodec_flush_buffers(CodecContext);
                    HasFinished = 0;
                    NextPts = StartPts;
                    NextPtsTimeBase = StartPtsTimeBase;
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

                            resultCode = gotSubtitle != 0 ? 0 : (currentPacket.PacketPtr->data != null ? ffmpeg.AVERROR(ffmpeg.EAGAIN) : ffmpeg.AVERROR_EOF);
                        }
                    }
                    else
                    {
                        if (ffmpeg.avcodec_send_packet(CodecContext, currentPacket.PacketPtr) == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            ffmpeg.av_log(CodecContext, ffmpeg.AV_LOG_ERROR, "Receive_frame and send_packet both returned EAGAIN, which is an API violation.\n");
                            IsPacketPending = true;
                            PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                        }
                    }

                    currentPacket?.Dispose();
                }
            }
        }

        public virtual void InitializeDecoder(AVCodecContext* codecContext)
        {
            CodecContext = codecContext;
            StartPts = ffmpeg.AV_NOPTS_VALUE;
            PacketSerial = -1;
            StartPtsTimeBase = new();
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

        public virtual void Start() => Start(WorkerThreadMethod, $"{GetType().Name}Worker");

        protected abstract FrameQueue CreateFrameQueue();

        protected abstract void WorkerThreadMethod();
    }
}

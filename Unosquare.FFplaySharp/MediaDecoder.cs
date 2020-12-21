namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;

    public abstract unsafe class MediaDecoder
    {
        private readonly int ReorderPts;
        private readonly PacketQueue Packets;
        private readonly FrameQueue Frames;
        private readonly AutoResetEvent EmptyQueueEvent;

        private PacketHolder PendingPacket;
        private bool IsPacketPending;
        private long NextPts;
        private AVRational NextPtsTimeBase;
        private Thread Worker;

        protected MediaDecoder(MediaComponent component, AVCodecContext* codecContext)
        {
            Component = component;
            Container = component.Container;
            CodecContext = codecContext;
            Packets = component.Packets;
            Frames = component.Frames;
            EmptyQueueEvent = component.Container.continue_read_thread;
            StartPts = ffmpeg.AV_NOPTS_VALUE;
            PacketSerial = -1;
            ReorderPts = component.Container.Options.decoder_reorder_pts;
            StartPtsTimeBase = new();
        }

        public AVCodecContext* CodecContext { get; private set; }

        public MediaComponent Component { get; }

        public MediaContainer Container { get; }

        public int PacketSerial { get; private set; }

        public int HasFinished { get; set; }

        public long StartPts { get; set; }

        public AVRational StartPtsTimeBase { get; set; }

        protected int DecodeFrame(out AVFrame* frame, out AVSubtitle* sub)
        {
            int ret = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            sub = null;
            frame = null;

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
                                if (frame == null) frame = ffmpeg.av_frame_alloc();

                                ret = ffmpeg.avcodec_receive_frame(CodecContext, frame);
                                if (ret >= 0)
                                {
                                    if (ReorderPts == -1)
                                    {
                                        frame->pts = frame->best_effort_timestamp;
                                    }
                                    else if (ReorderPts == 0)
                                    {
                                        frame->pts = frame->pkt_dts;
                                    }
                                }
                                break;
                            case AVMediaType.AVMEDIA_TYPE_AUDIO:
                                if (frame == null) frame = ffmpeg.av_frame_alloc();

                                ret = ffmpeg.avcodec_receive_frame(CodecContext, frame);
                                if (ret >= 0)
                                {
                                    AVRational tb = new();
                                    tb.num = 1;
                                    tb.den = frame->sample_rate;

                                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                                        frame->pts = ffmpeg.av_rescale_q(frame->pts, CodecContext->pkt_timebase, tb);
                                    else if (NextPts != ffmpeg.AV_NOPTS_VALUE)
                                        frame->pts = ffmpeg.av_rescale_q(NextPts, NextPtsTimeBase, tb);
                                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                                    {
                                        NextPts = frame->pts + frame->nb_samples;
                                        NextPtsTimeBase = tb;
                                    }
                                }
                                break;
                        }
                        if (ret == ffmpeg.AVERROR_EOF)
                        {
                            HasFinished = PacketSerial;
                            ffmpeg.avcodec_flush_buffers(CodecContext);
                            return 0;
                        }
                        if (ret >= 0)
                            return 1;
                    } while (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN));
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
                        int got_frame = 0;
                        sub = (AVSubtitle*)ffmpeg.av_malloc((ulong)sizeof(AVSubtitle));
                        ret = ffmpeg.avcodec_decode_subtitle2(CodecContext, sub, &got_frame, currentPacket.PacketPtr);

                        if (ret < 0)
                        {
                            ret = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                        }
                        else
                        {
                            if (got_frame != 0 && currentPacket.PacketPtr->data == null)
                            {
                                IsPacketPending = true;
                                PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                            }

                            ret = got_frame != 0 ? 0 : (currentPacket.PacketPtr->data != null ? ffmpeg.AVERROR(ffmpeg.EAGAIN) : ffmpeg.AVERROR_EOF);
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

        public void Dispose()
        {
            PendingPacket?.Dispose();
            var codecContext = CodecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            CodecContext = null;
        }

        public void Abort()
        {
            Packets.Close();
            Frames.SignalChanged();
            Worker.Join();
            Worker = null;
            Packets.Clear();
        }

        protected void Start(ThreadStart workerMethod, string threadName)
        {
            Packets.Open();
            Worker = new Thread(workerMethod) { Name = threadName, IsBackground = true };
            Worker.Start();
        }

        public abstract void Start();
    }
}

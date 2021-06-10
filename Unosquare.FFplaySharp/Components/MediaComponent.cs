﻿namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System.Threading;
    using Unosquare.FFplaySharp.Primitives;

    public abstract unsafe class MediaComponent
    {
        private readonly ThreeState ReorderPts;
        private FFPacket PendingPacket;
        private bool IsPacketPending;
        private Thread Worker;

        protected MediaComponent(MediaContainer container)
        {
            Container = container;
            Packets = new(this);
            Frames = CreateFrameQueue();
            ReorderPts = Container.Options.IsPtsReorderingEnabled;
        }

        public MediaContainer Container { get; }

        public PacketQueue Packets { get; }

        public FrameQueue Frames { get; }

        public FFCodecContext CodecContext { get; private set; }

        public FFStream Stream;

        public int StreamIndex { get; set; }

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
            Stream.DispositionFlags.HasFlag(ffmpeg.AV_DISPOSITION_ATTACHED_PIC);

        public bool HasEnoughPackets
        {
            get
            {
                var duration = Packets.DurationUnits > 0
                    ? Stream.TimeBase.ToFactor() * Packets.DurationUnits
                    : 0d;

                return StreamIndex < 0
                    || Packets.IsClosed
                    || IsPictureAttachmentStream
                    || Packets.Count > Constants.MinPacketCount
                    && (duration <= 0 || duration > 1.0);
            }
        }

        public int PacketGroupIndex { get; private set; }

        public int FinalPacketGroupIndex { get; protected set; }

        public bool HasFinishedDecoding => Stream == null || (FinalPacketGroupIndex == Packets.GroupIndex && Frames.PendingCount == 0);

        public virtual void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext.Streams.Count)
                return;

            AbortDecoder();
            DisposeDecoder();
            Container.InputContext.Streams[StreamIndex].DiscardFlags = AVDiscard.AVDISCARD_ALL;
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
                FFPacket currentPacket = null;

                if (Packets.GroupIndex == PacketGroupIndex)
                {
                    do
                    {
                        if (Packets.IsClosed)
                            return -1;

                        switch (CodecContext.CodecType)
                        {
                            case AVMediaType.AVMEDIA_TYPE_VIDEO:
                                if (decodedFrame == null)
                                    decodedFrame = ffmpeg.av_frame_alloc();

                                resultCode = ffmpeg.avcodec_receive_frame(CodecContext.Pointer, decodedFrame);
                                if (resultCode >= 0)
                                {
                                    if (ReorderPts.IsAuto())
                                        decodedFrame->pts = decodedFrame->best_effort_timestamp;
                                    else if (ReorderPts == 0)
                                        decodedFrame->pts = decodedFrame->pkt_dts;
                                }

                                break;
                            case AVMediaType.AVMEDIA_TYPE_AUDIO:
                                if (decodedFrame == null) decodedFrame = ffmpeg.av_frame_alloc();

                                resultCode = ffmpeg.avcodec_receive_frame(CodecContext.Pointer, decodedFrame);
                                break;
                        }

                        if (resultCode == ffmpeg.AVERROR_EOF)
                        {
                            FinalPacketGroupIndex = PacketGroupIndex;
                            ffmpeg.avcodec_flush_buffers(CodecContext.Pointer);
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
                        currentPacket = Packets.Dequeue(true);
                        if (Packets.IsClosed)
                            return -1;

                        if (currentPacket != null)
                            PacketGroupIndex = currentPacket.GroupIndex;
                    }

                    if (Packets.GroupIndex == PacketGroupIndex)
                        break;

                    currentPacket?.Release();

                } while (true);

                if (currentPacket.IsFlushPacket)
                {
                    currentPacket.Release();
                    FlushCodecBuffers();
                }
                else
                {
                    if (CodecContext.CodecType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        var gotSubtitle = 0;

                        // TODO: ensure subtatile gets freed. Pretty sure there is a memory leak around here.
                        if (decodedSubtitle == null)
                            decodedSubtitle = (AVSubtitle*)ffmpeg.av_malloc((ulong)sizeof(AVSubtitle));

                        resultCode = ffmpeg.avcodec_decode_subtitle2(CodecContext.Pointer, decodedSubtitle, &gotSubtitle, currentPacket.Pointer);

                        if (resultCode < 0)
                        {
                            resultCode = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                        }
                        else
                        {
                            if (gotSubtitle != 0 && currentPacket.Data == null)
                            {
                                IsPacketPending = true;
                                PendingPacket = currentPacket.Clone();
                            }

                            resultCode = gotSubtitle != 0
                                ? 0
                                : currentPacket.Data != null
                                ? ffmpeg.AVERROR(ffmpeg.EAGAIN)
                                : ffmpeg.AVERROR_EOF;
                        }
                    }
                    else
                    {
                        if (CodecContext.SendPacket(currentPacket) == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            Helpers.LogError(CodecContext.Pointer, "Receive_frame and send_packet both returned EAGAIN, which is an API violation.\n");
                            IsPacketPending = true;
                            PendingPacket = currentPacket.Clone();
                        }
                    }

                    currentPacket?.Release();
                }
            }
        }

        protected virtual void FlushCodecBuffers()
        {
            CodecContext.FlushBuffers();
            FinalPacketGroupIndex = 0;
        }

        public virtual int InitializeDecoder(FFCodecContext codecContext, int streamIndex)
        {
            StreamIndex = streamIndex;
            Stream = Container.InputContext.Streams[streamIndex];
            CodecContext = codecContext;
            PacketGroupIndex = -1;
            return 0;
        }

        public void DisposeDecoder()
        {
            PendingPacket?.Release();
            CodecContext.Release();
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

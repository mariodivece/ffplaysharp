namespace Unosquare.FFplaySharp.Components;

public abstract class MediaComponent
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

    public PacketStore Packets { get; }

    public FrameStore Frames { get; }

    public FFCodecContext CodecContext { get; private set; }

    public FFStream Stream { get; protected set; }

    public int StreamIndex { get; set; }

    public int LastStreamIndex;

    public abstract AVMediaType MediaType { get; }

    public string MediaTypeString => MediaType.ToName();

    public bool IsAudio => MediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;

    public bool IsVideo => MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;

    public bool IsSubtitle => MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

    public abstract string? WantedCodecName { get; }

    public bool IsPictureAttachmentStream =>
        MediaType.IsVideo() &&
        Stream.IsNotNull() &&
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

    public bool HasFinishedDecoding => Stream.IsNull() || (FinalPacketGroupIndex == Packets.GroupIndex && !Frames.CanRead);

    public virtual void Close()
    {
        if (StreamIndex < 0 || StreamIndex >= Container.Input.Streams.Count)
            return;

        AbortDecoder();
        DisposeDecoder();
        Container.Input.Streams[StreamIndex].DiscardFlags = AVDiscard.AVDISCARD_ALL;
        Stream = default;
        StreamIndex = -1;
    }

    protected int DecodeFrame(FFFrame? decodedFrame, FFSubtitle? decodedSubtitle)
    {
        var resultCode = ffmpeg.AVERROR(ffmpeg.EAGAIN);

        while (true)
        {
            if (Packets.GroupIndex == PacketGroupIndex)
            {
                do
                {
                    if (Packets.IsClosed)
                        return -1;

                    switch (CodecContext.CodecType)
                    {
                        case AVMediaType.AVMEDIA_TYPE_VIDEO:
                            resultCode = CodecContext.ReceiveFrame(decodedFrame);
                            if (resultCode >= 0)
                            {
                                if (ReorderPts.IsAuto())
                                    decodedFrame.Pts = decodedFrame.BestEffortPts;
                                else if (ReorderPts == 0)
                                    decodedFrame.Pts = decodedFrame.PacketDts;
                            }

                            break;
                        case AVMediaType.AVMEDIA_TYPE_AUDIO:
                            resultCode = CodecContext.ReceiveFrame(decodedFrame);
                            break;
                    }

                    if (resultCode == ffmpeg.AVERROR_EOF)
                    {
                        FinalPacketGroupIndex = PacketGroupIndex;
                        CodecContext.FlushBuffers();
                        return 0;
                    }

                    if (resultCode >= 0)
                        return 1;

                } while (resultCode != ffmpeg.AVERROR(ffmpeg.EAGAIN));
            }

            FFPacket? currentPacket;
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
                    Packets.TryDequeue(true, out currentPacket);
                    if (Packets.IsClosed)
                    {
                        currentPacket?.Release();
                        return -1;
                    }

                    if (currentPacket.IsNotNull())
                        PacketGroupIndex = currentPacket!.GroupIndex;
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
                if (CodecContext.CodecType.IsSubtitle())
                {
                    var gotSubtitle = 0;
                    resultCode = CodecContext.DecodeSubtitle(decodedSubtitle, currentPacket, ref gotSubtitle);

                    if (resultCode < 0)
                    {
                        resultCode = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                    }
                    else
                    {
                        if (gotSubtitle != 0 && !currentPacket.HasData)
                        {
                            IsPacketPending = true;
                            PendingPacket = currentPacket.Clone();
                        }

                        resultCode = gotSubtitle != 0
                            ? 0
                            : currentPacket.HasData
                            ? ffmpeg.AVERROR(ffmpeg.EAGAIN)
                            : ffmpeg.AVERROR_EOF;
                    }
                }
                else
                {
                    if (CodecContext.SendPacket(currentPacket) == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        CodecContext.LogError("Receive_frame and send_packet both returned EAGAIN, which is an API violation.");
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

    public virtual void InitializeDecoder(FFCodecContext codecContext, int streamIndex)
    {
        StreamIndex = streamIndex;
        Stream = Container.Input.Streams[streamIndex];
        CodecContext = codecContext;
        PacketGroupIndex = -1;
    }

    public void DisposeDecoder()
    {
        PendingPacket?.Release();
        CodecContext.Release();
        CodecContext = default;
    }

    public void AbortDecoder()
    {
        Packets.Close();
        Frames.SignalChanged();
        Worker.Join();
        Worker = default;
        Packets.Clear();
    }

    private void Start(ThreadStart workerMethod, string threadName)
    {
        Packets.Open();
        Worker = new Thread(workerMethod)
        {
            Name = threadName,
            IsBackground = true,
            Priority = Constants.DecodingPriority
        };

        Worker.Start();
    }

    public virtual void Start() => Start(DecodingThreadMethod, $"{GetType().Name}Worker");

    protected abstract FrameStore CreateFrameQueue();

    protected abstract void DecodingThreadMethod();
}

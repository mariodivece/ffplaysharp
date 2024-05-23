﻿namespace Unosquare.FFplaySharp.Components;

public sealed class SubtitleComponent : MediaComponent
{
    public SubtitleComponent(MediaContainer container)
        : base(container)
    {
        // placeholder
    }

    public RescalerContext ConvertContext { get; } = new();

    public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_SUBTITLE;

    public override string? WantedCodecName => Container.Options.AudioForcedCodecName;

    protected override FrameStore CreateFrameQueue() => new(Packets, Constants.SubtitleFrameQueueCapacity, false);

    private int DecodeSubtitleInto(FFSubtitle frame) => DecodeFrame(null, frame);

    protected override void DecodingThreadMethod()
    {
        while (true)
        {
            var frame = new FFSubtitle();
            var gotSubtitle = DecodeSubtitleInto(frame);
            if (gotSubtitle < 0)
            {
                frame.Dispose();
                break;
            }
            else if (gotSubtitle == 0 || frame.Format != 0)
            {
                frame.Dispose();
                continue;
            }

            if (!Frames.LeaseFrameForWriting(out var targetFrame))
            {
                frame.Dispose();
                break;
            }

            var frameTime = frame.Pts.IsValidTimestamp() ? frame.Pts / Clock.TimeBaseMicros : 0;

            // now we can update the picture count
            targetFrame.Update(frame, CodecContext, PacketGroupIndex, frameTime);
            Frames.EnqueueLeasedFrame();
        }
    }
}

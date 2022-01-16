namespace Unosquare.FFplaySharp.Primitives;

public class Clock : ISerialGroupable
{
    /// <summary>
    /// Typically a packet queue to read the group index from.
    /// But it could also hold a reference to itself in case of external clocks.
    /// </summary>
    private readonly ISerialGroupable GroupIndexProvider;

    /// <summary>
    /// Clock base minus time at which we updated the clock.
    /// </summary>
    private double Offset;

    /// <summary>
    /// Creates a new instance of the <see cref="Clock"/> class.
    /// </summary>
    /// <param name="packetQueue">The related group index provider.</param>
    public Clock(ISerialGroupable packetQueue)
    {
        SpeedRatio = 1.0;
        IsPaused = false;
        GroupIndexProvider = packetQueue ?? this;
        Set(double.NaN, -1);
    }

    /// <summary>
    /// The ffmpeg time base. Returns the number of microsends per second (i.e. 1,000,000 microseconds = 1 second).
    /// </summary>
    public static double TimeBaseMicros { get; } = ffmpeg.AV_TIME_BASE.ToDouble();

    /// <summary>
    /// Gets the current relative system time in seconds.
    /// </summary>
    public static double SystemTime => Convert.ToDouble(ffmpeg.av_gettime_relative()) / TimeBaseMicros;

    /// <summary>
    /// Gets the clock base.
    /// </summary>
    public double BaseTime { get; private set; }

    public double LastUpdated { get; private set; }

    public double SpeedRatio { get; private set; }

    /// <summary>
    /// Clock is based on a packet with this group index (serial).
    /// </summary>
    public int GroupIndex { get; private set; }

    public bool IsPaused { get; set; }

    /// <summary>
    /// Pointer to the current packet queue serial, used for obsolete clock detection.
    /// </summary>
    public int QueueGroupIndex
    {
        get => GroupIndexProvider != null
            ? GroupIndexProvider.GroupIndex
            : 0;
    }

    /// <summary>
    /// Gets the current clock value in seconds.
    /// </summary>
    public double Value
    {
        get
        {
            if (QueueGroupIndex != GroupIndex)
                return double.NaN;

            if (IsPaused)
                return BaseTime;

            var systemTime = SystemTime;
            var elapsedTime = systemTime - LastUpdated;
            return Offset + systemTime - elapsedTime * (1.0 - SpeedRatio);
        }
    }

    public void Set(double baseTime, int groupIndex, double systemTime)
    {
        BaseTime = baseTime;
        LastUpdated = systemTime;
        Offset = BaseTime - systemTime;
        GroupIndex = groupIndex;
    }

    public void Set(double baseTime, int groupIndex) =>
        Set(baseTime, groupIndex, SystemTime);

    public void SetSpeed(double speed)
    {
        Set(Value, GroupIndex);
        SpeedRatio = speed;
    }

    /// <summary>
    /// Synchronizes the clock to a slave reference clock.
    /// </summary>
    /// <param name="slaveClock">The clock to synchronize to.</param>
    public void SyncToSlave(Clock slaveClock)
    {
        var currentTime = Value;
        var slaveTime = slaveClock.Value;
        if (!slaveTime.IsNaN() && (currentTime.IsNaN() || Math.Abs(currentTime - slaveTime) > Constants.MediaNoSyncThreshold))
            Set(slaveTime, slaveClock.GroupIndex);
    }
}

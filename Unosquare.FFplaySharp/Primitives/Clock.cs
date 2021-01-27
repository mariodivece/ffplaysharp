namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;

    public class Clock : ISerialProvider
    {
        private readonly ISerialProvider SerialProvider;

        /// <summary>
        /// clock base minus time at which we updated the clock
        /// </summary>
        private double Offset;

        /// <summary>
        /// The ffmpeg time base. Returns the number of microsends per second (i.e. 1,000,000 microseconds = 1 second).
        /// </summary>
        public static double TimeBaseMicros { get; } = Convert.ToDouble(ffmpeg.AV_TIME_BASE);

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
        /// Clock is based on a packet with this serial.
        /// </summary>
        public int Serial { get; private set; }
        
        public bool IsPaused { get; set; }

        /// <summary>
        /// Pointer to the current packet queue serial, used for obsolete clock detection.
        /// </summary>
        public int RelatedSerial { get => SerialProvider != null ? SerialProvider.Serial : 0; }
        
        public Clock(ISerialProvider serialProvider)
        {
            SpeedRatio = 1.0;
            IsPaused = false;
            SerialProvider = serialProvider ?? this;
            Set(double.NaN, -1);
        }

        public void Set(double baseTime, int serial, double systemTime)
        {
            BaseTime = baseTime;
            LastUpdated = systemTime;
            Offset = BaseTime - systemTime;
            Serial = serial;
        }

        public void Set(double baseTime, int serial) => Set(baseTime, serial, SystemTime);

        public void SetSpeed(double speed)
        {
            Set(Value, Serial);
            SpeedRatio = speed;
        }

        /// <summary>
        /// Gets the current clock value in seconds.
        /// </summary>
        public double Value
        {
            get
            {
                if (RelatedSerial != Serial)
                    return double.NaN;

                if (IsPaused)
                {
                    return BaseTime;
                }
                else
                {
                    var systemTime = SystemTime;
                    var elapsedTime = systemTime - LastUpdated;
                    return Offset + systemTime - elapsedTime * (1.0 - SpeedRatio);
                }
            }
        }

        /// <summary>
        /// Synchronizes the clock to a slave reference clock.
        /// </summary>
        /// <param name="slaveClock">The clock to synchronize to.</param>
        public void SyncToSlave(Clock slaveClock)
        {
            var currentTime = Value;
            var slaveTime = slaveClock.Value;
            if (!slaveTime.IsNaN() && (currentTime.IsNaN() || Math.Abs(currentTime - slaveTime) > Constants.AV_NOSYNC_THRESHOLD))
                Set(slaveTime, slaveClock.Serial);
        }
    }
}

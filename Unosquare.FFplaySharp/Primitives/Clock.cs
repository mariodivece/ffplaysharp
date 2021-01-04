namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;

    public class Clock : ISerialProvider
    {
        public static double TimeBaseMicros { get; } = Convert.ToDouble(ffmpeg.AV_TIME_BASE);

        private readonly ISerialProvider SerialProvider;

        public static double SystemTime => Convert.ToDouble(ffmpeg.av_gettime_relative()) / TimeBaseMicros;

        public double BaseTime { get; private set; }           /* clock base */
        
        public double Offset { get; private set; }     /* clock base minus time at which we updated the clock */
        
        public double LastUpdated { get; private set; }
        
        public double SpeedRatio { get; private set; }
        
        public int Serial { get; private set; }           /* clock is based on a packet with this serial */
        
        public bool IsPaused { get; set; }
        
        public int RelatedSerial { get => SerialProvider != null ? SerialProvider.Serial : 0; }    /* pointer to the current packet queue serial, used for obsolete clock detection */
        
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
                    return Offset + systemTime - (systemTime - LastUpdated) * (1.0 - SpeedRatio);
                }
            }
        }

        public void SyncToSlave(Clock slaveClock)
        {
            var currentTime = Value;
            var slaveTime = slaveClock.Value;
            if (!slaveTime.IsNaN() && (currentTime.IsNaN() || Math.Abs(currentTime - slaveTime) > Constants.AV_NOSYNC_THRESHOLD))
                Set(slaveTime, slaveClock.Serial);
        }
    }
}

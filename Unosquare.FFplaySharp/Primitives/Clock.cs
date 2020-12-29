namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;

    public class Clock : ISerialProvider
    {
        private readonly ISerialProvider SerialProvider;

        public double Pts { get; private set; }           /* clock base */
        
        public double PtsDrift { get; private set; }     /* clock base minus time at which we updated the clock */
        
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

        public void Set(double pts, int serial, double time)
        {
            Pts = pts;
            LastUpdated = time;
            PtsDrift = Pts - time;
            Serial = serial;
        }

        public void Set(double pts, int serial)
        {
            var time = ffmpeg.av_gettime_relative() / 1000000.0;
            Set(pts, serial, time);
        }

        public void SetSpeed(double speed)
        {
            Set(Time, Serial);
            SpeedRatio = speed;
        }

        public double Time
        {
            get
            {
                if (RelatedSerial != Serial)
                    return double.NaN;

                if (IsPaused)
                {
                    return Pts;
                }
                else
                {
                    var time = ffmpeg.av_gettime_relative() / 1000000.0;
                    return PtsDrift + time - (time - LastUpdated) * (1.0 - SpeedRatio);
                }
            }
        }

        public void SyncToSlave(Clock slaveClock)
        {
            var currentTime = Time;
            var slaveTime = slaveClock.Time;
            if (!double.IsNaN(slaveTime) && (double.IsNaN(currentTime) || Math.Abs(currentTime - slaveTime) > Constants.AV_NOSYNC_THRESHOLD))
                Set(slaveTime, slaveClock.Serial);
        }
    }
}

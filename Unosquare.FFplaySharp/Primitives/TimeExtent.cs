using System.Globalization;

namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents as <see cref="TimeSpan"/> backed type that can
/// have an undeterminate value <see cref="IsNaN"/> and that can perform
/// comparison and arithmetic operations with <see cref="double"/> and
/// <see cref="TimeSpan"/>. Please note that <see cref="double"/> arithmetic
/// is always represented in seconds.
/// </summary>
public readonly struct TimeExtent :
    IEquatable<TimeExtent>, IEquatable<TimeSpan>, IEquatable<double>,
    IComparable<TimeExtent>, IComparable<TimeSpan>, IComparable<double>
{
    private static readonly TimeExtent ConstantZero = new();
    private static readonly TimeExtent ConstantNaN = new(TimeSpan.MinValue);

    private readonly bool m_IsNan = false;
    private readonly TimeSpan m_Value = TimeSpan.Zero;

    #region Constructors

    /// <summary>
    /// Creates a new instance of the <see cref="TimeExtent"/> struct.
    /// </summary>
    public TimeExtent()
    {
        // placeholder
    }

    private TimeExtent(bool isNaN, TimeSpan value)
    {
        m_IsNan = isNaN;
        m_Value = value;
    }

    private TimeExtent(TimeSpan value)
        : this(isNaN: CheckNaN(value), value: value)
    {
        // placeholder
    }

    private TimeExtent(double seconds)
        : this(MakeTimeSpan(seconds))
    {
        // placeholder
    }

    #endregion

    #region Constants

    /// <summary>
    /// Returns an instance of <see cref="TimeExtent"/> representing Zero.
    /// </summary>
    public static TimeExtent Zero => ConstantZero;

    /// <summary>
    /// Returns an instance of <see cref="TimeExtent"/> representing NaN (unspecified).
    /// </summary>
    public static TimeExtent NaN => ConstantNaN;

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether this instance contains an
    /// undefined value. If set to true, any arithmetic operations
    /// will result in undefined instances of this struct.
    /// </summary>
    public readonly bool IsNaN => m_IsNan;

    /// <summary>
    /// Gets the backing <see cref="TimeSpan"/> value. If <see cref="IsNaN"/>
    /// is set to true, then this value will return <see cref="TimeSpan.MinValue"/>.
    /// </summary>
    public readonly TimeSpan Value => m_Value;

    /// <summary>
    /// Gets the time value expressed in seconds. If <see cref="IsNaN"/>
    /// is set to true, then this value will return <see cref="double.NaN"/>.
    /// </summary>
    public readonly double Seconds => IsNaN ? double.NaN : Value.TotalSeconds;

    /// <summary>
    /// Gets the time value expressed in milliseconds. If <see cref="IsNaN"/>
    /// is set to true, then this value will return <see cref="double.NaN"/>.
    /// </summary>
    public readonly double Milliseconds => IsNaN ? double.NaN : Value.TotalMilliseconds;

    #endregion

    #region CompareTo

    public readonly int CompareTo(TimeExtent other) => !IsNaN
        ? Value.CompareTo(other.Value)
        : IsNaN.CompareTo(other.IsNaN);

    public readonly int CompareTo(TimeSpan other) => IsNaN && CheckNaN(other)
        ? 0
        : !IsNaN && CheckNaN(other)
        ? 1
        : IsNaN && !CheckNaN(other)
        ? -1
        : Value.CompareTo(other);

    public readonly int CompareTo(double other) => IsNaN && CheckNaN(other)
        ? 0
        : !IsNaN && CheckNaN(other)
        ? 1
        : IsNaN && !CheckNaN(other)
        ? -1
        : Value.CompareTo(other);

    #endregion

    #region Equals

    public override bool Equals(object? obj) => obj is TimeExtent other && Equals(other);

    public readonly bool Equals(TimeExtent other) => CompareTo(other) == 0;

    public readonly bool Equals(TimeSpan other) => CompareTo(other) == 0;

    public readonly bool Equals(double other) => CompareTo(other) == 0;

    public override int GetHashCode() => Value.GetHashCode();

    #endregion

    #region Comparison Operators

    public static bool operator ==(TimeExtent left, TimeExtent right) => !left.IsNaN && !right.IsNaN && left.Equals(right);

    public static bool operator !=(TimeExtent left, TimeExtent right) => !(left == right);

    public static bool operator <(TimeExtent left, TimeExtent right) => !left.IsNaN && !right.IsNaN && left.CompareTo(right) < 0;

    public static bool operator <=(TimeExtent left, TimeExtent right) => !left.IsNaN && !right.IsNaN && left.CompareTo(right) <= 0;

    public static bool operator >(TimeExtent left, TimeExtent right) => !left.IsNaN && !right.IsNaN && left.CompareTo(right) > 0;

    public static bool operator >=(TimeExtent left, TimeExtent right) => !left.IsNaN && !right.IsNaN && left.CompareTo(right) >= 0;

    #endregion

    #region Implicit Casting

    public static implicit operator TimeExtent(TimeSpan other) => CheckNaN(other) ? ConstantNaN : new(other);

    public static implicit operator TimeSpan(TimeExtent other) => other.IsNaN ? TimeSpan.MinValue : other.Value;

    public static implicit operator TimeExtent(double other) => CheckNaN(other) ? ConstantNaN : new(other);

    public static implicit operator double(TimeExtent other) => other.IsNaN ? double.NaN : other.Seconds;

    #endregion

    #region Unary Operators

    public static TimeExtent operator +(TimeExtent left) => left.IsNaN ? ConstantNaN : left.Value;

    public static TimeExtent operator -(TimeExtent left) => left.IsNaN ? ConstantNaN : TimeSpan.FromTicks(-left.Value.Ticks);

    #endregion

    #region Increment and Decrement

    /// <summary>
    /// Increments the value by 1 second.
    /// </summary>
    /// <param name="left">The argument.</param>
    /// <returns>The incremented value.</returns>
    public static TimeExtent operator ++(TimeExtent left) => left.IsNaN ? ConstantNaN : left + 1d;

    /// <summary>
    /// Decrements the value by 1 second.
    /// </summary>
    /// <param name="left">The argument.</param>
    /// <returns>The decremented value.</returns>
    public static TimeExtent operator --(TimeExtent left) => left.IsNaN ? ConstantNaN : left - 1d;

    #endregion

    #region Arithmetic Basis

    public static TimeExtent operator +(TimeExtent left, TimeExtent right) => left.IsNaN || right.IsNaN
        ? ConstantNaN : TimeSpan.FromTicks(left.Value.Ticks + right.Value.Ticks);

    public static TimeExtent operator -(TimeExtent left, TimeExtent right) => left.IsNaN || right.IsNaN
        ? ConstantNaN : TimeSpan.FromTicks(left.Value.Ticks - right.Value.Ticks);

    public static TimeExtent operator *(TimeExtent left, TimeExtent right) => left.IsNaN || right.IsNaN 
        ? ConstantNaN : MakeTimeSpan(left.Value.TotalSeconds * right.Value.TotalSeconds);

    public static TimeExtent operator /(TimeExtent left, TimeExtent right) => left.IsNaN || right.IsNaN
        ? ConstantNaN : MakeTimeSpan(left.Value.TotalSeconds / right.Value.TotalSeconds);

    public static TimeExtent operator %(TimeExtent left, TimeExtent right) => left.IsNaN || right.IsNaN
        ? ConstantNaN : MakeTimeSpan(left.Value.TotalSeconds % right.Value.TotalSeconds);

    #endregion

    #region Arithmetic overloads

    public static TimeExtent operator +(TimeExtent left, TimeSpan right) => left + (TimeExtent)right;

    public static TimeExtent operator +(TimeSpan left, TimeExtent right) => (TimeExtent)left + right;

    public static TimeExtent operator +(TimeExtent left, double right) => left + (TimeExtent)right;

    public static TimeExtent operator +(double left, TimeExtent right) => (TimeExtent)left + right;


    public static TimeExtent operator -(TimeExtent left, TimeSpan right) => left - (TimeExtent)right;

    public static TimeExtent operator -(TimeSpan left, TimeExtent right) => (TimeExtent)left - right;

    public static TimeExtent operator -(TimeExtent left, double right) => left - (TimeExtent)right;

    public static TimeExtent operator -(double left, TimeExtent right) => (TimeExtent)left - right;


    public static TimeExtent operator *(TimeExtent left, TimeSpan right) => left * (TimeExtent)right;

    public static TimeExtent operator *(TimeSpan left, TimeExtent right) => (TimeExtent)left * right;

    public static TimeExtent operator *(TimeExtent left, double right) => left * (TimeExtent)right;

    public static TimeExtent operator *(double left, TimeExtent right) => (TimeExtent)left * right;


    public static TimeExtent operator /(TimeExtent left, TimeSpan right) => left / (TimeExtent)right;

    public static TimeExtent operator /(TimeSpan left, TimeExtent right) => (TimeExtent)left / right;

    public static TimeExtent operator /(TimeExtent left, double right) => left / (TimeExtent)right;

    public static TimeExtent operator /(double left, TimeExtent right) => (TimeExtent)left / right;


    public static TimeExtent operator %(TimeExtent left, TimeSpan right) => left % (TimeExtent)right;

    public static TimeExtent operator %(TimeSpan left, TimeExtent right) => (TimeExtent)left % right;

    public static TimeExtent operator %(TimeExtent left, double right) => left % (TimeExtent)right;

    public static TimeExtent operator %(double left, TimeExtent right) => (TimeExtent)left % right;

    #endregion

    #region Instance Methods (Inmutable)

    /// <summary>
    /// Provides a new <see cref="TimeExtent"/> with an absolute value.
    /// If <see cref="IsNaN"/> is true, then the newly produced value will also contain a set <see cref="IsNaN"/>.
    /// </summary>
    /// <returns></returns>
    public readonly TimeExtent Abs() => IsNaN
        ? ConstantNaN
        : Value >= TimeSpan.Zero
        ? Value
        : TimeSpan.FromTicks(-Value.Ticks);

    /// <summary>
    /// Returns the string representation of the <see cref="Seconds"/> value
    /// with 4 fixed decimal places and in <see cref="CultureInfo.InvariantCulture"/>.
    /// If <see cref="IsNaN"/> then it returns the NaN string.
    /// </summary>
    /// <returns>The string representation of this structure.</returns>
    public override string ToString() => ToString("n4", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the string representation of the <see cref="Seconds"/> value
    /// If <see cref="IsNaN"/> then it returns the NaN string.
    /// </summary>
    /// <returns>The string representation of this structure.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) => IsNaN
        ? double.NaN.ToString(format, formatProvider)
        : Seconds.ToString(format, formatProvider);

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates an instance of <see cref="TimeExtent"/> struct based on a <see cref="TimeSpan"/>.
    /// Note that if the <see cref="TimeSpan"/> is set to <see cref="TimeSpan.MinValue"/> then <see cref="IsNaN"/>
    /// will be set to true.
    /// </summary>
    /// <param name="other">The time represented in a <see cref="TimeSpan"/>.</param>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromTimeSpan(TimeSpan other) => CheckNaN(other) ? ConstantNaN : new(other);

    /// <summary>
    /// Creates an instance of <see cref="TimeExtent"/> struct based on number of seconds.
    /// Note that if the <see cref="double"/> is set to <see cref="double.NaN"/> then <see cref="IsNaN"/>
    /// will be set to true.
    /// </summary>
    /// <param name="seconds">The time represented in seconds.</param>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromSeconds(double seconds) => CheckNaN(seconds) ? ConstantNaN : new(seconds);

    /// <summary>
    /// Creates an instance of <see cref="TimeExtent"/> struct based on number of milliseconds.
    /// Note that if the <see cref="double"/> is set to <see cref="double.NaN"/> then <see cref="IsNaN"/>
    /// will be set to true.
    /// </summary>
    /// <param name="milliseconds">The time represented in milliseconds.</param>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromMilliseconds(double milliseconds) => CheckNaN(milliseconds) ? ConstantNaN : new(milliseconds / 1000d);


    /// <summary>
    /// Creates an instance of <see cref="TimeExtent"/> struct.
    /// </summary>
    /// <param name="sw">The <see cref="Stopwatch"/>.</param>
    /// <returns>The new instance.</returns>
    /// <exception cref="ArgumentNullException">The stopwatch instance must not be null.</exception>
    public static TimeExtent FromStopwatch(Stopwatch sw) => sw is null
        ? throw new ArgumentNullException(nameof(sw))
        : new(sw.Elapsed);

    /// <summary>
    /// Retrieves a <see cref="TimeExtent"/> instance based on relative system time.
    /// This is an easy way t get a system timestamp expressed in <see cref="TimeSpan"/>.
    /// </summary>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromSystem() => TimeSpan.FromTicks(Convert.ToInt64(
        TimeSpan.TicksPerSecond * Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));

    /// <summary>
    /// Retrieves a <see cref="TimeExtent"/> instance based on a timestamp previously
    /// obtained from <see cref="Stopwatch.GetTimestamp"/> and up to the current system
    /// timestamp.
    /// </summary>
    /// <param name="startStopwatchTimestamp"></param>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromElapsed(long startStopwatchTimestamp) =>
        Stopwatch.GetElapsedTime(startStopwatchTimestamp);

    /// <summary>
    /// Retrieves a <see cref="TimeExtent"/> instance based on cycles per second.
    /// </summary>
    /// <param name="cyclesPerSecond">The number of cycles per second.</param>
    /// <returns>The new instance.</returns>
    public static TimeExtent FromHertz(double cyclesPerSecond) =>
        CheckNaN(cyclesPerSecond) ? ConstantNaN : new(1d / cyclesPerSecond);

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TimeSpan MakeTimeSpan(double seconds) => CheckNaN(seconds)
        ? TimeSpan.MinValue
        : TimeSpan.FromTicks(Convert.ToInt64(TimeSpan.TicksPerSecond * seconds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CheckNaN(double other) => !double.IsFinite(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CheckNaN(TimeSpan other) => other == TimeSpan.MinValue;

    #endregion

}

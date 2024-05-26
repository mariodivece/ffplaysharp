namespace FFmpeg;

/// <summary>
/// Represents a delegate that is called continuously by <see cref="FFFormatContext"/>.
/// </summary>
/// <param name="blokingObject">
/// During blocking operations, 
/// this arguument is called with an opaque non-null pointer reference.
/// </param>
/// <returns>
/// Returns 0 to continue without error.
/// Anything other than 0 interrupts the blocking function
/// and makes such function refurn <see cref="ffmpeg.AVERROR_EXIT"/>.
/// </returns>
public delegate int FormatContextInterruptCallback(INativeReference blokingObject);

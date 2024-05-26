namespace Unosquare.FFplaySharp.Interop;

public static unsafe class InteropExtensions
{
    /// <summary>
    /// Tests if a <see cref="INativeReference"/> is null
    /// or contains a null (zero) address reference.
    /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis"/>
    /// </summary>
    /// <typeparam name="T">Generic type parameter.</typeparam>
    /// <param name="reference">The reference to inspect.</param>
    /// <returns>
    /// True if either the reference or its address are null or zero, false if not.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVoid<T>([NotNullWhen(false)] this T? reference) where T : INativeReference =>
        reference is null || reference.IsEmpty;

    /// <summary>
    /// Tests if a <see cref="INativeReference"/> is not null and points to a valid, non-zero address.
    /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis"/>
    /// </summary>
    /// <typeparam name="T">Generic type parameter.</typeparam>
    /// <param name="reference">The reference to inspect.</param>
    /// <returns>
    /// False if either the reference or its address are null or zero, false if not.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValid<T>([NotNullWhen(true)] this T? reference) where T : INativeReference =>
        reference is not null && !reference.IsEmpty;

    public static T* AllocateNativeMemory<T>() where T : unmanaged =>
        (T*)ffmpeg.av_mallocz((ulong)sizeof(T));

    public static T* AllocateNativeMemory<T>(ulong elementCount) where T : unmanaged =>
        (T*)ffmpeg.av_mallocz((ulong)sizeof(T) * elementCount);

    public static void FreeNativeMemory(void* target)
    {
        if (target is null)
            return;

        ffmpeg.av_free(target);
    }
}

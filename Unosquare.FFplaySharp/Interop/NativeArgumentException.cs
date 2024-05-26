namespace Unosquare.FFplaySharp.Interop;

public class NativeArgumentException : ArgumentNullException
{
    public NativeArgumentException() : base() { 
    
    }

    /// <summary>Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
    /// <param name="argument">The reference type argument to validate as non-null.</param>
    /// <param name="paramName">The name of the parameter with which <paramref name="argument"/> corresponds.</param>
    public static void ThrowIfNull([NotNull] INativeReference? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
            Throw(paramName);
    }

    public static void ThrowIfNullOrEmpty([NotNull] INativeReference? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument.IsVoid())
            Throw(paramName);
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) =>
    throw new ArgumentNullException(paramName);
}

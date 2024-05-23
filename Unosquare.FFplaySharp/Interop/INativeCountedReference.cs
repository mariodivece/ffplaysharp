namespace Unosquare.FFplaySharp.Interop;

/// <summary>
/// Interface for a <see cref="INativeReference"/> that is
/// registered in the <see cref="ReferenceCounter"/>.
/// </summary>
public interface INativeCountedReference : INativeReference, IDisposable
{
    /// <summary>
    /// Gets the unique identifier of this object within the <see cref="ReferenceCounter"/>.
    /// </summary>
    ulong ObjectId { get; }
}

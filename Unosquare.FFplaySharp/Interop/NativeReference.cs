namespace Unosquare.FFplaySharp.Interop;


/// <summary>
/// A base implementation of an object that wraps a
/// strongly typed native reference.
/// </summary>
/// <typeparam name="T">Generic type parameter.</typeparam>
public abstract unsafe class NativeReference<T> : INativeReference<T>
    where T : unmanaged
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeReference{T}"/> class.
    /// </summary>
    /// <param name="target">The target pointer to wrap.</param>
    protected NativeReference(T* target)
    {
        UpdatePointer(target);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeReference{T}"/> class.
    /// </summary>
    protected NativeReference()
    {
        // placeholder
    }

    /// <inheritdoc/>
    public nint Address { get; protected set; } = nint.Zero;

    /// <inheritdoc/>
    public T* Reference => (T*)Address;

    /// <inheritdoc/>
    public T? Dereference() => Address == nint.Zero ? default : *Reference;

    /// <inheritdoc/>
    public bool IsEmpty => Address == nint.Zero;

    /// <inheritdoc/>
    public void UpdatePointer(nint address) =>
        Address = address;

    /// <inheritdoc/>
    public void UpdatePointer(T* target) => Address = target is null
        ? nint.Zero
        : new(target);

    /// <inheritdoc/>
    public void ClearPointer() => Address = nint.Zero;

    public static implicit operator T*(NativeReference<T>? reference) =>
        reference is null ? default : reference.Reference;

}

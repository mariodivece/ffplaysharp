namespace Unosquare.FFplaySharp.Interop;


/// <summary>
/// A base implementation of an object that wraps a
/// strongly typed native reference.
/// </summary>
/// <typeparam name="T">Generic type parameter.</typeparam>
public abstract unsafe class NativeReference<T> : INativeReference<T>
    where T : unmanaged
{
    private nint _Address = nint.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeReference{T}"/> class.
    /// </summary>
    /// <param name="target">The target pointer to wrap.</param>
    protected NativeReference(T* target)
    {
        var targetAddress = target is null ? nint.Zero : new(target);
        Interlocked.Exchange(ref _Address, targetAddress);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeReference{T}"/> class.
    /// </summary>
    protected NativeReference()
    {
        // placeholder
    }

    /// <inheritdoc/>
    public virtual nint Address
    {
        get => Interlocked.CompareExchange(ref _Address, 0, 0);
        private set => Interlocked.Exchange(ref _Address, value);
    }

    /// <inheritdoc/>
    public virtual T* Reference => (T*)Address;

    /// <inheritdoc/>
    public virtual bool IsEmpty => Address == nint.Zero;

    /// <inheritdoc/>
    public virtual T? Dereference() => Address == nint.Zero ? default : *Reference;

    public virtual FixedDoublePointer<T> AsDoublePointer() => new(this);

    /// <inheritdoc/>
    public virtual void UpdatePointer(nint address) =>
        Address = address;

    /// <inheritdoc/>
    public virtual void UpdatePointer(T* target) => Address = target is null
        ? nint.Zero
        : new(target);

    /// <inheritdoc/>
    public virtual void ClearPointer() => Address = nint.Zero;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is INativeReference r && Address == r.Address;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        Interlocked.CompareExchange(ref _Address, 0, 0).GetHashCode();

    /// <summary>
    /// Implicit cast that converts the given <see cref="NativeReference{T}"/> to a T*.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns>
    /// The result of the operation.
    /// </returns>
    public static implicit operator T*(NativeReference<T>? reference) =>
        reference is null ? default : reference.Reference;

}

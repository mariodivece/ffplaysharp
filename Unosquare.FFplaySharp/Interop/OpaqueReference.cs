namespace Unosquare.FFplaySharp.Interop;

public unsafe class OpaqueReference : INativeReference
{
    private nint _Address = nint.Zero;

    public OpaqueReference(void* target)
    {
        var targetAddress = target is null ? nint.Zero : new(target);
        Interlocked.Exchange(ref _Address, targetAddress);
    }

    /// <inheritdoc/>
    public virtual nint Address
    {
        get => Interlocked.CompareExchange(ref _Address, 0, 0);
        protected set => Interlocked.Exchange(ref _Address, value);
    }

    /// <inheritdoc/>
    public virtual bool IsEmpty => Address == nint.Zero;
}

namespace Unosquare.FFplaySharp.Primitives;


public abstract unsafe class NativeReference<T> : INativeReference<T>
    where T : unmanaged
{
    protected NativeReference(T* target)
    {
        Update(target);
    }

    protected NativeReference()
    {
        // placeholder
    }

    public IntPtr Address { get; protected set; } = IntPtr.Zero;

    public T* Target => (T*)Address;

    public T Value => Address.IsNull() ? default : *Target;

    public void Update(IntPtr address) => Address = address;

    public void Update(T* target) => Address = target is null
        ? IntPtr.Zero
        : new(target);
}

namespace Unosquare.FFplaySharp.Primitives;


public interface INativeReference
{
    IntPtr Address { get; }

    void Update(IntPtr address);
}

public interface INativeCountedReference : INativeReference
{
    ulong ObjectId { get; }

    void Release();
}

public unsafe interface IUnmanagedReference<T> : INativeReference
    where T : unmanaged
{
    T* Pointer { get; }

    void Update(T* pointer);

    T PointerValue { get; }
}

public abstract unsafe class UnmanagedReference<T> : IUnmanagedReference<T>
    where T : unmanaged
{
    protected UnmanagedReference(T* pointer)
    {
        Update(pointer);
    }

    protected UnmanagedReference()
    {
        // placeholder
    }

    public IntPtr Address { get; protected set; } = IntPtr.Zero;

    public T* Pointer => (T*)Address;

    public T PointerValue => Address.IsNull() ? default : *Pointer;

    public void Update(IntPtr address) => Address = address;

    public void Update(T* pointer) => Address = new(pointer);
}

public abstract unsafe class UnmanagedCountedReference<T> : UnmanagedReference<T>, INativeCountedReference
    where T : unmanaged
{
    protected UnmanagedCountedReference(string? filePath, int? lineNumber)
        : base()
    {
        filePath ??= "(No file)";
        lineNumber ??= 0;
        Source = $"{Path.GetFileName(filePath)}: {lineNumber}";
        ObjectId = ReferenceCounter.Add(this, Source);
    }

    public ulong ObjectId { get; protected set; }

    protected string Source { get; }

    public void Release()
    {
        if (!Address.IsNull())
            ReleaseInternal(Pointer);

        Update(IntPtr.Zero);
        ReferenceCounter.Remove(this);
    }

    protected abstract void ReleaseInternal(T* pointer);
}

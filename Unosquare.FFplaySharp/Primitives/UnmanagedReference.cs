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

public unsafe interface INativeReference<T> : INativeReference
    where T : unmanaged
{
    T* Target { get; }

    void Update(T* target);

    T Value { get; }
}

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

public abstract unsafe class CountedReference<T> : NativeReference<T>, INativeCountedReference
    where T : unmanaged
{
    protected CountedReference(string? filePath, int? lineNumber)
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
        if (Address.IsNotNull())
            ReleaseInternal(Target);

        Update(IntPtr.Zero);
        ReferenceCounter.Remove(this);
    }

    protected abstract void ReleaseInternal(T* target);
}

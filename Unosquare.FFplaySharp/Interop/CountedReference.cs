namespace Unosquare.FFplaySharp.Interop;

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
        if (!IsEmpty)
            ReleaseInternal(this);

        ClearPointer();
        ReferenceCounter.Remove(this);
    }

    protected abstract void ReleaseInternal(T* target);
}

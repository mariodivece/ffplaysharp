namespace Unosquare.FFplaySharp.Interop;

public abstract unsafe class CountedReference<T> : NativeReference<T>, INativeCountedReference
    where T : unmanaged
{
    private long _IsDisposed;

    protected CountedReference(string? filePath, int? lineNumber)
        : base()
    {
        filePath ??= "(No file)";
        lineNumber ??= 0;
        Source = $"{Path.GetFileName(filePath)}: {lineNumber}";
        ObjectId = ReferenceCounter.Add(this, Source);
    }

    /// <inheritdoc/>
    public ulong ObjectId { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether this instance is disposed.
    /// </summary>
    public bool IsDisposed => Interlocked.Read(ref _IsDisposed) > 0;

    protected string Source { get; }

    /// <inheritdoc/>
    public override T? Dereference()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return base.Dereference();
    }

    /// <inheritdoc/>
    public override void UpdatePointer(nint address)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        base.UpdatePointer(address);
    }

    /// <inheritdoc/>
    public override unsafe void UpdatePointer(T* target)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        base.UpdatePointer(target);
    }

    /// <inheritdoc/>
    public override unsafe T* Reference
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return base.Reference;
        }
    }

    /// <inheritdoc/>
    public override nint Address
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return base.Address;
        }
    }

    /// <inheritdoc/>
    public override bool IsEmpty =>
        IsDisposed || Address == nint.Zero;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(alsoManaged: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="CountedReference{T}"/> class.
    /// </summary>
    ~CountedReference()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(alsoManaged: false);
    }

    /// <summary>
    /// Releases native or unmanaged resources.
    /// There is no need to call <see cref="INativeReference.ClearPointer"/>
    /// or <see cref="Dispose()"/>. These are called automatically.
    /// </summary>
    /// <param name="target">The non-null pointer to be released.</param>
    protected abstract void ReleaseNative(T* target);

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="CountedReference{T}"/> and optionally
    /// releases the managed resources.
    /// </summary>
    /// <param name="alsoManaged">True if also managed.</param>
    protected virtual void Dispose(bool alsoManaged)
    {
        if (Interlocked.Add(ref _IsDisposed, 1) > 1)
            return;

        if (alsoManaged)
        {
            // TODO: dispose managed state (managed objects)
        }

        // Free unmanaged resources (unmanaged objects)
        if (!IsEmpty)
            ReleaseNative(this);

        ClearPointer();
        ReferenceCounter.Remove(this);

        // TODO: set large fields to null
    }

}

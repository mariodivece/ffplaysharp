﻿namespace Unosquare.FFplaySharp.Interop;

public abstract unsafe class CountedReference<T> : NativeReference<T>, INativeCountedReference
    where T : unmanaged
{
    private long _IsDisposed;

    protected CountedReference(T* target, string? filePath, int? lineNumber)
        : base(target)
    {
        filePath ??= "(No file)";
        lineNumber ??= 0;
        Source = $"{Path.GetFileName(filePath)}: {lineNumber}";
        ObjectId = ReferenceCounter.Add(this, Source);
    }

    protected CountedReference(string? filePath, int? lineNumber)
        : this(null, filePath, lineNumber)
    {
        // placeholder
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
    public override T* Reference => IsDisposed ? null : base.Reference;

    /// <inheritdoc/>
    public override nint Address => IsDisposed ? nint.Zero : base.Address;

    /// <inheritdoc/>
    public override bool IsEmpty => Address == nint.Zero;

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
    protected abstract void DisposeNative(T* target);

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="CountedReference{T}"/> and optionally
    /// releases the managed resources.
    /// </summary>
    /// <param name="alsoManaged">True if also managed.</param>
    protected virtual void Dispose(bool alsoManaged)
    {
        var address = Address;
        if (Interlocked.Add(ref _IsDisposed, 1) > 1)
            return;

        if (alsoManaged)
        {
            // TODO: dispose managed state (managed objects)
        }

        // Free unmanaged resources (unmanaged objects)
        if (address != nint.Zero)
            DisposeNative((T*)address);

        Address = nint.Zero;
        ReferenceCounter.Remove(this);

        // TODO: set large fields to null
    }

}

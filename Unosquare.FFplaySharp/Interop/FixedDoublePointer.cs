namespace Unosquare.FFplaySharp.Interop;

public unsafe struct FixedDoublePointer<T> : IDisposable
    where T : unmanaged
{
    private static readonly nuint DoublePointerSize = (nuint)sizeof(nint);
    private long m_IsDisposed;
    private INativeReference<T>? m_NativeReference;
    private T** m_StorageAddress;

    internal FixedDoublePointer(INativeReference<T> reference)
    {
        m_NativeReference = reference;
        m_StorageAddress = (T**)NativeMemory.AllocZeroed(DoublePointerSize);
        var referencePointer = m_NativeReference.Reference;
        var pointerAddress = &referencePointer;
        NativeMemory.Copy(pointerAddress, m_StorageAddress, DoublePointerSize);        
    }

    public void Dispose()
    {
        if (Interlocked.Add(ref m_IsDisposed, 1) > 1)
            return;

        m_NativeReference?.UpdatePointer(*m_StorageAddress);
        NativeMemory.Free(m_StorageAddress);
        m_StorageAddress = null;
        m_NativeReference = null;
    }

    public static implicit operator T**(FixedDoublePointer<T> fixedPointer) => fixedPointer.m_StorageAddress;
}

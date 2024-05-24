namespace Unosquare.FFplaySharp.Interop;

public unsafe struct DoublePointer<T> : IDisposable
    where T : unmanaged
{
    private static readonly nuint DoublePointerSize = (nuint)sizeof(nint);
    private long m_IsDisposed;
    private IUpdateableReference<T>? m_NativeReference;
    private T** m_StorageAddress;

    internal DoublePointer(IUpdateableReference<T> reference)
    {
        m_NativeReference = reference;
        m_StorageAddress = (T**)InteropExtensions.AllocateNativeMemory<byte>(DoublePointerSize);
        var referencePointer = m_NativeReference.Reference;
        var pointerAddress = &referencePointer;
        NativeMemory.Copy(pointerAddress, m_StorageAddress, DoublePointerSize);
    }

    public void Dispose()
    {
        if (Interlocked.Add(ref m_IsDisposed, 1) > 1)
            return;

        m_NativeReference?.UpdatePointer(*m_StorageAddress);
        InteropExtensions.FreeNativeMemory(m_StorageAddress);
        m_StorageAddress = null;
        m_NativeReference = null;
    }

    public static implicit operator T**(DoublePointer<T> fixedPointer) => fixedPointer.m_StorageAddress;
}

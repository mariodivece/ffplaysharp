namespace Unosquare.FFplaySharp.Primitives;


public interface INativeReference
{
    IntPtr Address { get; }

    void Update(IntPtr address);
}

public unsafe interface INativeReference<T> : INativeReference
    where T : unmanaged
{
    T* Target { get; }

    void Update(T* target);

    T Value { get; }
}

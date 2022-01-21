namespace Unosquare.FFplaySharp.Primitives;

public interface INativeCountedReference : INativeReference
{
    ulong ObjectId { get; }

    void Release();
}

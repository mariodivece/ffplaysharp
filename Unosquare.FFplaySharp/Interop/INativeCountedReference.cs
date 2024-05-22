namespace Unosquare.FFplaySharp.Interop;

public interface INativeCountedReference : INativeReference
{
    ulong ObjectId { get; }

    void Release();
}

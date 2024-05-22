using Unosquare.FFplaySharp.Interop;

namespace Unosquare.FFplaySharp.Primitives;

public abstract unsafe class NativeChildSet<TParent, TChild> : IReadOnlyList<TChild>
    where TParent : INativeReference
    where TChild : INativeReference
{
    protected NativeChildSet(TParent parent)
    {
        Parent = parent;
    }

    public abstract TChild this[int index] { get; set; }

    public TParent Parent { get; }

    public abstract int Count { get; }

    public IEnumerator<TChild> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    /// <summary>
    /// Port of FFSWAP
    /// </summary>
    /// <param name="indexA">The first item index to swap.</param>
    /// <param name="indexB">The second item index to swap.</param>
    public void Swap(int indexA, int indexB) =>
        (this[indexA], this[indexB]) = (this[indexB], this[indexA]);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

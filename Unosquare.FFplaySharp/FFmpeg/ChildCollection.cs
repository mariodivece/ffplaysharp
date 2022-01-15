namespace FFmpeg;

public abstract unsafe class ChildCollection<TParent, TChild> : IReadOnlyList<TChild>
    where TParent : IUnmanagedReference
    where TChild : IUnmanagedReference
{
    protected ChildCollection(TParent parent)
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

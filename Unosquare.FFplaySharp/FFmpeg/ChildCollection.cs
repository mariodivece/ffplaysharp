namespace FFmpeg
{
    using System.Collections;
    using System.Collections.Generic;
    using Unosquare.FFplaySharp.Primitives;

    public abstract unsafe class ChildCollection<P, T> : IReadOnlyList<T>
        where P : IUnmanagedReference
        where T : IUnmanagedReference
    {
        public ChildCollection(P parent)
        {
            Parent = parent;
        }

        public abstract T this[int index]
        {
            get;
            set;
        }

        public P Parent { get; }

        public abstract int Count { get; }

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return this[i];
        }

        /// <summary>
        /// Port of FFSWAP
        /// </summary>
        /// <param name="indexA"></param>
        /// <param name="indexB"></param>
        public void Swap(int indexA, int indexB)
        {
            var tempItem = this[indexB];
            this[indexB] = this[indexA];
            this[indexA] = tempItem;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

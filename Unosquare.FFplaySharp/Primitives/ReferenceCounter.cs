namespace Unosquare.FFplaySharp.Primitives
{
    using System.Collections.Generic;
    using System.Diagnostics;

    public static class ReferenceCounter
    {
        private static readonly object SyncLock = new();
        private static readonly SortedDictionary<ulong, (IUnmanagedReference obj, string source)> Graph = new();
        private static ulong LastObjectId = 0;
        private static ulong m_Count = 0;

        public static ulong Add(IUnmanagedReference item, string source)
        {
            lock (SyncLock)
            {
                var objectId = LastObjectId;
                Graph.Add(LastObjectId, (item, source));
                LastObjectId++;
                m_Count++;
                return objectId;
            }
        }

        public static void Remove(IUnmanagedReference item)
        {
            lock (SyncLock)
            {
                Graph.Remove(item.ObjectId);
                m_Count--;
            }
        }

        public static ulong Count
        {
            get
            {
                lock (SyncLock)
                {
                    return m_Count;
                }
            }
        }

        public static void VeirfyZero()
        {
            if (Count != 0)
            {
                Debug.Assert(Count == 0);
            }
        }
    }
}

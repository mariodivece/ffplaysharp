namespace Unosquare.FFplaySharp.Primitives;

public static class ReferenceCounter
{
    private static readonly object SyncLock = new();
    private static readonly SortedDictionary<ulong, (INativeCountedReference obj, string source)> Graph = new();
    private static ulong LastObjectId = 0;
    private static ulong m_Count = 0;

    public static ulong Add(INativeCountedReference item, string source)
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

    public static void Remove(INativeCountedReference item)
    {
        lock (SyncLock)
        {
            if (!Graph.ContainsKey(item.ObjectId))
                return;

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

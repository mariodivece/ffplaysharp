using System;
using System.Threading;

namespace Unosquare.FFplaySharp.Avalonia;

internal sealed class BusyLocker
{
    private long m_IsBusy = 0;

    public IDisposable TryEnter(out bool entered)
    {
        entered = Interlocked.Increment(ref m_IsBusy) <= 1;
        return new Releaser(this, entered);
    }

    private void Release() => Interlocked.Exchange(ref m_IsBusy, 0);

    private readonly struct Releaser : IDisposable
    {
        private readonly bool Acquired;
        private readonly BusyLocker Locker;

        public Releaser(BusyLocker locker, bool acquired)
        {
            Acquired = acquired;
            Locker = locker;
        }

        public void Dispose()
        {
            if (Acquired)
                Locker.Release();
        }
    }
}
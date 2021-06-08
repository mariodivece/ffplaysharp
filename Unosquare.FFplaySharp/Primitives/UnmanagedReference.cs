namespace Unosquare.FFplaySharp.Primitives
{
    using System;
    using System.IO;

    public interface IUnmanagedReference
    {
        ulong ObjectId { get; }

        IntPtr Address { get; }

        void Release();
    }

    public abstract unsafe class UnmanagedReference<T> : IUnmanagedReference
        where T : unmanaged
    {
        protected UnmanagedReference(string filePath, int lineNumber)
        {
            ObjectId = ReferenceCounter.Add(this,
                $"{Path.GetFileName(filePath)}: {lineNumber}");
        }

        public ulong ObjectId { get; }

        public T Value => *Pointer;

        public IntPtr Address { get; protected set; }

        public T* Pointer => (T*)Address;

        public void Update(IntPtr address) => Address = address;

        public void Update(T* pointer) => Address = new IntPtr(pointer);

        public void Release()
        {
            if (Address == IntPtr.Zero)
                return;

            ReleaseInternal(Pointer);
            Update(IntPtr.Zero);
            ReferenceCounter.Remove(this);
        }

        protected abstract void ReleaseInternal(T* pointer);
    }
}

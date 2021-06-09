namespace Unosquare.FFplaySharp.Primitives
{
    using System;
    using System.IO;

    public interface IUnmanagedReference
    {
        IntPtr Address { get; }

        bool IsNull { get; }

        void Update(IntPtr address);
    }

    public interface IUnmanagedCountedReference : IUnmanagedReference
    {
        ulong ObjectId { get; }

        void Release();
    }

    public abstract unsafe class UnmanagedReference<T> : IUnmanagedReference
        where T : unmanaged
    {
        protected UnmanagedReference(T* pointer)
        {
            Update(pointer);
        }

        protected UnmanagedReference()
        {
            // placeholder
        }

        public bool IsNull => Address == IntPtr.Zero;

        public IntPtr Address { get; protected set; } = IntPtr.Zero;

        public T* Pointer => (T*)Address;

        public void Update(IntPtr address) => Address = address;

        public void Update(T* pointer) => Address = new IntPtr(pointer);
    }

    public abstract unsafe class UnmanagedCountedReference<T> : UnmanagedReference<T>, IUnmanagedCountedReference
        where T : unmanaged
    {
        protected UnmanagedCountedReference(string filePath, int lineNumber)
            : base()
        {
            Source = $"{Path.GetFileName(filePath)}: {lineNumber}";
            ObjectId = ReferenceCounter.Add(this, Source);
        }

        public ulong ObjectId { get; protected set; }

        public T Value => *Pointer;

        protected string Source { get; }

        public void Release()
        {
            if (Address != IntPtr.Zero)
                ReleaseInternal(Pointer);

            Update(IntPtr.Zero);
            ReferenceCounter.Remove(this);
        }

        protected abstract void ReleaseInternal(T* pointer);
    }
}

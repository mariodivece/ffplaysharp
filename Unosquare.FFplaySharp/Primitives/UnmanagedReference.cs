namespace Unosquare.FFplaySharp.Primitives
{
    using System;

    public abstract unsafe class UnmanagedReference<T>
        where T : unmanaged
    {
        protected UnmanagedReference()
        {
            ObjectId = ReferenceCounter.Add(this);
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

    public abstract unsafe class AllocatableReference<T> : UnmanagedReference<T>
        where T : unmanaged
    {
        protected AllocatableReference()
            : base()
        {
            Update(Allocate());
        }

        protected abstract T* Allocate();
    }
}

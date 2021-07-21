namespace Unosquare.FFplaySharp.Primitives
{
    using System;
    using System.IO;

    public interface IUnmanagedReference : IEquatable<IUnmanagedReference>
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

    public unsafe interface IUnmanagedReference<T> : IUnmanagedReference
        where T : unmanaged
    {
        T* Pointer { get; }

        void Update(T* pointer);

        T PointerValue { get; }
    }

    public abstract unsafe class UnmanagedReference<T> : IUnmanagedReference<T>
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

        public static bool operator ==(UnmanagedReference<T> a, object b)
        {
            var addressA = a?.Address ?? default;
            var addressB = (b as IUnmanagedReference)?.Address ?? default;

            return addressA == addressB;
        }

        public static bool operator !=(UnmanagedReference<T> a, object b) => !(a == b);

        public bool IsNull => Address.IsNull();

        public IntPtr Address { get; protected set; } = IntPtr.Zero;

        public T* Pointer => (T*)Address;

        public T PointerValue => Address.IsNull() ? default : *Pointer;

        public void Update(IntPtr address) => Address = address;

        public void Update(T* pointer) => Address = new IntPtr(pointer);

        public bool Equals(IUnmanagedReference other) => other == this;

        public override bool Equals(object obj) => Equals(obj as IUnmanagedReference);

        public override int GetHashCode() => Address.GetHashCode();
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

        protected string Source { get; }

        public void Release()
        {
            if (!Address.IsNull())
                ReleaseInternal(Pointer);

            Update(IntPtr.Zero);
            ReferenceCounter.Remove(this);
        }

        protected abstract void ReleaseInternal(T* pointer);
    }
}

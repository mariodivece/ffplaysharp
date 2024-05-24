namespace Unosquare.FFplaySharp.Interop;

/// <summary>
/// Interface that defines the members required
/// to wrap a native object reference.
/// </summary>
public interface INativeReference
{
    /// <summary>
    /// Gets the address (<see cref="nint"/>) of the wrapped native reference.
    /// </summary>
    nint Address { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Address"/>
    /// points to <see cref="nint.Zero"/>
    /// </summary>
    bool IsEmpty { get; }
}

/// <summary>
/// Interface that defines a typed, native object reference wrapper.
/// There is no inference whether memory allocation occurs
/// in managed or unmanaged spece, so be catious when allocating,
/// updating pointers or releasing memory.
/// </summary>
/// <typeparam name="T">Generic type parameter.</typeparam>
public unsafe interface INativeReference<T> : INativeReference
    where T : unmanaged
{
    /// <summary>
    /// Gets the native, strongly typed pointer to the wrapped object.
    /// The return pointer may be null.
    /// </summary>
    T* Reference { get; }

    /// <summary>
    /// Gets the size in bytes of the wrapped data structure.
    /// </summary>
    int StructureSize { get; }

    /// <summary>
    /// Dereferences the pointer and copies its data by value.
    /// If the wrapped object <see cref="INativeReference.IsEmpty"/>,
    /// a default value of <see cref="T?"/> (null) is returned.
    /// </summary>
    T? Dereference();

    /// <summary>
    /// Obtains a <see cref="DoublePointer{T}"/> object
    /// for the purpose of updating the underlying <see cref="Reference"/>
    /// typically outside of managed code. Always call <see cref="IDisposable.Dispose"/>
    /// method to update the undelying <see cref="Reference"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="DoublePointer{T}"/> that updates this <see cref="INativeReference{T}"/>
    /// upon disposal.
    /// </returns>
    DoublePointer<T> AsDoublePointer();
}

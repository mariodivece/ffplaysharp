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
    /// Updates the memory location stored in the <see cref="Address">.
    /// </summary>
    /// <param name="address">The address (<see cref="nint"/>) of
    ///                       the wrapped native reference.</param>
    void UpdatePointer(nint address);

    /// <summary>
    /// Sets the memory location to <see cref="nint.Zero"/>.
    /// This effectively makes <see cref="IsEmpty"/> true.
    /// </summary>
    void ClearPointer();

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
    /// Updates the reference stored in <see cref="Reference"/>.
    /// Conversely, the corresponding <see cref="INativeReference.Address"/>
    /// is also set to <see cref="nint.Zero"/>.
    /// </summary>
    /// <param name="target">The typed pointer.</param>
    void UpdatePointer(T* target);

    /// <summary>
    /// Dereferences the pointer and copies its data by value.
    /// If the wrapped object <see cref="INativeReference.IsEmpty"/>,
    /// a default value of <see cref="T?"/> (null) is returned.
    /// </summary>
    T? Dereference();
}

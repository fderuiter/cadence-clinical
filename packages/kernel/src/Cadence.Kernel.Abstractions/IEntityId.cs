using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Cadence.Kernel;

/// <summary>
/// Non-generic base contract for strongly-typed clinical kernel entity identifiers wrapping UUIDv7.
/// </summary>
public interface IEntityId : IComparable
{
    /// <summary>
    /// Gets the underlying Guid representation (UUIDv7).
    /// </summary>
    Guid Value { get; }

    /// <summary>
    /// Gets a value indicating whether this identifier is empty.
    /// </summary>
    bool IsEmpty { get; }
}

/// <summary>
/// Generic contract for strongly-typed clinical kernel entity identifiers wrapping UUIDv7.
/// </summary>
public interface IEntityId<TSelf> : IEntityId,
    IComparable<TSelf>,
    IEquatable<TSelf>,
    ISpanParsable<TSelf>,
    IFormattable,
    IComparisonOperators<TSelf, TSelf, bool>
    where TSelf : struct, IEntityId<TSelf>
{
    /// <summary>
    /// Creates a new identifier with a cryptographically ordered UUIDv7 at current UTC time.
    /// </summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "New() is the established factory name on every kernel ID; the interface is static abstract and is only implemented in C#, so the VB keyword clash does not arise.")]
    static abstract TSelf New();

    /// <summary>
    /// Creates a new identifier with a cryptographically ordered UUIDv7 at the specified timestamp.
    /// </summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "New() is the established factory name on every kernel ID; the interface is static abstract and is only implemented in C#, so the VB keyword clash does not arise.")]
    static abstract TSelf New(DateTimeOffset timestamp);

    /// <summary>
    /// Wraps an existing Guid into the strongly-typed identifier.
    /// </summary>
    static abstract TSelf FromGuid(Guid value);

    /// <summary>
    /// Gets an empty identifier.
    /// </summary>
    static abstract TSelf Empty { get; }

    /// <summary>
    /// Parses a string into a strongly-typed identifier using default format provider.
    /// </summary>
    static abstract bool TryParse([NotNullWhen(true)] string? s, out TSelf result);

    /// <summary>
    /// Parses a character span into a strongly-typed identifier using default format provider.
    /// </summary>
    static abstract bool TryParse(ReadOnlySpan<char> s, out TSelf result);
}

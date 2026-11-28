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
    static abstract TSelf New();

    /// <summary>
    /// Creates a new identifier with a cryptographically ordered UUIDv7 at the specified timestamp.
    /// </summary>
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

/// <summary>
/// Shared comparison and UUID parsing operations for <see cref="IEntityId{TSelf}"/> value types.
/// </summary>
public static class EntityIdOperations
{
    /// <summary>
    /// Compares <paramref name="value"/> against an untyped object according to <see cref="IComparable.CompareTo"/>.
    /// </summary>
    public static int CompareTo<TId>(TId value, object? obj)
        where TId : struct, IEntityId<TId> =>
        obj switch
        {
            null => 1,
            TId other => value.Value.CompareTo(other.Value),
            _ => throw new ArgumentException($"Object must be of type {typeof(TId).Name}", nameof(obj)),
        };

    /// <summary>
    /// Parses a string representation of a UUID into <typeparamref name="TId"/>.
    /// </summary>
    public static TId Parse<TId>(string s, IFormatProvider? provider = null)
        where TId : struct, IEntityId<TId>
    {
        ArgumentNullException.ThrowIfNull(s);
        return TId.FromGuid(Guid.Parse(s, provider));
    }

    /// <summary>
    /// Parses a character span representation of a UUID into <typeparamref name="TId"/>.
    /// </summary>
    public static TId Parse<TId>(ReadOnlySpan<char> s, IFormatProvider? provider = null)
        where TId : struct, IEntityId<TId> =>
        TId.FromGuid(Guid.Parse(s, provider));

    /// <summary>
    /// Attempts to parse a string representation of a UUID into <typeparamref name="TId"/>.
    /// </summary>
    public static bool TryParse<TId>([NotNullWhen(true)] string? s, IFormatProvider? provider, out TId result)
        where TId : struct, IEntityId<TId>
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = TId.FromGuid(guid);
            return true;
        }

        result = TId.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to parse a character span representation of a UUID into <typeparamref name="TId"/>.
    /// </summary>
    public static bool TryParse<TId>(ReadOnlySpan<char> s, IFormatProvider? provider, out TId result)
        where TId : struct, IEntityId<TId>
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = TId.FromGuid(guid);
            return true;
        }

        result = TId.Empty;
        return false;
    }
}

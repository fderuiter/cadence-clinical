using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed identifier for an Assertion (spec §8.2): a statement by an actor with authority about an observation,
/// another assertion or a lock scope, such as Verified, Queried or Locked (ADR-2621).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<AssertionId>))]
[JsonConverter(typeof(EntityIdJsonConverter<AssertionId>))]
public readonly record struct AssertionId(Guid Value) : IEntityId<AssertionId>
{
    public static AssertionId New() => new(Guid.CreateVersion7());

    public static AssertionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static AssertionId FromGuid(Guid value) => new(value);

    public static AssertionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(AssertionId id) => id.Value;

    public static explicit operator AssertionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(AssertionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(AssertionId left, AssertionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(AssertionId left, AssertionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(AssertionId left, AssertionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(AssertionId left, AssertionId right) => left.CompareTo(right) >= 0;

    public static AssertionId Parse(string s) => EntityIdOperations.Parse<AssertionId>(s);

    public static AssertionId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<AssertionId>(s, provider);

    public static AssertionId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<AssertionId>(s);

    public static AssertionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<AssertionId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out AssertionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out AssertionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AssertionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AssertionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

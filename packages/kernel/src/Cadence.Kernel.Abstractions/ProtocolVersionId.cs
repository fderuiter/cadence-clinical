using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an immutable ProtocolVersion snapshot in the Clinical Kernel.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ProtocolVersionId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ProtocolVersionId>))]
public readonly record struct ProtocolVersionId(Guid Value) : IEntityId<ProtocolVersionId>
{
    public static ProtocolVersionId New() => new(Guid.CreateVersion7());

    public static ProtocolVersionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ProtocolVersionId FromGuid(Guid value) => new(value);

    public static ProtocolVersionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ProtocolVersionId id) => id.Value;

    public static explicit operator ProtocolVersionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ProtocolVersionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ProtocolVersionId left, ProtocolVersionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProtocolVersionId left, ProtocolVersionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProtocolVersionId left, ProtocolVersionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProtocolVersionId left, ProtocolVersionId right) => left.CompareTo(right) >= 0;

    public static ProtocolVersionId Parse(string s) => EntityIdOperations.Parse<ProtocolVersionId>(s);

    public static ProtocolVersionId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ProtocolVersionId>(s, provider);

    public static ProtocolVersionId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ProtocolVersionId>(s);

    public static ProtocolVersionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ProtocolVersionId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ProtocolVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProtocolVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProtocolVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProtocolVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

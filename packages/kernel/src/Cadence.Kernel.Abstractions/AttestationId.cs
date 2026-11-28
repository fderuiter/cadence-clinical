using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Attestation (21 CFR Part 11 electronic signature ceremony and audit manifest).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<AttestationId>))]
[JsonConverter(typeof(EntityIdJsonConverter<AttestationId>))]
public readonly record struct AttestationId(Guid Value) : IEntityId<AttestationId>
{
    public static AttestationId New() => new(Guid.CreateVersion7());

    public static AttestationId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static AttestationId FromGuid(Guid value) => new(value);

    public static AttestationId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(AttestationId id) => id.Value;

    public static explicit operator AttestationId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(AttestationId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(AttestationId left, AttestationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(AttestationId left, AttestationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(AttestationId left, AttestationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(AttestationId left, AttestationId right) => left.CompareTo(right) >= 0;

    public static AttestationId Parse(string s) => EntityIdOperations.Parse<AttestationId>(s);

    public static AttestationId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<AttestationId>(s, provider);

    public static AttestationId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<AttestationId>(s);

    public static AttestationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<AttestationId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out AttestationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out AttestationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AttestationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AttestationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

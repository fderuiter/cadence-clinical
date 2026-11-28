using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Participant (pseudonymous clinical trial subject across EDC, eCOA, RTSM).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ParticipantId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ParticipantId>))]
public readonly record struct ParticipantId(Guid Value) : IEntityId<ParticipantId>
{
    public static ParticipantId New() => new(Guid.CreateVersion7());

    public static ParticipantId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ParticipantId FromGuid(Guid value) => new(value);

    public static ParticipantId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ParticipantId id) => id.Value;

    public static explicit operator ParticipantId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ParticipantId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ParticipantId left, ParticipantId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ParticipantId left, ParticipantId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ParticipantId left, ParticipantId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ParticipantId left, ParticipantId right) => left.CompareTo(right) >= 0;

    public static ParticipantId Parse(string s) => EntityIdOperations.Parse<ParticipantId>(s);

    public static ParticipantId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ParticipantId>(s, provider);

    public static ParticipantId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ParticipantId>(s);

    public static ParticipantId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ParticipantId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ParticipantId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ParticipantId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ParticipantId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ParticipantId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

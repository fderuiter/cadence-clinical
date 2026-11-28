using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Observation (clinical fact, form item data, lab result, ePRO answer).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ObservationId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ObservationId>))]
public readonly record struct ObservationId(Guid Value) : IEntityId<ObservationId>
{
    public static ObservationId New() => new(Guid.CreateVersion7());

    public static ObservationId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ObservationId FromGuid(Guid value) => new(value);

    public static ObservationId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ObservationId id) => id.Value;

    public static explicit operator ObservationId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ObservationId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ObservationId left, ObservationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ObservationId left, ObservationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ObservationId left, ObservationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ObservationId left, ObservationId right) => left.CompareTo(right) >= 0;

    public static ObservationId Parse(string s) => EntityIdOperations.Parse<ObservationId>(s);

    public static ObservationId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ObservationId>(s, provider);

    public static ObservationId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ObservationId>(s);

    public static ObservationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ObservationId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ObservationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ObservationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ObservationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ObservationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

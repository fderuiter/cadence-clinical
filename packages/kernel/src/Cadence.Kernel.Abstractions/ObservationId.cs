using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Observation (clinical fact, form item data, lab result, ePRO answer).
/// @req:PRD-SYS-053
/// </summary>
[TypeConverter(typeof(ObservationIdTypeConverter))]
[JsonConverter(typeof(ObservationIdJsonConverter))]
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

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ObservationId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ObservationId)}", nameof(obj))
    };

    public static bool operator <(ObservationId left, ObservationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ObservationId left, ObservationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ObservationId left, ObservationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ObservationId left, ObservationId right) => left.CompareTo(right) >= 0;

    public static ObservationId Parse(string s) => Parse(s, null);

    public static ObservationId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ObservationId(Guid.Parse(s, provider));
    }

    public static ObservationId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ObservationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ObservationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ObservationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ObservationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ObservationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ObservationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ObservationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ObservationIdTypeConverter : EntityIdTypeConverter<ObservationId>;

public sealed class ObservationIdJsonConverter : EntityIdJsonConverter<ObservationId>;

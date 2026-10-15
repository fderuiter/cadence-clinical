using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed identifier for the lock on one Observation (kernel Quality, ADR-2620). It carries the same UUID as
/// the observation it locks (<see cref="For"/>), so there is at most one lock per observation.
/// </summary>
[TypeConverter(typeof(ObservationLockIdTypeConverter))]
[JsonConverter(typeof(ObservationLockIdJsonConverter))]
public readonly record struct ObservationLockId(Guid Value) : IEntityId<ObservationLockId>
{
    public static ObservationLockId New() => new(Guid.CreateVersion7());

    public static ObservationLockId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    /// <summary>The lock of <paramref name="observation"/>.</summary>
    public static ObservationLockId For(ObservationId observation) => new(observation.Value);

    public static ObservationLockId FromGuid(Guid value) => new(value);

    public static ObservationLockId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ObservationLockId id) => id.Value;

    public static explicit operator ObservationLockId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ObservationLockId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ObservationLockId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ObservationLockId)}", nameof(obj))
    };

    public static bool operator <(ObservationLockId left, ObservationLockId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ObservationLockId left, ObservationLockId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ObservationLockId left, ObservationLockId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ObservationLockId left, ObservationLockId right) => left.CompareTo(right) >= 0;

    public static ObservationLockId Parse(string s) => Parse(s, null);

    public static ObservationLockId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ObservationLockId(Guid.Parse(s, provider));
    }

    public static ObservationLockId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ObservationLockId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ObservationLockId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ObservationLockId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ObservationLockId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ObservationLockId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ObservationLockId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ObservationLockId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ObservationLockIdTypeConverter : EntityIdTypeConverter<ObservationLockId>;

public sealed class ObservationLockIdJsonConverter : EntityIdJsonConverter<ObservationLockId>;

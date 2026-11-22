using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Applicability aggregate: which protocol version governs a participant's
/// enrollment over valid and recorded time (spec §7.5, K-TIME-6, K-TIME-8).
/// </summary>
[TypeConverter(typeof(ApplicabilityIdTypeConverter))]
[JsonConverter(typeof(ApplicabilityIdJsonConverter))]
public readonly record struct ApplicabilityId(Guid Value) : IEntityId<ApplicabilityId>
{
    public static ApplicabilityId New() => new(Guid.CreateVersion7());

    public static ApplicabilityId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ApplicabilityId FromGuid(Guid value) => new(value);

    public static ApplicabilityId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ApplicabilityId id) => id.Value;

    public static explicit operator ApplicabilityId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ApplicabilityId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ApplicabilityId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ApplicabilityId)}", nameof(obj))
    };

    public static bool operator <(ApplicabilityId left, ApplicabilityId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ApplicabilityId left, ApplicabilityId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ApplicabilityId left, ApplicabilityId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ApplicabilityId left, ApplicabilityId right) => left.CompareTo(right) >= 0;

    public static ApplicabilityId Parse(string s) => Parse(s, null);

    public static ApplicabilityId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ApplicabilityId(Guid.Parse(s, provider));
    }

    public static ApplicabilityId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ApplicabilityId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ApplicabilityId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ApplicabilityId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ApplicabilityId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ApplicabilityId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ApplicabilityId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ApplicabilityId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ApplicabilityIdTypeConverter : EntityIdTypeConverter<ApplicabilityId>;

public sealed class ApplicabilityIdJsonConverter : EntityIdJsonConverter<ApplicabilityId>;

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Enrollment: one participant's membership of one study site, with its
/// bitemporal status history (spec §7.4).
/// </summary>
[TypeConverter(typeof(EnrollmentIdTypeConverter))]
[JsonConverter(typeof(EnrollmentIdJsonConverter))]
public readonly record struct EnrollmentId(Guid Value) : IEntityId<EnrollmentId>
{
    public static EnrollmentId New() => new(Guid.CreateVersion7());

    public static EnrollmentId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static EnrollmentId FromGuid(Guid value) => new(value);

    public static EnrollmentId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(EnrollmentId id) => id.Value;

    public static explicit operator EnrollmentId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(EnrollmentId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        EnrollmentId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(EnrollmentId)}", nameof(obj))
    };

    public static bool operator <(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) < 0;
    public static bool operator <=(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) <= 0;
    public static bool operator >(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) > 0;
    public static bool operator >=(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) >= 0;

    public static EnrollmentId Parse(string s) => Parse(s, null);

    public static EnrollmentId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new EnrollmentId(Guid.Parse(s, provider));
    }

    public static EnrollmentId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static EnrollmentId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out EnrollmentId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out EnrollmentId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EnrollmentId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new EnrollmentId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out EnrollmentId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new EnrollmentId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class EnrollmentIdTypeConverter : EntityIdTypeConverter<EnrollmentId>;

public sealed class EnrollmentIdJsonConverter : EntityIdJsonConverter<EnrollmentId>;

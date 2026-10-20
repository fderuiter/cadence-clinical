using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Site (physical/operational trial location belonging to an Institution Organization).
/// </summary>
[TypeConverter(typeof(SiteIdTypeConverter))]
[JsonConverter(typeof(SiteIdJsonConverter))]
public readonly record struct SiteId(Guid Value) : IEntityId<SiteId>
{
    public static SiteId New() => new(Guid.CreateVersion7());

    public static SiteId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static SiteId FromGuid(Guid value) => new(value);

    public static SiteId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(SiteId id) => id.Value;

    public static explicit operator SiteId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(SiteId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        SiteId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(SiteId)}", nameof(obj))
    };

    public static bool operator <(SiteId left, SiteId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SiteId left, SiteId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SiteId left, SiteId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SiteId left, SiteId right) => left.CompareTo(right) >= 0;

    public static SiteId Parse(string s) => Parse(s, null);

    public static SiteId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new SiteId(Guid.Parse(s, provider));
    }

    public static SiteId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static SiteId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out SiteId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out SiteId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SiteId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new SiteId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SiteId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new SiteId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class SiteIdTypeConverter : EntityIdTypeConverter<SiteId>;

public sealed class SiteIdJsonConverter : EntityIdJsonConverter<SiteId>;

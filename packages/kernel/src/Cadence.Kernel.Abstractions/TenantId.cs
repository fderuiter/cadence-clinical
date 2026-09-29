using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Tenant (isolation and customer contractual boundary).
/// @req:PRD-SYS-053
/// </summary>
[TypeConverter(typeof(TenantIdTypeConverter))]
[JsonConverter(typeof(TenantIdJsonConverter))]
public readonly record struct TenantId(Guid Value) : IEntityId<TenantId>
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static TenantId FromGuid(Guid value) => new(value);

    public static TenantId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(TenantId id) => id.Value;

    public static explicit operator TenantId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(TenantId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        TenantId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(TenantId)}", nameof(obj))
    };

    public static bool operator <(TenantId left, TenantId right) => left.CompareTo(right) < 0;
    public static bool operator <=(TenantId left, TenantId right) => left.CompareTo(right) <= 0;
    public static bool operator >(TenantId left, TenantId right) => left.CompareTo(right) > 0;
    public static bool operator >=(TenantId left, TenantId right) => left.CompareTo(right) >= 0;

    public static TenantId Parse(string s) => Parse(s, null);

    public static TenantId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new TenantId(Guid.Parse(s, provider));
    }

    public static TenantId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static TenantId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out TenantId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out TenantId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TenantId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TenantId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TenantId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TenantId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class TenantIdTypeConverter : EntityIdTypeConverter<TenantId>;

public sealed class TenantIdJsonConverter : EntityIdJsonConverter<TenantId>;

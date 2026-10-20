using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a TenantMembership (a person's presence in one tenant, K-ID-4).
/// </summary>
[TypeConverter(typeof(TenantMembershipIdTypeConverter))]
[JsonConverter(typeof(TenantMembershipIdJsonConverter))]
public readonly record struct TenantMembershipId(Guid Value) : IEntityId<TenantMembershipId>
{
    public static TenantMembershipId New() => new(Guid.CreateVersion7());

    public static TenantMembershipId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static TenantMembershipId FromGuid(Guid value) => new(value);

    public static TenantMembershipId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(TenantMembershipId id) => id.Value;

    public static explicit operator TenantMembershipId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(TenantMembershipId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        TenantMembershipId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(TenantMembershipId)}", nameof(obj))
    };

    public static bool operator <(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) < 0;
    public static bool operator <=(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) <= 0;
    public static bool operator >(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) > 0;
    public static bool operator >=(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) >= 0;

    public static TenantMembershipId Parse(string s) => Parse(s, null);

    public static TenantMembershipId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new TenantMembershipId(Guid.Parse(s, provider));
    }

    public static TenantMembershipId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static TenantMembershipId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out TenantMembershipId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out TenantMembershipId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TenantMembershipId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TenantMembershipId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TenantMembershipId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TenantMembershipId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class TenantMembershipIdTypeConverter : EntityIdTypeConverter<TenantMembershipId>;

public sealed class TenantMembershipIdJsonConverter : EntityIdJsonConverter<TenantMembershipId>;

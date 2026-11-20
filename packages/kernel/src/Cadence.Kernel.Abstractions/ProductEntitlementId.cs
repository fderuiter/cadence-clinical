using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUID identifier for a commercial ProductEntitlement attached to a tenant subscription.
/// Entitlements govern commercial module availability only and never grant clinical data access.
/// </summary>
[TypeConverter(typeof(ProductEntitlementIdTypeConverter))]
[JsonConverter(typeof(ProductEntitlementIdJsonConverter))]
public readonly record struct ProductEntitlementId(Guid Value) : IEntityId<ProductEntitlementId>
{
    public static ProductEntitlementId New() => new(Guid.CreateVersion7());

    public static ProductEntitlementId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ProductEntitlementId FromGuid(Guid value) => new(value);

    public static ProductEntitlementId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ProductEntitlementId id) => id.Value;

    public static explicit operator ProductEntitlementId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ProductEntitlementId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ProductEntitlementId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ProductEntitlementId)}", nameof(obj))
    };

    public static bool operator <(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) >= 0;

    public static ProductEntitlementId Parse(string s) => Parse(s, null);

    public static ProductEntitlementId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ProductEntitlementId(Guid.Parse(s, provider));
    }

    public static ProductEntitlementId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ProductEntitlementId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ProductEntitlementId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProductEntitlementId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProductEntitlementId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ProductEntitlementId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProductEntitlementId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ProductEntitlementId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ProductEntitlementIdTypeConverter : EntityIdTypeConverter<ProductEntitlementId>;

public sealed class ProductEntitlementIdJsonConverter : EntityIdJsonConverter<ProductEntitlementId>;

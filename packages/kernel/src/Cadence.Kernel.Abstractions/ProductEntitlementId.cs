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
[TypeConverter(typeof(EntityIdTypeConverter<ProductEntitlementId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ProductEntitlementId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProductEntitlementId left, ProductEntitlementId right) => left.CompareTo(right) >= 0;

    public static ProductEntitlementId Parse(string s) => EntityIdOperations.Parse<ProductEntitlementId>(s);

    public static ProductEntitlementId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ProductEntitlementId>(s, provider);

    public static ProductEntitlementId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ProductEntitlementId>(s);

    public static ProductEntitlementId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ProductEntitlementId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ProductEntitlementId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProductEntitlementId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProductEntitlementId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProductEntitlementId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

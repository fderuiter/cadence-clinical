using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Tenant (isolation and customer contractual boundary).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<TenantId>))]
[JsonConverter(typeof(EntityIdJsonConverter<TenantId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(TenantId left, TenantId right) => left.CompareTo(right) < 0;
    public static bool operator <=(TenantId left, TenantId right) => left.CompareTo(right) <= 0;
    public static bool operator >(TenantId left, TenantId right) => left.CompareTo(right) > 0;
    public static bool operator >=(TenantId left, TenantId right) => left.CompareTo(right) >= 0;

    public static TenantId Parse(string s) => EntityIdOperations.Parse<TenantId>(s);

    public static TenantId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<TenantId>(s, provider);

    public static TenantId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<TenantId>(s);

    public static TenantId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<TenantId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out TenantId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out TenantId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TenantId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TenantId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

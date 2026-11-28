using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a TenantMembership (a person's presence in one tenant, K-ID-4).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<TenantMembershipId>))]
[JsonConverter(typeof(EntityIdJsonConverter<TenantMembershipId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) < 0;
    public static bool operator <=(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) <= 0;
    public static bool operator >(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) > 0;
    public static bool operator >=(TenantMembershipId left, TenantMembershipId right) => left.CompareTo(right) >= 0;

    public static TenantMembershipId Parse(string s) => EntityIdOperations.Parse<TenantMembershipId>(s);

    public static TenantMembershipId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<TenantMembershipId>(s, provider);

    public static TenantMembershipId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<TenantMembershipId>(s);

    public static TenantMembershipId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<TenantMembershipId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out TenantMembershipId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out TenantMembershipId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TenantMembershipId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TenantMembershipId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

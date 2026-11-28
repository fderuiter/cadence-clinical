using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Organization (Sponsor, CRO, Site institution, Vendor, Regulator, IRB/EC, Lab).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<OrganizationId>))]
[JsonConverter(typeof(EntityIdJsonConverter<OrganizationId>))]
public readonly record struct OrganizationId(Guid Value) : IEntityId<OrganizationId>
{
    public static OrganizationId New() => new(Guid.CreateVersion7());

    public static OrganizationId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static OrganizationId FromGuid(Guid value) => new(value);

    public static OrganizationId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(OrganizationId id) => id.Value;

    public static explicit operator OrganizationId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(OrganizationId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(OrganizationId left, OrganizationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(OrganizationId left, OrganizationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(OrganizationId left, OrganizationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(OrganizationId left, OrganizationId right) => left.CompareTo(right) >= 0;

    public static OrganizationId Parse(string s) => EntityIdOperations.Parse<OrganizationId>(s);

    public static OrganizationId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<OrganizationId>(s, provider);

    public static OrganizationId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<OrganizationId>(s);

    public static OrganizationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<OrganizationId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out OrganizationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out OrganizationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out OrganizationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out OrganizationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Organization (Sponsor, CRO, Site institution, Vendor, Regulator, IRB/EC, Lab).
/// @req:PRD-SYS-053
/// </summary>
[TypeConverter(typeof(OrganizationIdTypeConverter))]
[JsonConverter(typeof(OrganizationIdJsonConverter))]
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

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        OrganizationId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(OrganizationId)}", nameof(obj))
    };

    public static bool operator <(OrganizationId left, OrganizationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(OrganizationId left, OrganizationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(OrganizationId left, OrganizationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(OrganizationId left, OrganizationId right) => left.CompareTo(right) >= 0;

    public static OrganizationId Parse(string s) => Parse(s, null);

    public static OrganizationId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new OrganizationId(Guid.Parse(s, provider));
    }

    public static OrganizationId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static OrganizationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out OrganizationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out OrganizationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out OrganizationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new OrganizationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out OrganizationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new OrganizationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class OrganizationIdTypeConverter : EntityIdTypeConverter<OrganizationId>;

public sealed class OrganizationIdJsonConverter : EntityIdJsonConverter<OrganizationId>;

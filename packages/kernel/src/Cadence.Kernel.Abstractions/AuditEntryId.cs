using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a tamper-evident kernel audit entry (<c>kernel.audit_entries</c>, K-AUD-2).
/// </summary>
[TypeConverter(typeof(AuditEntryIdTypeConverter))]
[JsonConverter(typeof(AuditEntryIdJsonConverter))]
public readonly record struct AuditEntryId(Guid Value) : IEntityId<AuditEntryId>
{
    public static AuditEntryId New() => new(Guid.CreateVersion7());

    public static AuditEntryId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static AuditEntryId FromGuid(Guid value) => new(value);

    public static AuditEntryId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(AuditEntryId id) => id.Value;

    public static explicit operator AuditEntryId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(AuditEntryId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        AuditEntryId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(AuditEntryId)}", nameof(obj))
    };

    public static bool operator <(AuditEntryId left, AuditEntryId right) => left.CompareTo(right) < 0;
    public static bool operator <=(AuditEntryId left, AuditEntryId right) => left.CompareTo(right) <= 0;
    public static bool operator >(AuditEntryId left, AuditEntryId right) => left.CompareTo(right) > 0;
    public static bool operator >=(AuditEntryId left, AuditEntryId right) => left.CompareTo(right) >= 0;

    public static AuditEntryId Parse(string s) => Parse(s, null);

    public static AuditEntryId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new AuditEntryId(Guid.Parse(s, provider));
    }

    public static AuditEntryId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static AuditEntryId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out AuditEntryId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out AuditEntryId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AuditEntryId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new AuditEntryId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AuditEntryId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new AuditEntryId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class AuditEntryIdTypeConverter : EntityIdTypeConverter<AuditEntryId>;

public sealed class AuditEntryIdJsonConverter : EntityIdJsonConverter<AuditEntryId>;

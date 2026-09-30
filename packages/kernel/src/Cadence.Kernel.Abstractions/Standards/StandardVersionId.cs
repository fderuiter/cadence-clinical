using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Standards;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a registered version of a data standard (SDTMIG 3.4, ADaMIG 1.3, …) in the Standards Registry (spec §6.3).
/// </summary>
[TypeConverter(typeof(StandardVersionIdTypeConverter))]
[JsonConverter(typeof(StandardVersionIdJsonConverter))]
public readonly record struct StandardVersionId(Guid Value) : IEntityId<StandardVersionId>
{
    public static StandardVersionId New() => new(Guid.CreateVersion7());

    public static StandardVersionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static StandardVersionId FromGuid(Guid value) => new(value);

    public static StandardVersionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(StandardVersionId id) => id.Value;

    public static explicit operator StandardVersionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(StandardVersionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        StandardVersionId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(StandardVersionId)}", nameof(obj))
    };

    public static bool operator <(StandardVersionId left, StandardVersionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(StandardVersionId left, StandardVersionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(StandardVersionId left, StandardVersionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(StandardVersionId left, StandardVersionId right) => left.CompareTo(right) >= 0;

    public static StandardVersionId Parse(string s) => Parse(s, null);

    public static StandardVersionId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new StandardVersionId(Guid.Parse(s, provider));
    }

    public static StandardVersionId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static StandardVersionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out StandardVersionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out StandardVersionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StandardVersionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StandardVersionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StandardVersionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StandardVersionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class StandardVersionIdTypeConverter : EntityIdTypeConverter<StandardVersionId>;

public sealed class StandardVersionIdJsonConverter : EntityIdJsonConverter<StandardVersionId>;

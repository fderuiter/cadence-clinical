using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a ConsentStatus aggregate: computable bitemporal participant consent for a
/// protocol version and scope set (spec §9.4, K-CONS-1, K-CONS-2).
/// </summary>
[TypeConverter(typeof(ConsentStatusIdTypeConverter))]
[JsonConverter(typeof(ConsentStatusIdJsonConverter))]
public readonly record struct ConsentStatusId(Guid Value) : IEntityId<ConsentStatusId>
{
    public static ConsentStatusId New() => new(Guid.CreateVersion7());

    public static ConsentStatusId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ConsentStatusId FromGuid(Guid value) => new(value);

    public static ConsentStatusId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ConsentStatusId id) => id.Value;

    public static explicit operator ConsentStatusId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ConsentStatusId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ConsentStatusId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ConsentStatusId)}", nameof(obj))
    };

    public static bool operator <(ConsentStatusId left, ConsentStatusId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ConsentStatusId left, ConsentStatusId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ConsentStatusId left, ConsentStatusId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ConsentStatusId left, ConsentStatusId right) => left.CompareTo(right) >= 0;

    public static ConsentStatusId Parse(string s) => Parse(s, null);

    public static ConsentStatusId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ConsentStatusId(Guid.Parse(s, provider));
    }

    public static ConsentStatusId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ConsentStatusId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ConsentStatusId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ConsentStatusId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ConsentStatusId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConsentStatusId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ConsentStatusId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConsentStatusId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ConsentStatusIdTypeConverter : EntityIdTypeConverter<ConsentStatusId>;

public sealed class ConsentStatusIdJsonConverter : EntityIdJsonConverter<ConsentStatusId>;

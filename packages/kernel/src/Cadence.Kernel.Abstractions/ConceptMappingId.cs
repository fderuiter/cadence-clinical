using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a mapping from a Concept to a code in a pinned code-system version.
/// </summary>
[TypeConverter(typeof(ConceptMappingIdTypeConverter))]
[JsonConverter(typeof(ConceptMappingIdJsonConverter))]
public readonly record struct ConceptMappingId(Guid Value) : IEntityId<ConceptMappingId>
{
    public static ConceptMappingId New() => new(Guid.CreateVersion7());

    public static ConceptMappingId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ConceptMappingId FromGuid(Guid value) => new(value);

    public static ConceptMappingId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ConceptMappingId id) => id.Value;

    public static explicit operator ConceptMappingId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ConceptMappingId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ConceptMappingId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ConceptMappingId)}", nameof(obj))
    };

    public static bool operator <(ConceptMappingId left, ConceptMappingId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ConceptMappingId left, ConceptMappingId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ConceptMappingId left, ConceptMappingId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ConceptMappingId left, ConceptMappingId right) => left.CompareTo(right) >= 0;

    public static ConceptMappingId Parse(string s) => Parse(s, null);

    public static ConceptMappingId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ConceptMappingId(Guid.Parse(s, provider));
    }

    public static ConceptMappingId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ConceptMappingId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ConceptMappingId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ConceptMappingId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ConceptMappingId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConceptMappingId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ConceptMappingId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConceptMappingId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ConceptMappingIdTypeConverter : EntityIdTypeConverter<ConceptMappingId>;

public sealed class ConceptMappingIdJsonConverter : EntityIdJsonConverter<ConceptMappingId>;

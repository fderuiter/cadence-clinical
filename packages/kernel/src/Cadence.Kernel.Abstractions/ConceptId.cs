using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Concept in the Concept Registry, the kernel's key of clinical meaning (spec §6.1).
/// </summary>
[TypeConverter(typeof(ConceptIdTypeConverter))]
[JsonConverter(typeof(ConceptIdJsonConverter))]
public readonly record struct ConceptId(Guid Value) : IEntityId<ConceptId>
{
    public static ConceptId New() => new(Guid.CreateVersion7());

    public static ConceptId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ConceptId FromGuid(Guid value) => new(value);

    public static ConceptId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ConceptId id) => id.Value;

    public static explicit operator ConceptId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ConceptId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ConceptId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ConceptId)}", nameof(obj))
    };

    public static bool operator <(ConceptId left, ConceptId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ConceptId left, ConceptId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ConceptId left, ConceptId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ConceptId left, ConceptId right) => left.CompareTo(right) >= 0;

    public static ConceptId Parse(string s) => Parse(s, null);

    public static ConceptId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ConceptId(Guid.Parse(s, provider));
    }

    public static ConceptId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ConceptId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ConceptId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ConceptId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ConceptId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConceptId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ConceptId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ConceptId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ConceptIdTypeConverter : EntityIdTypeConverter<ConceptId>;

public sealed class ConceptIdJsonConverter : EntityIdJsonConverter<ConceptId>;

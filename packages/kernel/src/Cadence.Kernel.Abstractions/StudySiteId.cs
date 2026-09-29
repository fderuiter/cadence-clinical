using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a StudySite (association of Study x Site with lifecycle and protocol version effectivity).
/// @req:PRD-SYS-053
/// </summary>
[TypeConverter(typeof(StudySiteIdTypeConverter))]
[JsonConverter(typeof(StudySiteIdJsonConverter))]
public readonly record struct StudySiteId(Guid Value) : IEntityId<StudySiteId>
{
    public static StudySiteId New() => new(Guid.CreateVersion7());

    public static StudySiteId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static StudySiteId FromGuid(Guid value) => new(value);

    public static StudySiteId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(StudySiteId id) => id.Value;

    public static explicit operator StudySiteId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(StudySiteId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        StudySiteId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(StudySiteId)}", nameof(obj))
    };

    public static bool operator <(StudySiteId left, StudySiteId right) => left.CompareTo(right) < 0;
    public static bool operator <=(StudySiteId left, StudySiteId right) => left.CompareTo(right) <= 0;
    public static bool operator >(StudySiteId left, StudySiteId right) => left.CompareTo(right) > 0;
    public static bool operator >=(StudySiteId left, StudySiteId right) => left.CompareTo(right) >= 0;

    public static StudySiteId Parse(string s) => Parse(s, null);

    public static StudySiteId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new StudySiteId(Guid.Parse(s, provider));
    }

    public static StudySiteId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static StudySiteId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out StudySiteId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out StudySiteId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StudySiteId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StudySiteId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StudySiteId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StudySiteId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class StudySiteIdTypeConverter : EntityIdTypeConverter<StudySiteId>;

public sealed class StudySiteIdJsonConverter : EntityIdJsonConverter<StudySiteId>;

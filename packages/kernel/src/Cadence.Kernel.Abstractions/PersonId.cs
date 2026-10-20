using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Person (human identity independent of tenant-scoped role).
/// </summary>
[TypeConverter(typeof(PersonIdTypeConverter))]
[JsonConverter(typeof(PersonIdJsonConverter))]
public readonly record struct PersonId(Guid Value) : IEntityId<PersonId>
{
    public static PersonId New() => new(Guid.CreateVersion7());

    public static PersonId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static PersonId FromGuid(Guid value) => new(value);

    public static PersonId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(PersonId id) => id.Value;

    public static explicit operator PersonId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(PersonId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        PersonId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(PersonId)}", nameof(obj))
    };

    public static bool operator <(PersonId left, PersonId right) => left.CompareTo(right) < 0;
    public static bool operator <=(PersonId left, PersonId right) => left.CompareTo(right) <= 0;
    public static bool operator >(PersonId left, PersonId right) => left.CompareTo(right) > 0;
    public static bool operator >=(PersonId left, PersonId right) => left.CompareTo(right) >= 0;

    public static PersonId Parse(string s) => Parse(s, null);

    public static PersonId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new PersonId(Guid.Parse(s, provider));
    }

    public static PersonId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static PersonId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out PersonId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out PersonId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PersonId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new PersonId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out PersonId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new PersonId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class PersonIdTypeConverter : EntityIdTypeConverter<PersonId>;

public sealed class PersonIdJsonConverter : EntityIdJsonConverter<PersonId>;

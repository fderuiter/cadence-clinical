using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Study (clinical protocol definition owned by a Sponsor Organization).
/// </summary>
[TypeConverter(typeof(StudyIdTypeConverter))]
[JsonConverter(typeof(StudyIdJsonConverter))]
public readonly record struct StudyId(Guid Value) : IEntityId<StudyId>
{
    public static StudyId New() => new(Guid.CreateVersion7());

    public static StudyId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static StudyId FromGuid(Guid value) => new(value);

    public static StudyId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(StudyId id) => id.Value;

    public static explicit operator StudyId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(StudyId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        StudyId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(StudyId)}", nameof(obj))
    };

    public static bool operator <(StudyId left, StudyId right) => left.CompareTo(right) < 0;
    public static bool operator <=(StudyId left, StudyId right) => left.CompareTo(right) <= 0;
    public static bool operator >(StudyId left, StudyId right) => left.CompareTo(right) > 0;
    public static bool operator >=(StudyId left, StudyId right) => left.CompareTo(right) >= 0;

    public static StudyId Parse(string s) => Parse(s, null);

    public static StudyId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new StudyId(Guid.Parse(s, provider));
    }

    public static StudyId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static StudyId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out StudyId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out StudyId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StudyId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StudyId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StudyId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new StudyId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class StudyIdTypeConverter : EntityIdTypeConverter<StudyId>;

public sealed class StudyIdJsonConverter : EntityIdJsonConverter<StudyId>;

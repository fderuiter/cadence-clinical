using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Study (clinical protocol definition owned by a Sponsor Organization).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<StudyId>))]
[JsonConverter(typeof(EntityIdJsonConverter<StudyId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(StudyId left, StudyId right) => left.CompareTo(right) < 0;
    public static bool operator <=(StudyId left, StudyId right) => left.CompareTo(right) <= 0;
    public static bool operator >(StudyId left, StudyId right) => left.CompareTo(right) > 0;
    public static bool operator >=(StudyId left, StudyId right) => left.CompareTo(right) >= 0;

    public static StudyId Parse(string s) => EntityIdOperations.Parse<StudyId>(s);

    public static StudyId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<StudyId>(s, provider);

    public static StudyId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<StudyId>(s);

    public static StudyId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<StudyId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out StudyId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out StudyId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StudyId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StudyId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

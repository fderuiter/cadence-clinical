using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Standards;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a study's declared standards, controlled-terminology versions and pinned library items (spec §6.3, K-STD-1).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<StudyStandardsProfileId>))]
[JsonConverter(typeof(EntityIdJsonConverter<StudyStandardsProfileId>))]
public readonly record struct StudyStandardsProfileId(Guid Value) : IEntityId<StudyStandardsProfileId>
{
    public static StudyStandardsProfileId New() => new(Guid.CreateVersion7());

    public static StudyStandardsProfileId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static StudyStandardsProfileId FromGuid(Guid value) => new(value);

    public static StudyStandardsProfileId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(StudyStandardsProfileId id) => id.Value;

    public static explicit operator StudyStandardsProfileId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(StudyStandardsProfileId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(StudyStandardsProfileId left, StudyStandardsProfileId right) => left.CompareTo(right) < 0;
    public static bool operator <=(StudyStandardsProfileId left, StudyStandardsProfileId right) => left.CompareTo(right) <= 0;
    public static bool operator >(StudyStandardsProfileId left, StudyStandardsProfileId right) => left.CompareTo(right) > 0;
    public static bool operator >=(StudyStandardsProfileId left, StudyStandardsProfileId right) => left.CompareTo(right) >= 0;

    public static StudyStandardsProfileId Parse(string s) => EntityIdOperations.Parse<StudyStandardsProfileId>(s);

    public static StudyStandardsProfileId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<StudyStandardsProfileId>(s, provider);

    public static StudyStandardsProfileId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<StudyStandardsProfileId>(s);

    public static StudyStandardsProfileId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<StudyStandardsProfileId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out StudyStandardsProfileId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out StudyStandardsProfileId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StudyStandardsProfileId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StudyStandardsProfileId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

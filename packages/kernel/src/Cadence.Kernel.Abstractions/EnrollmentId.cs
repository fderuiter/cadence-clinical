using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Enrollment: one participant's membership of one study site, with its
/// bitemporal status history (spec §7.4).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<EnrollmentId>))]
[JsonConverter(typeof(EntityIdJsonConverter<EnrollmentId>))]
public readonly record struct EnrollmentId(Guid Value) : IEntityId<EnrollmentId>
{
    public static EnrollmentId New() => new(Guid.CreateVersion7());

    public static EnrollmentId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static EnrollmentId FromGuid(Guid value) => new(value);

    public static EnrollmentId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(EnrollmentId id) => id.Value;

    public static explicit operator EnrollmentId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(EnrollmentId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) < 0;
    public static bool operator <=(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) <= 0;
    public static bool operator >(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) > 0;
    public static bool operator >=(EnrollmentId left, EnrollmentId right) => left.CompareTo(right) >= 0;

    public static EnrollmentId Parse(string s) => EntityIdOperations.Parse<EnrollmentId>(s);

    public static EnrollmentId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<EnrollmentId>(s, provider);

    public static EnrollmentId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<EnrollmentId>(s);

    public static EnrollmentId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<EnrollmentId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out EnrollmentId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out EnrollmentId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EnrollmentId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out EnrollmentId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

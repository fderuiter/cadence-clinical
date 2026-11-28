using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Qualification: evidence about an actor, such as completed training, a
/// credential or a certificate, with its validity period and evidence artifact (spec §9.3, K-AUTH-5).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<QualificationId>))]
[JsonConverter(typeof(EntityIdJsonConverter<QualificationId>))]
public readonly record struct QualificationId(Guid Value) : IEntityId<QualificationId>
{
    public static QualificationId New() => new(Guid.CreateVersion7());

    public static QualificationId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static QualificationId FromGuid(Guid value) => new(value);

    public static QualificationId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(QualificationId id) => id.Value;

    public static explicit operator QualificationId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(QualificationId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(QualificationId left, QualificationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(QualificationId left, QualificationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(QualificationId left, QualificationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(QualificationId left, QualificationId right) => left.CompareTo(right) >= 0;

    public static QualificationId Parse(string s) => EntityIdOperations.Parse<QualificationId>(s);

    public static QualificationId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<QualificationId>(s, provider);

    public static QualificationId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<QualificationId>(s);

    public static QualificationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<QualificationId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out QualificationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out QualificationId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out QualificationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out QualificationId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a QualificationRequirement: a rule that exercising a capability within a scope
/// needs a current qualification of a given code (spec §9.3, K-AUTH-5).
/// </summary>
[TypeConverter(typeof(QualificationRequirementIdTypeConverter))]
[JsonConverter(typeof(QualificationRequirementIdJsonConverter))]
public readonly record struct QualificationRequirementId(Guid Value) : IEntityId<QualificationRequirementId>
{
    public static QualificationRequirementId New() => new(Guid.CreateVersion7());

    public static QualificationRequirementId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static QualificationRequirementId FromGuid(Guid value) => new(value);

    public static QualificationRequirementId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(QualificationRequirementId id) => id.Value;

    public static explicit operator QualificationRequirementId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(QualificationRequirementId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        QualificationRequirementId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(QualificationRequirementId)}", nameof(obj))
    };

    public static bool operator <(QualificationRequirementId left, QualificationRequirementId right) => left.CompareTo(right) < 0;
    public static bool operator <=(QualificationRequirementId left, QualificationRequirementId right) => left.CompareTo(right) <= 0;
    public static bool operator >(QualificationRequirementId left, QualificationRequirementId right) => left.CompareTo(right) > 0;
    public static bool operator >=(QualificationRequirementId left, QualificationRequirementId right) => left.CompareTo(right) >= 0;

    public static QualificationRequirementId Parse(string s) => Parse(s, null);

    public static QualificationRequirementId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new QualificationRequirementId(Guid.Parse(s, provider));
    }

    public static QualificationRequirementId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static QualificationRequirementId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out QualificationRequirementId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out QualificationRequirementId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out QualificationRequirementId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new QualificationRequirementId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out QualificationRequirementId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new QualificationRequirementId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class QualificationRequirementIdTypeConverter : EntityIdTypeConverter<QualificationRequirementId>;

public sealed class QualificationRequirementIdJsonConverter : EntityIdJsonConverter<QualificationRequirementId>;

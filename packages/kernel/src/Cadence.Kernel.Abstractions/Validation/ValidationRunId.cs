using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Strongly-typed UUIDv7 identifier for one validation run (spec §10.3): one evaluation of rule packs at one stage.
/// </summary>
[TypeConverter(typeof(ValidationRunIdTypeConverter))]
[JsonConverter(typeof(ValidationRunIdJsonConverter))]
public readonly record struct ValidationRunId(Guid Value) : IEntityId<ValidationRunId>
{
    public static ValidationRunId New() => new(Guid.CreateVersion7());

    public static ValidationRunId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ValidationRunId FromGuid(Guid value) => new(value);

    public static ValidationRunId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ValidationRunId id) => id.Value;

    public static explicit operator ValidationRunId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ValidationRunId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ValidationRunId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ValidationRunId)}", nameof(obj))
    };

    public static bool operator <(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) >= 0;

    public static ValidationRunId Parse(string s) => Parse(s, null);

    public static ValidationRunId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ValidationRunId(Guid.Parse(s, provider));
    }

    public static ValidationRunId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ValidationRunId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ValidationRunId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ValidationRunId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ValidationRunId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ValidationRunId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ValidationRunId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ValidationRunId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ValidationRunIdTypeConverter : EntityIdTypeConverter<ValidationRunId>;

public sealed class ValidationRunIdJsonConverter : EntityIdJsonConverter<ValidationRunId>;

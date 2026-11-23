using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed identifier for an eCRF discrepancy query (<c>FormQuery</c>, PRD-QRY-001, PRD-QRY-002, ADR-2625):
/// a module-managed workflow aggregate linked to an enrollment and observation in kernel lineage.
/// </summary>
[TypeConverter(typeof(FormQueryIdTypeConverter))]
[JsonConverter(typeof(FormQueryIdJsonConverter))]
public readonly record struct FormQueryId(Guid Value) : IEntityId<FormQueryId>
{
    /// <summary>Creates a new time-ordered <see cref="FormQueryId"/>.</summary>
    public static FormQueryId New() => new(Guid.CreateVersion7());

    /// <summary>Creates a new time-ordered <see cref="FormQueryId"/> seeded with <paramref name="timestamp"/>.</summary>
    public static FormQueryId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    /// <summary>Wraps <paramref name="value"/> as a <see cref="FormQueryId"/>.</summary>
    public static FormQueryId FromGuid(Guid value) => new(value);

    /// <summary>The empty <see cref="FormQueryId"/>.</summary>
    public static FormQueryId Empty => new(Guid.Empty);

    /// <summary>True when <see cref="Value"/> is <see cref="Guid.Empty"/>.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Unwraps <see cref="Value"/>.</summary>
    public static explicit operator Guid(FormQueryId id) => id.Value;

    /// <summary>Wraps <paramref name="value"/>.</summary>
    public static explicit operator FormQueryId(Guid value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    /// <inheritdoc />
    public int CompareTo(FormQueryId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        FormQueryId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(FormQueryId)}", nameof(obj)),
    };

    /// <summary>Compares two <see cref="FormQueryId"/> values.</summary>
    public static bool operator <(FormQueryId left, FormQueryId right) => left.CompareTo(right) < 0;

    /// <summary>Compares two <see cref="FormQueryId"/> values.</summary>
    public static bool operator <=(FormQueryId left, FormQueryId right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two <see cref="FormQueryId"/> values.</summary>
    public static bool operator >(FormQueryId left, FormQueryId right) => left.CompareTo(right) > 0;

    /// <summary>Compares two <see cref="FormQueryId"/> values.</summary>
    public static bool operator >=(FormQueryId left, FormQueryId right) => left.CompareTo(right) >= 0;

    /// <summary>Parses <paramref name="s"/> as a <see cref="FormQueryId"/>.</summary>
    public static FormQueryId Parse(string s) => Parse(s, null);

    /// <inheritdoc />
    public static FormQueryId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new FormQueryId(Guid.Parse(s, provider));
    }

    /// <summary>Parses <paramref name="s"/> as a <see cref="FormQueryId"/>.</summary>
    public static FormQueryId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    /// <inheritdoc />
    public static FormQueryId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    /// <summary>Tries to parse <paramref name="s"/> as a <see cref="FormQueryId"/>.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out FormQueryId result) =>
        TryParse(s, null, out result);

    /// <summary>Tries to parse <paramref name="s"/> as a <see cref="FormQueryId"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out FormQueryId result) =>
        TryParse(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out FormQueryId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new FormQueryId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out FormQueryId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new FormQueryId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

/// <summary>TypeConverter for <see cref="FormQueryId"/>.</summary>
public sealed class FormQueryIdTypeConverter : EntityIdTypeConverter<FormQueryId>;

/// <summary>JSON converter for <see cref="FormQueryId"/>.</summary>
public sealed class FormQueryIdJsonConverter : EntityIdJsonConverter<FormQueryId>;

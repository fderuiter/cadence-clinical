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
[TypeConverter(typeof(EntityIdTypeConverter<FormQueryId>))]
[JsonConverter(typeof(EntityIdJsonConverter<FormQueryId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(FormQueryId left, FormQueryId right) => left.CompareTo(right) < 0;
    public static bool operator <=(FormQueryId left, FormQueryId right) => left.CompareTo(right) <= 0;
    public static bool operator >(FormQueryId left, FormQueryId right) => left.CompareTo(right) > 0;
    public static bool operator >=(FormQueryId left, FormQueryId right) => left.CompareTo(right) >= 0;

    public static FormQueryId Parse(string s) => EntityIdOperations.Parse<FormQueryId>(s);

    public static FormQueryId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<FormQueryId>(s, provider);

    public static FormQueryId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<FormQueryId>(s);

    public static FormQueryId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<FormQueryId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out FormQueryId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out FormQueryId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out FormQueryId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out FormQueryId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

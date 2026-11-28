using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Strongly-typed UUIDv7 identifier for one validation run (spec §10.3): one evaluation of rule packs at one stage.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ValidationRunId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ValidationRunId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ValidationRunId left, ValidationRunId right) => left.CompareTo(right) >= 0;

    public static ValidationRunId Parse(string s) => EntityIdOperations.Parse<ValidationRunId>(s);

    public static ValidationRunId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ValidationRunId>(s, provider);

    public static ValidationRunId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ValidationRunId>(s);

    public static ValidationRunId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ValidationRunId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ValidationRunId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ValidationRunId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ValidationRunId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ValidationRunId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

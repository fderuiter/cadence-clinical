using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Effectivity: whether a protocol version is in force for a study, a country of
/// the study or one study site, over valid and recorded time (spec §7.1).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<EffectivityId>))]
[JsonConverter(typeof(EntityIdJsonConverter<EffectivityId>))]
public readonly record struct EffectivityId(Guid Value) : IEntityId<EffectivityId>
{
    public static EffectivityId New() => new(Guid.CreateVersion7());

    public static EffectivityId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static EffectivityId FromGuid(Guid value) => new(value);

    public static EffectivityId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(EffectivityId id) => id.Value;

    public static explicit operator EffectivityId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(EffectivityId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(EffectivityId left, EffectivityId right) => left.CompareTo(right) < 0;
    public static bool operator <=(EffectivityId left, EffectivityId right) => left.CompareTo(right) <= 0;
    public static bool operator >(EffectivityId left, EffectivityId right) => left.CompareTo(right) > 0;
    public static bool operator >=(EffectivityId left, EffectivityId right) => left.CompareTo(right) >= 0;

    public static EffectivityId Parse(string s) => EntityIdOperations.Parse<EffectivityId>(s);

    public static EffectivityId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<EffectivityId>(s, provider);

    public static EffectivityId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<EffectivityId>(s);

    public static EffectivityId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<EffectivityId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out EffectivityId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out EffectivityId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EffectivityId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out EffectivityId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

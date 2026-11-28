using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Site (physical/operational trial location belonging to an Institution Organization).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<SiteId>))]
[JsonConverter(typeof(EntityIdJsonConverter<SiteId>))]
public readonly record struct SiteId(Guid Value) : IEntityId<SiteId>
{
    public static SiteId New() => new(Guid.CreateVersion7());

    public static SiteId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static SiteId FromGuid(Guid value) => new(value);

    public static SiteId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(SiteId id) => id.Value;

    public static explicit operator SiteId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(SiteId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(SiteId left, SiteId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SiteId left, SiteId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SiteId left, SiteId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SiteId left, SiteId right) => left.CompareTo(right) >= 0;

    public static SiteId Parse(string s) => EntityIdOperations.Parse<SiteId>(s);

    public static SiteId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<SiteId>(s, provider);

    public static SiteId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<SiteId>(s);

    public static SiteId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<SiteId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out SiteId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out SiteId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SiteId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SiteId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

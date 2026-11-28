using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Role: a tenant's named bundle of capabilities, an administrative
/// convenience for issuing grants (spec §9.3).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<RoleId>))]
[JsonConverter(typeof(EntityIdJsonConverter<RoleId>))]
public readonly record struct RoleId(Guid Value) : IEntityId<RoleId>
{
    public static RoleId New() => new(Guid.CreateVersion7());

    public static RoleId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static RoleId FromGuid(Guid value) => new(value);

    public static RoleId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(RoleId id) => id.Value;

    public static explicit operator RoleId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(RoleId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(RoleId left, RoleId right) => left.CompareTo(right) < 0;
    public static bool operator <=(RoleId left, RoleId right) => left.CompareTo(right) <= 0;
    public static bool operator >(RoleId left, RoleId right) => left.CompareTo(right) > 0;
    public static bool operator >=(RoleId left, RoleId right) => left.CompareTo(right) >= 0;

    public static RoleId Parse(string s) => EntityIdOperations.Parse<RoleId>(s);

    public static RoleId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<RoleId>(s, provider);

    public static RoleId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<RoleId>(s);

    public static RoleId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<RoleId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out RoleId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out RoleId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out RoleId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out RoleId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

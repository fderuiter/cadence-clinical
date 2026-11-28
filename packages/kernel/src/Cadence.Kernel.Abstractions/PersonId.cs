using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Person (human identity independent of tenant-scoped role).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<PersonId>))]
[JsonConverter(typeof(EntityIdJsonConverter<PersonId>))]
public readonly record struct PersonId(Guid Value) : IEntityId<PersonId>
{
    public static PersonId New() => new(Guid.CreateVersion7());

    public static PersonId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static PersonId FromGuid(Guid value) => new(value);

    public static PersonId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(PersonId id) => id.Value;

    public static explicit operator PersonId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(PersonId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(PersonId left, PersonId right) => left.CompareTo(right) < 0;
    public static bool operator <=(PersonId left, PersonId right) => left.CompareTo(right) <= 0;
    public static bool operator >(PersonId left, PersonId right) => left.CompareTo(right) > 0;
    public static bool operator >=(PersonId left, PersonId right) => left.CompareTo(right) >= 0;

    public static PersonId Parse(string s) => EntityIdOperations.Parse<PersonId>(s);

    public static PersonId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<PersonId>(s, provider);

    public static PersonId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<PersonId>(s);

    public static PersonId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<PersonId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out PersonId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out PersonId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PersonId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out PersonId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

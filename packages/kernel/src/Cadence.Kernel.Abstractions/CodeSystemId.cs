using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a code system (a terminology such as LOINC, UCUM or CDISC CT) registered in a tenant's Concept Registry.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<CodeSystemId>))]
[JsonConverter(typeof(EntityIdJsonConverter<CodeSystemId>))]
public readonly record struct CodeSystemId(Guid Value) : IEntityId<CodeSystemId>
{
    public static CodeSystemId New() => new(Guid.CreateVersion7());

    public static CodeSystemId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static CodeSystemId FromGuid(Guid value) => new(value);

    public static CodeSystemId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(CodeSystemId id) => id.Value;

    public static explicit operator CodeSystemId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(CodeSystemId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) >= 0;

    public static CodeSystemId Parse(string s) => EntityIdOperations.Parse<CodeSystemId>(s);

    public static CodeSystemId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<CodeSystemId>(s, provider);

    public static CodeSystemId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<CodeSystemId>(s);

    public static CodeSystemId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<CodeSystemId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out CodeSystemId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out CodeSystemId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CodeSystemId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CodeSystemId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

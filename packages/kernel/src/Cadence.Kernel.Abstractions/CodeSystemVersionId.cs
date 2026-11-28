using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an immutable, checksum-verified release of a code system in the Concept Registry.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<CodeSystemVersionId>))]
[JsonConverter(typeof(EntityIdJsonConverter<CodeSystemVersionId>))]
public readonly record struct CodeSystemVersionId(Guid Value) : IEntityId<CodeSystemVersionId>
{
    public static CodeSystemVersionId New() => new(Guid.CreateVersion7());

    public static CodeSystemVersionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static CodeSystemVersionId FromGuid(Guid value) => new(value);

    public static CodeSystemVersionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(CodeSystemVersionId id) => id.Value;

    public static explicit operator CodeSystemVersionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(CodeSystemVersionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(CodeSystemVersionId left, CodeSystemVersionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CodeSystemVersionId left, CodeSystemVersionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CodeSystemVersionId left, CodeSystemVersionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CodeSystemVersionId left, CodeSystemVersionId right) => left.CompareTo(right) >= 0;

    public static CodeSystemVersionId Parse(string s) => EntityIdOperations.Parse<CodeSystemVersionId>(s);

    public static CodeSystemVersionId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<CodeSystemVersionId>(s, provider);

    public static CodeSystemVersionId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<CodeSystemVersionId>(s);

    public static CodeSystemVersionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<CodeSystemVersionId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out CodeSystemVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out CodeSystemVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CodeSystemVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CodeSystemVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

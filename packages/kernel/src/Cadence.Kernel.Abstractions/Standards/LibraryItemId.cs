using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Standards;

/// <summary>
/// Strongly-typed UUIDv7 identifier for one version of a governed Metadata Registry (MDR) library item (spec §6.3).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<LibraryItemId>))]
[JsonConverter(typeof(EntityIdJsonConverter<LibraryItemId>))]
public readonly record struct LibraryItemId(Guid Value) : IEntityId<LibraryItemId>
{
    public static LibraryItemId New() => new(Guid.CreateVersion7());

    public static LibraryItemId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static LibraryItemId FromGuid(Guid value) => new(value);

    public static LibraryItemId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(LibraryItemId id) => id.Value;

    public static explicit operator LibraryItemId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(LibraryItemId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(LibraryItemId left, LibraryItemId right) => left.CompareTo(right) < 0;
    public static bool operator <=(LibraryItemId left, LibraryItemId right) => left.CompareTo(right) <= 0;
    public static bool operator >(LibraryItemId left, LibraryItemId right) => left.CompareTo(right) > 0;
    public static bool operator >=(LibraryItemId left, LibraryItemId right) => left.CompareTo(right) >= 0;

    public static LibraryItemId Parse(string s) => EntityIdOperations.Parse<LibraryItemId>(s);

    public static LibraryItemId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<LibraryItemId>(s, provider);

    public static LibraryItemId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<LibraryItemId>(s);

    public static LibraryItemId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<LibraryItemId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out LibraryItemId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out LibraryItemId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out LibraryItemId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LibraryItemId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

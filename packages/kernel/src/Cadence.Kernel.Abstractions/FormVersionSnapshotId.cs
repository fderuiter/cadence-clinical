using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an immutable FormVersionSnapshot in the Clinical Kernel.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<FormVersionSnapshotId>))]
[JsonConverter(typeof(EntityIdJsonConverter<FormVersionSnapshotId>))]
public readonly record struct FormVersionSnapshotId(Guid Value) : IEntityId<FormVersionSnapshotId>
{
    public static FormVersionSnapshotId New() => new(Guid.CreateVersion7());

    public static FormVersionSnapshotId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static FormVersionSnapshotId FromGuid(Guid value) => new(value);

    public static FormVersionSnapshotId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(FormVersionSnapshotId id) => id.Value;

    public static explicit operator FormVersionSnapshotId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(FormVersionSnapshotId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) < 0;
    public static bool operator <=(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) <= 0;
    public static bool operator >(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) > 0;
    public static bool operator >=(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) >= 0;

    public static FormVersionSnapshotId Parse(string s) => EntityIdOperations.Parse<FormVersionSnapshotId>(s);

    public static FormVersionSnapshotId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<FormVersionSnapshotId>(s, provider);

    public static FormVersionSnapshotId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<FormVersionSnapshotId>(s);

    public static FormVersionSnapshotId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<FormVersionSnapshotId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out FormVersionSnapshotId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out FormVersionSnapshotId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out FormVersionSnapshotId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out FormVersionSnapshotId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

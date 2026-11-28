using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed identifier for a CollectionInstance (spec §8.1, K-FACT-6): the observations collected together for one
/// participant under one CollectionSpec version, the kernel side of an eCRF form instance (ADR-2622).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<CollectionInstanceId>))]
[JsonConverter(typeof(EntityIdJsonConverter<CollectionInstanceId>))]
public readonly record struct CollectionInstanceId(Guid Value) : IEntityId<CollectionInstanceId>
{
    public static CollectionInstanceId New() => new(Guid.CreateVersion7());

    public static CollectionInstanceId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static CollectionInstanceId FromGuid(Guid value) => new(value);

    public static CollectionInstanceId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(CollectionInstanceId id) => id.Value;

    public static explicit operator CollectionInstanceId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(CollectionInstanceId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(CollectionInstanceId left, CollectionInstanceId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CollectionInstanceId left, CollectionInstanceId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CollectionInstanceId left, CollectionInstanceId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CollectionInstanceId left, CollectionInstanceId right) => left.CompareTo(right) >= 0;

    public static CollectionInstanceId Parse(string s) => EntityIdOperations.Parse<CollectionInstanceId>(s);

    public static CollectionInstanceId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<CollectionInstanceId>(s, provider);

    public static CollectionInstanceId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<CollectionInstanceId>(s);

    public static CollectionInstanceId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<CollectionInstanceId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out CollectionInstanceId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out CollectionInstanceId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CollectionInstanceId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CollectionInstanceId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

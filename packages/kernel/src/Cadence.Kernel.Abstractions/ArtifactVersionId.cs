using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for one immutable version of an Artifact (spec §8.3). Evidence references and
/// attestations name a version, never the artifact alone.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ArtifactVersionId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ArtifactVersionId>))]
public readonly record struct ArtifactVersionId(Guid Value) : IEntityId<ArtifactVersionId>
{
    public static ArtifactVersionId New() => new(Guid.CreateVersion7());

    public static ArtifactVersionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ArtifactVersionId FromGuid(Guid value) => new(value);

    public static ArtifactVersionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ArtifactVersionId id) => id.Value;

    public static explicit operator ArtifactVersionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ArtifactVersionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ArtifactVersionId left, ArtifactVersionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ArtifactVersionId left, ArtifactVersionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ArtifactVersionId left, ArtifactVersionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ArtifactVersionId left, ArtifactVersionId right) => left.CompareTo(right) >= 0;

    public static ArtifactVersionId Parse(string s) => EntityIdOperations.Parse<ArtifactVersionId>(s);

    public static ArtifactVersionId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ArtifactVersionId>(s, provider);

    public static ArtifactVersionId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ArtifactVersionId>(s);

    public static ArtifactVersionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ArtifactVersionId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ArtifactVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ArtifactVersionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ArtifactVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ArtifactVersionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

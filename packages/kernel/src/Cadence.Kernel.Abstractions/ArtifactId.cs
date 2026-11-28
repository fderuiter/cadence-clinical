using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an Artifact: versioned, hash-addressed evidence content such as a signed
/// consent form, an ethics approval letter or a lab report (spec §8.3).
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ArtifactId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ArtifactId>))]
public readonly record struct ArtifactId(Guid Value) : IEntityId<ArtifactId>
{
    public static ArtifactId New() => new(Guid.CreateVersion7());

    public static ArtifactId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ArtifactId FromGuid(Guid value) => new(value);

    public static ArtifactId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ArtifactId id) => id.Value;

    public static explicit operator ArtifactId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ArtifactId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ArtifactId left, ArtifactId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ArtifactId left, ArtifactId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ArtifactId left, ArtifactId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ArtifactId left, ArtifactId right) => left.CompareTo(right) >= 0;

    public static ArtifactId Parse(string s) => EntityIdOperations.Parse<ArtifactId>(s);

    public static ArtifactId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ArtifactId>(s, provider);

    public static ArtifactId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ArtifactId>(s);

    public static ArtifactId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ArtifactId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ArtifactId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ArtifactId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ArtifactId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ArtifactId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

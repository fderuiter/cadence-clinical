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
[TypeConverter(typeof(ArtifactIdTypeConverter))]
[JsonConverter(typeof(ArtifactIdJsonConverter))]
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

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ArtifactId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ArtifactId)}", nameof(obj))
    };

    public static bool operator <(ArtifactId left, ArtifactId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ArtifactId left, ArtifactId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ArtifactId left, ArtifactId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ArtifactId left, ArtifactId right) => left.CompareTo(right) >= 0;

    public static ArtifactId Parse(string s) => Parse(s, null);

    public static ArtifactId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ArtifactId(Guid.Parse(s, provider));
    }

    public static ArtifactId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ArtifactId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ArtifactId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ArtifactId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ArtifactId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ArtifactId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ArtifactId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ArtifactId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ArtifactIdTypeConverter : EntityIdTypeConverter<ArtifactId>;

public sealed class ArtifactIdJsonConverter : EntityIdJsonConverter<ArtifactId>;

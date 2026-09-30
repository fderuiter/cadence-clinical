using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Identifier of one validation finding (spec §10.3). A finding is identified by its rule and target across runs
/// (K-FIND-1), so its identifier is derived from them with <see cref="For"/>: the same rule and target in the same
/// tenant, study and stage always give the same identifier, and every run that detects the problem names the same
/// finding.
/// </summary>
[TypeConverter(typeof(ValidationFindingIdTypeConverter))]
[JsonConverter(typeof(ValidationFindingIdJsonConverter))]
public readonly record struct ValidationFindingId(Guid Value) : IEntityId<ValidationFindingId>
{
    /// <summary>A random identifier. Findings use <see cref="For"/>; this exists for the identifier contract.</summary>
    public static ValidationFindingId New() => new(Guid.CreateVersion7());

    public static ValidationFindingId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    /// <summary>
    /// The identifier of the finding for <paramref name="identity"/> in <paramref name="tenant"/>: a version 8 UUID
    /// made from the first 16 bytes of SHA-256 over the identity's components, each length-prefixed so no two
    /// identities share an input.
    /// </summary>
    public static ValidationFindingId For(TenantId tenant, FindingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var text = new StringBuilder("validation_finding/v1");
        foreach (var part in identity.Components(tenant))
        {
            text.Append('|').Append(part.Length).Append(':').Append(part);
        }

        Span<byte> bytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()), bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new(new Guid(bytes[..16], bigEndian: true));
    }

    public static ValidationFindingId FromGuid(Guid value) => new(value);

    public static ValidationFindingId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ValidationFindingId id) => id.Value;

    public static explicit operator ValidationFindingId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ValidationFindingId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ValidationFindingId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ValidationFindingId)}", nameof(obj))
    };

    public static bool operator <(ValidationFindingId left, ValidationFindingId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ValidationFindingId left, ValidationFindingId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ValidationFindingId left, ValidationFindingId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ValidationFindingId left, ValidationFindingId right) => left.CompareTo(right) >= 0;

    public static ValidationFindingId Parse(string s) => Parse(s, null);

    public static ValidationFindingId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ValidationFindingId(Guid.Parse(s, provider));
    }

    public static ValidationFindingId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ValidationFindingId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ValidationFindingId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ValidationFindingId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ValidationFindingId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ValidationFindingId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ValidationFindingId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ValidationFindingId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ValidationFindingIdTypeConverter : EntityIdTypeConverter<ValidationFindingId>;

public sealed class ValidationFindingIdJsonConverter : EntityIdJsonConverter<ValidationFindingId>;

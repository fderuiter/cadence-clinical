using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Identifier of a protocol element (an objective, an arm, a collection item, ...) as a lineage node (ADR-2474). An
/// element keeps its id across the versions of a study's protocol (K-PROT-1), so its node id is derived from the study
/// and the element id with <see cref="For"/>, and every version of the protocol names the same object. The node's
/// version is the number of the protocol version that last changed the element.
/// </summary>
[TypeConverter(typeof(EntityIdTypeConverter<ProtocolElementId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ProtocolElementId>))]
public readonly record struct ProtocolElementId(Guid Value) : IEntityId<ProtocolElementId>
{
    /// <summary>
    /// The element <paramref name="elementId"/> of <paramref name="study"/>'s protocol: a name-based UUID (RFC 9562
    /// version 8) from the SHA-256 of the study and the element id, so the same element always has the same id.
    /// </summary>
    public static ProtocolElementId For(StudyId study, string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        if (study.IsEmpty)
        {
            throw new ArgumentException("A protocol element belongs to a study.", nameof(study));
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence.protocol_element\n{study.Value:D}\n{elementId}"), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new ProtocolElementId(new Guid(hash[..16], bigEndian: true));
    }

    /// <summary>A random identifier, required by <see cref="IEntityId{TSelf}"/>; elements use <see cref="For"/>.</summary>
    public static ProtocolElementId New() => new(Guid.CreateVersion7());

    public static ProtocolElementId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ProtocolElementId FromGuid(Guid value) => new(value);

    public static ProtocolElementId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ProtocolElementId id) => id.Value;

    public static explicit operator ProtocolElementId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ProtocolElementId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ProtocolElementId left, ProtocolElementId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProtocolElementId left, ProtocolElementId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProtocolElementId left, ProtocolElementId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProtocolElementId left, ProtocolElementId right) => left.CompareTo(right) >= 0;

    public static ProtocolElementId Parse(string s) => EntityIdOperations.Parse<ProtocolElementId>(s);

    public static ProtocolElementId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ProtocolElementId>(s, provider);

    public static ProtocolElementId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ProtocolElementId>(s);

    public static ProtocolElementId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ProtocolElementId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ProtocolElementId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProtocolElementId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProtocolElementId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProtocolElementId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}

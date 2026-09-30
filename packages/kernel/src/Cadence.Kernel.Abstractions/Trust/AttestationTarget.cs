using System;

namespace Cadence.Kernel.Trust;

/// <summary>
/// The exact record an attestation signs (spec §9.5): its kind, id and the SHA-256 content hash of the signed
/// content. Binding the hash means a signed record cannot change without the signature no longer matching
/// (21 CFR 11.70).
/// @req:PRD-SYS-053
/// </summary>
/// <param name="Kind">The target's kind, for example <see cref="ProtocolVersionKind"/>.</param>
/// <param name="Id">The target's id.</param>
/// <param name="ContentHash">Lower-case hex SHA-256 of the signed content.</param>
public sealed record AttestationTarget(string Kind, Guid Id, string ContentHash)
{
    /// <summary>The target kind for a protocol version.</summary>
    public const string ProtocolVersionKind = "ProtocolVersion";
}

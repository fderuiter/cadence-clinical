using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// 21 CFR Part 11 cryptographic electronic signature attestation manifest.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record SignatureManifest(
    [property: JsonPropertyName("signer_id")] string SignerId,
    [property: JsonPropertyName("signer_name")] string SignerName,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("signing_reason")] string SigningReason,
    [property: JsonPropertyName("payload_hash")] string PayloadHash,
    [property: JsonPropertyName("hmac_signature")] string? HmacSignature,
    [property: JsonPropertyName("signed_at")] DateTime SignedAt)
{
    /// <summary>
    /// Convenience factory taking the signer's id as their display name when none is given. A factory rather
    /// than a second public constructor, so System.Text.Json still has exactly one constructor to bind.
    /// </summary>
    public static SignatureManifest Create(
        string SignerId,
        string SignerRole,
        string SigningReason,
        DateTime SignedAt,
        string PayloadHash,
        string? SignerName = null,
        string? HmacSignature = null) =>
        new(SignerId, SignerName ?? SignerId, SignerRole, SigningReason, PayloadHash, HmacSignature, SignedAt);
}

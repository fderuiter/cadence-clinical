using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// 21 CFR Part 11 cryptographic electronic signature attestation manifest.
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
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
    public SignatureManifest(
        string SignerId,
        string SignerRole,
        string SigningReason,
        DateTime SignedAt,
        string PayloadHash,
        string? SignerName = null,
        string? HmacSignature = null)
        : this(SignerId, SignerName ?? SignerId, SignerRole, SigningReason, PayloadHash, HmacSignature, SignedAt)
    {
    }
}

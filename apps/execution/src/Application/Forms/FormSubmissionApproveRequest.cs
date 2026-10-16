using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// A Principal Investigator's request to sign one form (21 CFR 11.50, 11.70). The signer proves identity with a
/// single-use step-up token (ADR-2515) bound to this form and to <see cref="ApprovalAttestation.PayloadHash"/>; the
/// server takes the signer and role from the verified caller, stamps the time from its clock and seals the manifest with
/// its own key (#5403). The request carries no signature of its own.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record FormSubmissionApproveRequest(
    [property: JsonPropertyName("signing_reason")] string SigningReason,
    [property: JsonPropertyName("signature_manifest")] ApprovalAttestation SignatureManifest)
{
    /// <summary>
    /// Convenience factory building the attestation from flat fields. A factory rather than a second public
    /// constructor, so System.Text.Json still has exactly one constructor to bind the request body with.
    /// </summary>
    public static FormSubmissionApproveRequest Create(
        string SignerId,
        string SigningReason,
        string PayloadHash,
        string? SignerName = null) =>
        new(SigningReason, new ApprovalAttestation(SignerId, SignerName, PayloadHash));
}

/// <summary>
/// What the signer attests to: the hash of the form content they reviewed (RFC 8785 canonical JSON, SHA-256, lowercase
/// hex). <see cref="SignerId"/> may restate the caller's own id but never names anyone else.
/// </summary>
public sealed record ApprovalAttestation(
    [property: JsonPropertyName("signer_id")] string? SignerId,
    [property: JsonPropertyName("signer_name")] string? SignerName,
    [property: JsonPropertyName("payload_hash")] string PayloadHash);

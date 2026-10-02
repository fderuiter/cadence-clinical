using System;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Forms;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Ingestion DTO for PI electronic signature sign-off.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record FormSubmissionApproveRequest(
    [property: JsonPropertyName("signing_reason")] string SigningReason,
    [property: JsonPropertyName("signature_manifest")] SignatureManifest SignatureManifest)
{
    /// <summary>
    /// Convenience factory building the manifest from flat fields. A factory rather than a second public
    /// constructor, so System.Text.Json still has exactly one constructor to bind the request body with.
    /// </summary>
    public static FormSubmissionApproveRequest Create(
        string SignerId,
        string SignerRole,
        string SigningReason,
        DateTime SignedAt,
        string PayloadHash,
        string? SignerName = null,
        string? HmacSignature = null) =>
        new(SigningReason, SignatureManifest.Create(SignerId, SignerRole, SigningReason, SignedAt, PayloadHash, SignerName, HmacSignature));
}

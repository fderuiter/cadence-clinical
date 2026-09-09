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
    public FormSubmissionApproveRequest(
        string SignerId,
        string SignerRole,
        string SigningReason,
        DateTime SignedAt,
        string PayloadHash,
        string? SignerName = null)
        : this(SigningReason, new SignatureManifest(SignerId, SignerName ?? SignerId, SignerRole, SigningReason, PayloadHash, null, SignedAt))
    {
    }
}

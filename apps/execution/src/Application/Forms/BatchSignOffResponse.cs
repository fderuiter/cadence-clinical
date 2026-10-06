using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Response DTO returning outcome and certification details for batch electronic sign-off.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record BatchSignOffResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("approved_submission_ids")] IReadOnlyList<string> ApprovedSubmissionIds,
    [property: JsonPropertyName("skipped_submission_ids")] IReadOnlyList<string> SkippedSubmissionIds,
    [property: JsonPropertyName("signature_id")] string? SignatureId = null,
    [property: JsonPropertyName("study_id")] string? StudyId = null,
    [property: JsonPropertyName("subject_id")] string? SubjectId = null,
    [property: JsonPropertyName("signed_forms_count")] int SignedFormsCount = 0,
    [property: JsonPropertyName("content_digest")] string? ContentDigest = null,
    [property: JsonPropertyName("timestamp_utc")] string? TimestampUtc = null,
    [property: JsonPropertyName("audit_tx")] string? AuditTx = null
);

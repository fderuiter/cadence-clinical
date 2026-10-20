using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Response DTO for batch electronic sign-off. Every identifier in it names a stored record: a signature manifest is
/// stored on the form submission it signs, so each entry of <see cref="Signatures"/> gives the form submission id
/// under which <c>GET /api/v1/execution/forms/{id}</c> returns that manifest. The service mints no certificate
/// serial or audit transaction id of its own (#5468).
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record BatchSignOffResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("approved_submission_ids")] IReadOnlyList<string> ApprovedSubmissionIds,
    [property: JsonPropertyName("skipped_submission_ids")] IReadOnlyList<string> SkippedSubmissionIds,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("signed_forms_count")] int SignedFormsCount,
    [property: JsonPropertyName("signed_at")] DateTime SignedAt,
    [property: JsonPropertyName("signatures")] IReadOnlyList<BatchSignOffSignature> Signatures
);

/// <summary>
/// One signature a batch sign-off stored: the form submission that carries the signature manifest, and the
/// manifest's payload hash, which identifies the content signed.
/// </summary>
public sealed record BatchSignOffSignature(
    [property: JsonPropertyName("form_submission_id")] string FormSubmissionId,
    [property: JsonPropertyName("payload_hash")] string PayloadHash
);

using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Forms;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Response DTO returning FormSubmission state, payload, and signature manifest.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record FormSubmissionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("form_id")] string FormId,
    [property: JsonPropertyName("status")] FormSubmissionStatus Status,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion = null,
    [property: JsonPropertyName("payload")] string? Payload = null,
    [property: JsonPropertyName("signature_manifest")] SignatureManifest? SignatureManifest = null,
    [property: JsonPropertyName("is_active")] bool IsActive = true,
    [property: JsonPropertyName("is_readonly")] bool IsReadonly = false,
    [property: JsonPropertyName("cloned_from_id")] string? ClonedFromId = null,
    [property: JsonPropertyName("version")] long Version = 1
);

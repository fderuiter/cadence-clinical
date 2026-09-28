using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Ingestion DTO for creating an eCRF form submission.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public sealed record FormSubmissionCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("form_id")] string FormId,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion = null,
    [property: JsonPropertyName("payload")] string? Payload = null
);

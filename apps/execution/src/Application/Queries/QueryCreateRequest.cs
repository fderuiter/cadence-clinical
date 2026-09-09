using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for raising a clinical discrepancy query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("status")] string? Status = "OPEN",
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("domain")] string? Domain = null,
    [property: JsonPropertyName("observation_id")] string? ObservationId = null,
    [property: JsonPropertyName("field_link")] string? FieldLink = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("origin")] string? Origin = "MANUAL",
    [property: JsonPropertyName("priority")] string? Priority = "MEDIUM",
    [property: JsonPropertyName("rule_id")] string? RuleId = null,
    [property: JsonPropertyName("form_id")] string? FormId = null,
    [property: JsonPropertyName("field_id")] string? FieldId = null,
    [property: JsonPropertyName("query_type")] string? QueryType = null,
    [property: JsonPropertyName("action_required")] string? ActionRequired = null
);

using System;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Queries;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Response DTO returning details of a clinical discrepancy query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record ClinicalQueryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("status")] QueryStatus Status,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("domain")] string? Domain = null,
    [property: JsonPropertyName("explanation")] string? Explanation = null,
    [property: JsonPropertyName("response")] string? Response = null,
    [property: JsonPropertyName("observation_id")] string? ObservationId = null,
    [property: JsonPropertyName("field_link")] string? FieldLink = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("origin")] string? Origin = null,
    [property: JsonPropertyName("priority")] string? Priority = null,
    [property: JsonPropertyName("rule_id")] string? RuleId = null,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null,
    [property: JsonPropertyName("responder")] string? Responder = null,
    [property: JsonPropertyName("resolver")] string? Resolver = null,
    [property: JsonPropertyName("resolved_at")] DateTime? ResolvedAt = null,
    [property: JsonPropertyName("cancellation_reason")] string? CancellationReason = null,
    [property: JsonPropertyName("escalated_at")] DateTime? EscalatedAt = null,
    [property: JsonPropertyName("form_id")] string? FormId = null,
    [property: JsonPropertyName("field_id")] string? FieldId = null,
    [property: JsonPropertyName("query_type")] string? QueryType = null,
    [property: JsonPropertyName("action_required")] string? ActionRequired = null,
    [property: JsonPropertyName("created_at")] DateTime? CreatedAt = null,
    [property: JsonPropertyName("version")] long Version = 1
);

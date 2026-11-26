using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Tickets.Endpoints;

/// <summary>
/// Request payload for <c>POST /api/v1/tickets</c>.
/// Note: C# record property names avoid raw <c>*StudyId</c>/<c>*SiteId</c>/<c>*ParticipantId</c> suffixes per K-XS-1
/// while preserving snake_case JSON field names on the wire. Caller identity is never accepted from the body.
/// </summary>
public sealed record CreateTicketRequestDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string? Category = "SYSTEM_SUPPORT",
    [property: JsonPropertyName("gxp_severity")] string? GxpSeverity = "MINOR",
    [property: JsonPropertyName("priority")] string? Priority = "MEDIUM",
    [property: JsonPropertyName("study_id")] string? StudyScope = null,
    [property: JsonPropertyName("site_id")] string? SiteScope = null,
    [property: JsonPropertyName("subject_id")] string? SubjectCode = null,
    [property: JsonPropertyName("entity_type")] string? EntityType = null,
    [property: JsonPropertyName("entity_id")] string? EntityId = null,
    [property: JsonPropertyName("source_route")] string? SourceRoute = null,
    [property: JsonPropertyName("persona")] string? Persona = null);

public sealed record UpdateTicketRequestDto(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("priority")] string? Priority = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser = null,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode = null,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary = null,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory = null,
    [property: JsonPropertyName("expected_version_index")] int? ExpectedVersionIndex = null);

public sealed record TicketResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("gxp_severity")] string GxpSeverity,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("assigned_queue")] string AssignedQueue,
    [property: JsonPropertyName("assignee_role")] string? AssigneeRole,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser,
    [property: JsonPropertyName("reporter")] string Reporter,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("study_id")] string? StudyScope,
    [property: JsonPropertyName("site_id")] string? SiteScope,
    [property: JsonPropertyName("subject_id")] string? SubjectCode,
    [property: JsonPropertyName("related_entity_type")] string? RelatedEntityType,
    [property: JsonPropertyName("related_entity_id")] string? RelatedEntityId,
    [property: JsonPropertyName("source_route")] string? SourceRoute,
    [property: JsonPropertyName("due_date")] DateTimeOffset? DueDate,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("sla_target_at")] DateTimeOffset? SlaTargetAt,
    [property: JsonPropertyName("sla_paused_at")] DateTimeOffset? SlaPausedAt,
    [property: JsonPropertyName("sla_total_paused_seconds")] int SlaTotalPausedSeconds,
    [property: JsonPropertyName("sla_breached")] bool SlaBreached,
    [property: JsonPropertyName("sla_amber_warned")] bool SlaAmberWarned,
    [property: JsonPropertyName("escalation_count")] int EscalationCount,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary,
    [property: JsonPropertyName("signature_action")] string? SignatureAction,
    [property: JsonPropertyName("signature_token")] string? SignatureToken,
    [property: JsonPropertyName("signature_meaning")] string? SignatureMeaning,
    [property: JsonPropertyName("signature_timestamp")] DateTimeOffset? SignatureTimestamp,
    [property: JsonPropertyName("signature_user")] string? SignatureUser,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record CreateTicketCommentRequestDto(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("visibility")] string? Visibility = "PUBLIC");

public sealed record TicketCommentResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ticket_id")] string TicketId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record TransitionTicketRequestDto(
    [property: JsonPropertyName("target_status")] string TargetStatus,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode = null,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary = null,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory = null,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser = null);

public sealed record SignTicketRequestDto(
    [property: JsonPropertyName("signature_meaning")] string SignatureMeaning,
    [property: JsonPropertyName("signature_action")] string? SignatureAction = "SIGN_CLOSURE");

public sealed record TicketAuditEntryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("entity_name")] string EntityName,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("changes_json")] string? ChangesJson,
    [property: JsonPropertyName("predecessor_hash")] string PredecessorHash,
    [property: JsonPropertyName("block_hash")] string BlockHash);

internal sealed record StoredTicketContext(
    [property: JsonPropertyName("assigned_queue")] string? AssignedQueue,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("source_route")] string? SourceRoute,
    [property: JsonPropertyName("persona")] string? Persona,
    [property: JsonPropertyName("subject_code")] string? SubjectCode);

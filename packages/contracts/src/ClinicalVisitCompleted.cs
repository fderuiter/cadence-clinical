using System;
using System.Text.Json.Serialization;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when a clinical monitoring visit report is completed or signed off.
/// Written to the transactional outbox table for downstream event processing (such as automated site payments).
/// @req:PRD-SYS-001
/// </summary>
/// <param name="EventId">Unique event identifier from the publishing system (used as trigger event identifier for idempotency).</param>
/// <param name="Study">Study protocol identifier.</param>
/// <param name="Site">Clinical study site identifier.</param>
/// <param name="VisitCode">Clinical visit code or monitoring visit type (e.g., IMV, SIV, V1_SCREENING).</param>
/// <param name="CompletedAt">Timestamp when the monitoring visit report was completed or signed off.</param>
/// <param name="CraId">Optional identifier of the Clinical Research Associate who performed the visit.</param>
/// <param name="Signer">Optional identifier of the supervisor who signed off on the visit report.</param>
/// <param name="SubjectId">Optional participant or subject identifier when the visit relates to a specific subject.</param>
/// <param name="VisitId">Optional monitoring visit identifier from the source system.</param>
public sealed record ClinicalVisitCompleted(
    string EventId,
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("site_id")] string Site,
    string VisitCode,
    DateTimeOffset CompletedAt,
    string? CraId = null,
    string? Signer = null,
    string? SubjectId = null,
    string? VisitId = null
);

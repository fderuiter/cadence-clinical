using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Ctms.Endpoints;

public record MonitoringVisitResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("cra_id")] string CraId,
    [property: JsonPropertyName("visit_type")] string VisitType,
    [property: JsonPropertyName("scheduled_date")] string ScheduledDate,
    [property: JsonPropertyName("actual_date")] string? ActualDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("signature_manifestation")] string? SignatureManifestation = null,
    [property: JsonPropertyName("signer")] string? Signer = null,
    [property: JsonPropertyName("signing_timestamp")] string? SigningTimestamp = null
);

public record MonitoringVisitCreateDto(
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("cra_id")] string CraId,
    [property: JsonPropertyName("visit_type")] string VisitType,
    [property: JsonPropertyName("scheduled_date")] DateTimeOffset ScheduledDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

public record FindingCreateDto(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("resolution_status")] string? ResolutionStatus = null
);

public record MonitoringVisitCompleteDto(
    [property: JsonPropertyName("actual_date")] DateTimeOffset ActualDate,
    [property: JsonPropertyName("findings")] List<FindingCreateDto>? Findings = null,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

public record GeneratedLetterResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("visit_id")] string VisitId,
    [property: JsonPropertyName("letter_type")] string LetterType,
    [property: JsonPropertyName("rendered_content")] string RenderedContent,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex
);

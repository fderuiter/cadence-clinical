using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Ctms.Endpoints;

public record SiteMilestoneResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("milestone_type")] string MilestoneType,
    [property: JsonPropertyName("planned_date")] string? PlannedDate,
    [property: JsonPropertyName("actual_date")] string? ActualDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex
);

public record SiteMilestoneCreateDto(
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("milestone_type")] string MilestoneType,
    [property: JsonPropertyName("planned_date")] DateTimeOffset? PlannedDate = null,
    [property: JsonPropertyName("actual_date")] DateTimeOffset? ActualDate = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

public record SiteMilestoneUpdateDto(
    [property: JsonPropertyName("planned_date")] DateTimeOffset? PlannedDate = null,
    [property: JsonPropertyName("actual_date")] DateTimeOffset? ActualDate = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel.Tenancy;

namespace Cadence.Apps.Ctms.Endpoints;

public record DelegationTaskRequest(
    [property: JsonPropertyName("site_id")] KernelSiteCode Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] KernelStudyCode? Study = null
);

public record RevokeDelegationRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] KernelStudyCode? Study = null,
    [property: JsonPropertyName("expected_version_index")] long? ExpectedVersionIndex = null
);

public record DoaSignOffRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] KernelStudyCode? Study = null,
    [property: JsonPropertyName("expected_version_index")] long? ExpectedVersionIndex = null
);

public record DelegationResponseDto(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("site_id")] KernelSiteCode Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("end_date")] string? EndDate,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("signed_off")] bool SignedOff,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("kernel_grant_id")] Guid? KernelGrantId = null
);

public record DoaAuditHistoryItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("details")] string Details
);

public record DoaLogResponseDto(
    [property: JsonPropertyName("site_id")] KernelSiteCode Site,
    [property: JsonPropertyName("pi_name")] string PiName,
    [property: JsonPropertyName("delegated_staff")] List<DelegationResponseDto> DelegatedStaff,
    [property: JsonPropertyName("audit_history")] List<DoaAuditHistoryItemDto> AuditHistory
);

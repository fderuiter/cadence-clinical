using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Locks;

/// <summary>
/// Request payload for pre-flight pre-lock discrepancy and source data validation check.
/// </summary>
public record PreflightCheckRequest(
    string ScopeType, // STUDY, SITE, SUBJECT, VISIT, FORM, FIELD
    string ScopeId,
    string StudyId,
    string? SiteId = null,
    string? SubjectId = null,
    string? VisitId = null,
    string? FormId = null,
    string? FieldName = null,
    string? Action = "HARD_LOCK" // FREEZE, HARD_LOCK
);

/// <summary>
/// Breakdown of discrepancy counts for a target scope hierarchy.
/// </summary>
public record DiscrepancyCounts(
    int OpenQueries,
    int UnansweredQueries,
    int UnverifiedSourceData,
    int TotalDiscrepancies
);

/// <summary>
/// Detailed pre-lock discrepancy item.
/// </summary>
public record PreLockDiscrepancyItem(
    string Id,
    string DiscrepancyType, // OPEN_QUERY, UNANSWERED_QUERY, UNVERIFIED_SOURCE_DATA
    string ScopeType,
    string ScopeId,
    string? SubjectId,
    string? VisitId,
    string? FormId,
    string? FieldId,
    string Description,
    string Severity,
    string Status
);

/// <summary>
/// Response payload for pre-flight validation check.
/// </summary>
public record PreflightCheckResponse(
    string ScopeType,
    string ScopeId,
    string Status, // PASSED, BLOCKED
    bool CanProceed,
    bool RequiresOverride,
    string Summary,
    DiscrepancyCounts DiscrepancyCounts,
    IReadOnlyList<PreLockDiscrepancyItem> Discrepancies,
    DateTimeOffset CheckedAt,
    double ExecutionTimeMs
);

/// <summary>
/// Request payload for executing data lock or freeze operation.
/// </summary>
public record DataLockExecuteRequest(
    string ScopeType,
    string ScopeId,
    string StudyId,
    string? SiteId = null,
    string? SubjectId = null,
    string? VisitId = null,
    string? FormId = null,
    string? FieldName = null,
    string Action = "HARD_LOCK", // FREEZE, HARD_LOCK
    string LockType = "HARD_LOCK",
    string ReasonForChange = "",
    string? SigToken = null,
    bool OverrideApproved = false,
    string? OverrideJustification = null
);

/// <summary>
/// Request payload for unlocking a data scope override.
/// </summary>
public record DataLockUnlockRequest(
    string? LockId,
    string ScopeType,
    string ScopeId,
    string Justification,
    string ReasonForChange = ""
);

/// <summary>
/// Response DTO representing a data lock state record.
/// </summary>
public record DataLockRecordResponse(
    string LockId,
    string ScopeType,
    string ScopeId,
    string StudyId,
    string? SiteId,
    string? SubjectId,
    string? VisitId,
    string? FormId,
    string? FieldName,
    string LockType,
    string Status,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ReasonForChange,
    string? PreflightSummary,
    int OpenQueryCount,
    int UnansweredQueryCount,
    int UnverifiedSourceDataCount,
    bool IsOverride,
    string? OverrideJustification,
    string? UnlockJustification,
    string? UnlockedBy,
    DateTimeOffset? UnlockedAt
);

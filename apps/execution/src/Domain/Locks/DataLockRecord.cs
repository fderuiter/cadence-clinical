using System;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Locks;

/// <summary>
/// Aggregate root representing a clinical data lock or freeze state record across the 6-tier hierarchy.
/// Stores lock state, 21 CFR Part 11 audit details, pre-flight discrepancy snapshots, and override justifications.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// </summary>
public sealed class DataLockRecord : AggregateRoot<string>
{
    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public SubjectCode? SubjectId { get; private set; }
    public string? VisitId { get; private set; }
    public string? FormId { get; private set; }
    public string? FieldName { get; private set; }

    public string ScopeType { get; private set; } = string.Empty; // STUDY, SITE, SUBJECT, VISIT, FORM, FIELD
    public string ScopeId { get; private set; } = string.Empty;
    public string LockType { get; private set; } = "HARD_LOCK"; // FROZEN, HARD_LOCK
    public string Status { get; private set; } = "LOCKED"; // FROZEN, LOCKED, UNLOCKED
    public bool IsActive { get; private set; } = true;

    public string? SigToken { get; private set; }

    // Pre-flight Discrepancy Engine Snapshot & Override Audit (21 CFR Part 11)
    public string? PreflightSummary { get; private set; }
    public int OpenQueryCount { get; private set; }
    public int UnansweredQueryCount { get; private set; }
    public int UnverifiedSourceDataCount { get; private set; }
    public bool IsOverride { get; private set; }
    public string? OverrideJustification { get; private set; }

    public string? UnlockJustification { get; private set; }
    public string? UnlockedBy { get; private set; }
    public DateTimeOffset? UnlockedAt { get; private set; }

    private DataLockRecord() { } // EF Core

    public static DataLockRecord Create(
        string scopeType,
        string scopeId,
        StudyCode studyId,
        SiteCode? siteId,
        SubjectCode? subjectId,
        string? visitId,
        string? formId,
        string? fieldName,
        string lockType,
        string reasonForChange,
        string createdBy,
        string? sigToken = null,
        string? preflightSummary = null,
        int openQueryCount = 0,
        int unansweredQueryCount = 0,
        int unverifiedSourceDataCount = 0,
        bool isOverride = false,
        string? overrideJustification = null)
    {
        var id = $"LOCK-{scopeType.ToUpperInvariant()}-{scopeId}-{Guid.NewGuid().ToString("N")[..8]}";
        var status = lockType.Equals("FROZEN", StringComparison.OrdinalIgnoreCase) ? "FROZEN" : "LOCKED";

        var record = new DataLockRecord
        {
            Id = id,
            ScopeType = scopeType.ToUpperInvariant(),
            ScopeId = scopeId,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            VisitId = visitId,
            FormId = formId,
            FieldName = fieldName,
            LockType = lockType.ToUpperInvariant(),
            Status = status,
            IsActive = true,
            ReasonForChange = reasonForChange.Trim(),
            CreatedBy = createdBy,
            SigToken = sigToken,
            PreflightSummary = preflightSummary,
            OpenQueryCount = openQueryCount,
            UnansweredQueryCount = unansweredQueryCount,
            UnverifiedSourceDataCount = unverifiedSourceDataCount,
            IsOverride = isOverride,
            OverrideJustification = overrideJustification?.Trim()
        };

        return record;
    }

    public Result<bool> Unlock(string unlockedBy, string justification, DateTimeOffset unlockedAt)
    {
        if (!IsActive && Status == "UNLOCKED")
        {
            return DomainError.Validation("ALREADY_UNLOCKED", "This data scope is already unlocked.");
        }

        if (string.IsNullOrWhiteSpace(justification) || justification.Trim().Length < 50)
        {
            return DomainError.Validation("INVALID_UNLOCK_JUSTIFICATION", "Mandatory unlock justification must be at least 50 characters long.");
        }

        IsActive = false;
        Status = "UNLOCKED";
        UnlockedBy = unlockedBy;
        UnlockJustification = justification.Trim();
        UnlockedAt = unlockedAt.ToUniversalTime();
        ReasonForChange = justification.Trim();
        return true;
    }

    public void Deactivate()
    {
        IsActive = false;
        Status = "UNLOCKED";
    }
}

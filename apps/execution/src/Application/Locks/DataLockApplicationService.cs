using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Locks;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Locks;

/// <summary>
/// Application service implementing 6-tier hierarchical data lock governance, automated pre-lock discrepancy enforcement, and 21 CFR Part 11 audit logging.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public sealed class DataLockApplicationService : IDataLockApplicationService
{
    private readonly ExecutionDbContext _dbContext;
    private readonly IPreLockValidationEngine _validationEngine;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _auditLogWriter;

    public DataLockApplicationService(
        ExecutionDbContext dbContext,
        IPreLockValidationEngine validationEngine,
        IClock clock,
        IAuditLogWriter auditLogWriter)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _validationEngine = validationEngine ?? throw new ArgumentNullException(nameof(validationEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _auditLogWriter = auditLogWriter ?? throw new ArgumentNullException(nameof(auditLogWriter));
    }

    public async Task<Result<PreflightCheckResponse>> PreflightCheckAsync(PreflightCheckRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return DomainError.Validation("INVALID_REQUEST", "Pre-flight check request cannot be null.");
        }

        var response = await _validationEngine.RunPreflightCheckAsync(request, ct).ConfigureAwait(false);
        return response;
    }

    public async Task<Result<DataLockRecordResponse>> LockAsync(DataLockExecuteRequest request, string callerUserId, CancellationToken ct = default)
    {
        if (request is null)
        {
            return DomainError.Validation("INVALID_REQUEST", "Lock request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.ReasonForChange))
        {
            return DomainError.Validation("REASON_REQUIRED", "GxP reason for change is mandatory for lock operations.");
        }

        // Run automated pre-lock discrepancy check across target scope hierarchy
        var preflightReq = new PreflightCheckRequest(
            ScopeType: request.ScopeType,
            ScopeId: request.ScopeId,
            StudyId: request.StudyId,
            SiteId: request.SiteId,
            SubjectId: request.SubjectId,
            VisitId: request.VisitId,
            FormId: request.FormId,
            FieldName: request.FieldName,
            Action: request.Action
        );

        var preflightRes = await _validationEngine.RunPreflightCheckAsync(preflightReq, ct).ConfigureAwait(false);

        var totalDiscrepancies = preflightRes.DiscrepancyCounts.TotalDiscrepancies;
        bool isOverride = totalDiscrepancies > 0 && request.OverrideApproved && !string.IsNullOrWhiteSpace(request.OverrideJustification);

        // Block lock execution if unresolved discrepancies exist and no approved override justification was provided
        if (totalDiscrepancies > 0 && !isOverride)
        {
            return DomainError.Validation(
                "PRE_LOCK_CHECK_BLOCKED",
                $"Database lock execution blocked: {totalDiscrepancies} open discrepancy(ies) ({preflightRes.DiscrepancyCounts.OpenQueries} open queries, {preflightRes.DiscrepancyCounts.UnverifiedSourceData} unverified source records) exist within target scope ({request.ScopeType}: {request.ScopeId}). An approved override justification is required to proceed.");
        }

        var studyCode = new StudyCode(request.StudyId);
        var siteCode = !string.IsNullOrWhiteSpace(request.SiteId) ? new SiteCode(request.SiteId) : (SiteCode?)null;
        var subjectCode = !string.IsNullOrWhiteSpace(request.SubjectId) ? new SubjectCode(request.SubjectId) : (SubjectCode?)null;

        var lockRecord = DataLockRecord.Create(
            scopeType: request.ScopeType,
            scopeId: request.ScopeId,
            studyId: studyCode,
            siteId: siteCode,
            subjectId: subjectCode,
            visitId: request.VisitId,
            formId: request.FormId,
            fieldName: request.FieldName,
            lockType: request.LockType,
            reasonForChange: request.ReasonForChange,
            createdBy: string.IsNullOrWhiteSpace(callerUserId) ? "System Data Manager" : callerUserId,
            sigToken: request.SigToken,
            preflightSummary: preflightRes.Summary,
            openQueryCount: preflightRes.DiscrepancyCounts.OpenQueries,
            unansweredQueryCount: preflightRes.DiscrepancyCounts.UnansweredQueries,
            unverifiedSourceDataCount: preflightRes.DiscrepancyCounts.UnverifiedSourceData,
            isOverride: isOverride,
            overrideJustification: isOverride ? request.OverrideJustification : null
        );

        _dbContext.DataLocks.Add(lockRecord);

        // Log Part 11 audit action
        var actionName = isOverride
            ? $"EXECUTE_OVERRIDE_LOCK_{request.ScopeType.ToUpperInvariant()}"
            : $"EXECUTE_LOCK_{request.ScopeType.ToUpperInvariant()}";

        var newValues = new Dictionary<string, object?>
        {
            ["LockId"] = lockRecord.Id,
            ["ScopeType"] = lockRecord.ScopeType,
            ["ScopeId"] = lockRecord.ScopeId,
            ["LockType"] = lockRecord.LockType,
            ["Status"] = lockRecord.Status,
            ["PreflightSummary"] = lockRecord.PreflightSummary,
            ["OpenQueryCount"] = lockRecord.OpenQueryCount,
            ["IsOverride"] = lockRecord.IsOverride,
            ["OverrideJustification"] = lockRecord.OverrideJustification,
            ["ReasonForChange"] = lockRecord.ReasonForChange
        };

        await _auditLogWriter.WriteAsync(
            entityName: nameof(DataLockRecord),
            recordId: lockRecord.Id,
            action: actionName,
            userId: callerUserId,
            changeReason: isOverride ? $"[OVERRIDE] {request.OverrideJustification}" : request.ReasonForChange,
            oldValues: null,
            newValues: newValues,
            ct: ct
        ).ConfigureAwait(false);

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapResponse(lockRecord);
    }

    public async Task<Result<DataLockRecordResponse>> UnlockAsync(DataLockUnlockRequest request, string callerUserId, CancellationToken ct = default)
    {
        if (request is null)
        {
            return DomainError.Validation("INVALID_REQUEST", "Unlock request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Trim().Length < 50)
        {
            return DomainError.Validation(
                "MANDATORY_JUSTIFICATION_REQUIRED",
                "21 CFR Part 11 unlock overrides require a detailed justification of at least 50 characters.");
        }

        DataLockRecord? record = null;
        if (!string.IsNullOrWhiteSpace(request.LockId))
        {
            record = await _dbContext.DataLocks
                .FirstOrDefaultAsync(l => l.Id == request.LockId && l.IsActive, ct)
                .ConfigureAwait(false);
        }

        if (record is null)
        {
            var targetScopeType = request.ScopeType.ToUpperInvariant();
            record = await _dbContext.DataLocks
                .Where(l => l.IsActive && l.ScopeType == targetScopeType && l.ScopeId == request.ScopeId)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        if (record is null)
        {
            return DomainError.NotFound("DataLockRecord", request.LockId ?? $"{request.ScopeType}:{request.ScopeId}");
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["Status"] = record.Status,
            ["IsActive"] = record.IsActive
        };

        var unlockResult = record.Unlock(callerUserId, request.Justification, _clock.UtcNow);
        if (unlockResult.IsFailure)
        {
            return unlockResult.Error!;
        }

        var newValues = new Dictionary<string, object?>
        {
            ["Status"] = record.Status,
            ["IsActive"] = record.IsActive,
            ["UnlockedBy"] = record.UnlockedBy,
            ["UnlockJustification"] = record.UnlockJustification,
            ["UnlockedAt"] = record.UnlockedAt
        };

        await _auditLogWriter.WriteAsync(
            entityName: nameof(DataLockRecord),
            recordId: record.Id,
            action: $"UNLOCK_OVERRIDE_{record.ScopeType}",
            userId: callerUserId,
            changeReason: request.Justification,
            oldValues: oldValues,
            newValues: newValues,
            ct: ct
        ).ConfigureAwait(false);

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapResponse(record);
    }

    public async Task<IReadOnlyList<DataLockRecordResponse>> ListLocksAsync(string? studyId = null, string? scopeType = null, string? scopeId = null, CancellationToken ct = default)
    {
        var query = _dbContext.DataLocks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            var studyCode = new StudyCode(studyId);
            query = query.Where(l => l.StudyId == studyCode);
        }

        if (!string.IsNullOrWhiteSpace(scopeType))
        {
            var typeUpper = scopeType.ToUpperInvariant();
            query = query.Where(l => l.ScopeType == typeUpper);
        }

        if (!string.IsNullOrWhiteSpace(scopeId))
        {
            query = query.Where(l => l.ScopeId == scopeId);
        }

        var records = await query.OrderByDescending(l => l.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return records.Select(MapResponse).ToList();
    }

    private static DataLockRecordResponse MapResponse(DataLockRecord record)
    {
        return new DataLockRecordResponse(
            LockId: record.Id,
            ScopeType: record.ScopeType,
            ScopeId: record.ScopeId,
            StudyId: record.StudyId.Value,
            SiteId: record.SiteId?.Value,
            SubjectId: record.SubjectId?.Value,
            VisitId: record.VisitId,
            FormId: record.FormId,
            FieldName: record.FieldName,
            LockType: record.LockType,
            Status: record.Status,
            IsActive: record.IsActive,
            CreatedBy: record.CreatedBy,
            CreatedAt: record.CreatedAt,
            ReasonForChange: record.ReasonForChange,
            PreflightSummary: record.PreflightSummary,
            OpenQueryCount: record.OpenQueryCount,
            UnansweredQueryCount: record.UnansweredQueryCount,
            UnverifiedSourceDataCount: record.UnverifiedSourceDataCount,
            IsOverride: record.IsOverride,
            OverrideJustification: record.OverrideJustification,
            UnlockJustification: record.UnlockJustification,
            UnlockedBy: record.UnlockedBy,
            UnlockedAt: record.UnlockedAt
        );
    }
}

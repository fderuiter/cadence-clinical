using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Application.Services;

public sealed class EsourceReconciliationService(InteropUnitOfWork uow, IClock clock)
{
    public async Task<IResult> AcceptCandidateAsync(
        EhrReconcileAcceptRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await uow.Db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        item.Status = "ACCEPTED";
        item.ResolutionAction = "ACCEPT_COMMIT";
        item.ResolutionReason = string.IsNullOrWhiteSpace(request.CommitReason) ? "CRC accepted candidate data from hospital EHR" : request.CommitReason;
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.CurrentEcrfValueJson = item.CandidateValueJson;
        item.CurrentEcrfUpdatedAt = now;
        item.DiscrepancyType = "EXACT_MATCH";
        item.VersionIndex += 1;

        var eSig = new
        {
            signed_by = callerUser,
            role = callerRole,
            signed_at = now,
            meaning = "21 CFR Part 11 eSignature - Accept & Commit eSource Record",
            reason = request.ChangeReason ?? "CRC eSource Reconciliation Accept & Commit"
        };
        item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

        uow.Db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_ACCEPTED",
            Details = $"Accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Accept & Commit"
        });

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: EsourceIngestionAndQueryService.ToDto(item),
            ClinicalQueryId: null,
            Message: $"Candidate {item.TargetVariable} accepted and committed to eCRF."));
    }

    public async Task<IResult> RejectCandidateAsync(
        EhrReconcileRejectRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem("Mandatory clinical rationale is required when rejecting candidate EHR records under GxP rules.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await uow.Db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        item.Status = "REJECTED";
        item.ResolutionAction = "REJECT";
        item.ResolutionReason = request.RejectionReason.Trim();
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.VersionIndex += 1;

        var eSig = new
        {
            signed_by = callerUser,
            role = callerRole,
            signed_at = now,
            meaning = "21 CFR Part 11 eSignature - Reject eSource Record",
            reason = request.RejectionReason
        };
        item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

        uow.Db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_REJECTED",
            Details = $"Rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Reject"
        });

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: EsourceIngestionAndQueryService.ToDto(item),
            ClinicalQueryId: null,
            Message: $"Candidate {item.TargetVariable} rejected with recorded justification."));
    }

    public async Task<IResult> BatchAcceptCandidatesAsync(
        EhrReconcileBatchAcceptRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return Results.Problem("CandidateIds array cannot be empty for batch accept.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CandidateIds.Count > 100)
        {
            return Results.Problem("Batch size exceeds maximum limit of 100 candidate items.", statusCode: StatusCodes.Status400BadRequest);
        }

        var distinctIds = request.CandidateIds.Distinct().ToList();

        var items = await uow.Db.EhrReconciliationCandidateRecords
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (items.Count != distinctIds.Count)
        {
            var missingIds = distinctIds.Except(items.Select(i => i.Id)).ToList();
            return Results.Problem($"Batch operation aborted. Candidate record(s) not found: {string.Join(", ", missingIds)}", statusCode: StatusCodes.Status404NotFound);
        }

        if (items.Exists(i => ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, i.StudyId, i.SiteId) is not null))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;
        var commitReason = string.IsNullOrWhiteSpace(request.CommitReason) ? "CRC batch accepted candidate data from hospital EHR" : request.CommitReason;

        foreach (var item in items)
        {
            item.Status = "ACCEPTED";
            item.ResolutionAction = "ACCEPT_COMMIT";
            item.ResolutionReason = commitReason;
            item.ResolvedBy = callerUser;
            item.ResolvedAt = now;
            item.CurrentEcrfValueJson = item.CandidateValueJson;
            item.CurrentEcrfUpdatedAt = now;
            item.DiscrepancyType = "EXACT_MATCH";
            item.VersionIndex += 1;

            var eSig = new
            {
                signed_by = callerUser,
                role = callerRole,
                signed_at = now,
                meaning = "21 CFR Part 11 eSignature - Batch Accept & Commit eSource Records",
                reason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Accept & Commit"
            };
            item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

            uow.Db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_ACCEPTED",
                Details = $"Batch accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Accept & Commit"
            });
        }

        await uow.CommitAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(EsourceIngestionAndQueryService.ToDto).ToList();
        return Results.Ok(new EhrReconcileBatchActionResponse(
            Success: true,
            ActionCount: dtos.Count,
            Candidates: dtos,
            Message: $"Successfully batch accepted {dtos.Count} candidate items."));
    }

    public async Task<IResult> BatchRejectCandidatesAsync(
        EhrReconcileBatchRejectRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return Results.Problem("CandidateIds array cannot be empty for batch reject.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CandidateIds.Count > 100)
        {
            return Results.Problem("Batch size exceeds maximum limit of 100 candidate items.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem("Mandatory clinical rationale is required when batch rejecting candidate EHR records under GxP rules.", statusCode: StatusCodes.Status400BadRequest);
        }

        var distinctIds = request.CandidateIds.Distinct().ToList();

        var items = await uow.Db.EhrReconciliationCandidateRecords
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (items.Count != distinctIds.Count)
        {
            var missingIds = distinctIds.Except(items.Select(i => i.Id)).ToList();
            return Results.Problem($"Batch operation aborted. Candidate record(s) not found: {string.Join(", ", missingIds)}", statusCode: StatusCodes.Status404NotFound);
        }

        if (items.Exists(i => ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, i.StudyId, i.SiteId) is not null))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        foreach (var item in items)
        {
            item.Status = "REJECTED";
            item.ResolutionAction = "REJECT";
            item.ResolutionReason = request.RejectionReason.Trim();
            item.ResolvedBy = callerUser;
            item.ResolvedAt = now;
            item.VersionIndex += 1;

            var eSig = new
            {
                signed_by = callerUser,
                role = callerRole,
                signed_at = now,
                meaning = "21 CFR Part 11 eSignature - Batch Reject eSource Records",
                reason = request.RejectionReason
            };
            item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

            uow.Db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_REJECTED",
                Details = $"Batch rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Reject"
            });
        }

        await uow.CommitAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(EsourceIngestionAndQueryService.ToDto).ToList();
        return Results.Ok(new EhrReconcileBatchActionResponse(
            Success: true,
            ActionCount: dtos.Count,
            Candidates: dtos,
            Message: $"Successfully batch rejected {dtos.Count} candidate items."));
    }

    public async Task<IResult> RaiseClinicalQueryAsync(
        EhrReconcileQueryRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.QueryExplanation))
        {
            return Results.Problem("Discrepancy explanation is required to raise a clinical query.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await uow.Db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        var query = new ClinicalQuery
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = item.StudyId,
            SubjectId = item.SubjectId,
            Domain = item.TargetDomain,
            TestCode = item.CdashTestcd ?? item.TargetVariable,
            Status = "OPEN",
            Explanation = request.QueryExplanation.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        uow.Db.ClinicalQueries.Add(query);

        item.ClinicalQueryId = query.Id;
        item.Status = "QUERIED";
        item.ResolutionAction = "QUERY";
        item.ResolutionReason = request.QueryExplanation.Trim();
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.VersionIndex += 1;

        uow.Db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CLINICAL_QUERY_RAISED",
            Details = $"Raised clinical query #{query.Id} for candidate {item.Id} ({item.TargetVariable}). Discrepancy: {request.QueryExplanation}",
            ChangeReason = request.ChangeReason ?? "CRC Raised eSource Discrepancy Query"
        });

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: EsourceIngestionAndQueryService.ToDto(item),
            ClinicalQueryId: query.Id,
            Message: $"Clinical query #{query.Id} opened for record {item.TargetVariable}."));
    }
}

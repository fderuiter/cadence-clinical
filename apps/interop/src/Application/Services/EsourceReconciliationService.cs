using System;
using System.Collections.Generic;
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

public interface IEsourceReconciliationService
{
    Task<IResult> ListCandidatesAsync(
        string? studyId,
        string? subjectId,
        string? siteId,
        string? domain,
        string? status,
        int skip,
        int limit,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetCandidateByIdAsync(string candidateId, HttpContext httpContext, CancellationToken ct);
    Task<IResult> AcceptCandidateAsync(EhrReconcileAcceptRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> RejectCandidateAsync(EhrReconcileRejectRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> BatchAcceptCandidatesAsync(EhrReconcileBatchAcceptRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> BatchRejectCandidatesAsync(EhrReconcileBatchRejectRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> RaiseClinicalQueryAsync(EhrReconcileQueryRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> GetProvenanceAuditAsync(string candidateId, HttpContext httpContext, CancellationToken ct);
}

public sealed class EsourceReconciliationService(
    InteropDbContext db,
    IInteropUnitOfWork unitOfWork,
    IClock clock) : IEsourceReconciliationService
{
    public async Task<IResult> ListCandidatesAsync(
        string? studyId,
        string? subjectId,
        string? siteId,
        string? domain,
        string? status,
        int skip,
        int limit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        HashSet<string>? siteFilter = null;
        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null
            && !ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out _, out siteFilter, out var scopeDenied))
        {
            return scopeDenied!;
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(db.EhrReconciliationCandidateRecords.AsNoTracking(), httpContext, c => c.StudyId);
        if (siteFilter is not null)
        {
            var allowedSites = siteFilter.ToList();
            query = query.Where(c => c.SiteId != null && allowedSites.Contains(c.SiteId));
        }

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(c => c.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(c => c.SubjectId == subjectId || c.SubjectPseudonym == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(c => c.SiteId == siteId);
        }

        if (!string.IsNullOrWhiteSpace(domain) && domain != "ALL")
        {
            query = query.Where(c => c.TargetDomain == domain);
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(c => c.Status == status);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(skip > 0 ? skip : 0)
            .Take(limit > 0 ? limit : 50)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrCandidateListResponse(dtos, total));
    }

    public async Task<IResult> GetCandidateByIdAsync(string candidateId, HttpContext httpContext, CancellationToken ct)
    {
        var item = await db.EhrReconciliationCandidateRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            .ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(ToDto(item));
    }

    public async Task<IResult> AcceptCandidateAsync(
        EhrReconcileAcceptRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords
            .FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct)
            .ConfigureAwait(false);
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
        item.ResolutionReason = string.IsNullOrWhiteSpace(request.CommitReason)
            ? "CRC accepted candidate data from hospital EHR"
            : request.CommitReason;
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

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_ACCEPTED",
            Details = $"Accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Accept & Commit"
        });

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
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
            return Results.Problem(
                "Mandatory clinical rationale is required when rejecting candidate EHR records under GxP rules.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords
            .FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct)
            .ConfigureAwait(false);
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

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_REJECTED",
            Details = $"Rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Reject"
        });

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
            ClinicalQueryId: null,
            Message: $"Candidate {item.TargetVariable} rejected with recorded justification."));
    }

    public async Task<IResult> BatchAcceptCandidatesAsync(
        EhrReconcileBatchAcceptRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (problem, batch) = await LoadAuthorizedBatchCandidatesAsync(
            request.CandidateIds,
            emptyError: "CandidateIds array cannot be empty for batch accept.",
            httpContext,
            ct).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        var (items, callerUser, callerRole, now) = batch!.Value;
        var commitReason = string.IsNullOrWhiteSpace(request.CommitReason)
            ? "CRC batch accepted candidate data from hospital EHR"
            : request.CommitReason;

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

            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_ACCEPTED",
                Details = $"Batch accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Accept & Commit"
            });
        }

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
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
        if (request.CandidateIds is { Count: > 0 and <= 100 } && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem(
                "Mandatory clinical rationale is required when batch rejecting candidate EHR records under GxP rules.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var (problem, batch) = await LoadAuthorizedBatchCandidatesAsync(
            request.CandidateIds,
            emptyError: "CandidateIds array cannot be empty for batch reject.",
            httpContext,
            ct).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        var (items, callerUser, callerRole, now) = batch!.Value;

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

            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_REJECTED",
                Details = $"Batch rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Reject"
            });
        }

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrReconcileBatchActionResponse(
            Success: true,
            ActionCount: dtos.Count,
            Candidates: dtos,
            Message: $"Successfully batch rejected {dtos.Count} candidate items."));
    }

    private async Task<(IResult? Problem, (List<EhrReconciliationCandidateRecord> Items, string CallerUser, string CallerRole, DateTimeOffset Now)? Batch)>
        LoadAuthorizedBatchCandidatesAsync(
            IReadOnlyList<string>? candidateIds,
            string emptyError,
            HttpContext httpContext,
            CancellationToken ct)
    {
        if (candidateIds == null || candidateIds.Count == 0)
        {
            return (Results.Problem(emptyError, statusCode: StatusCodes.Status400BadRequest), null);
        }

        if (candidateIds.Count > 100)
        {
            return (Results.Problem("Batch size exceeds maximum limit of 100 candidate items.", statusCode: StatusCodes.Status400BadRequest), null);
        }

        var distinctIds = candidateIds.Distinct().ToList();
        var items = await db.EhrReconciliationCandidateRecords
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (items.Count != distinctIds.Count)
        {
            var missingIds = distinctIds.Except(items.Select(i => i.Id)).ToList();
            return (
                Results.Problem(
                    $"Batch operation aborted. Candidate record(s) not found: {string.Join(", ", missingIds)}",
                    statusCode: StatusCodes.Status404NotFound),
                null);
        }

        if (items.Exists(i => ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, i.StudyId, i.SiteId) is not null))
        {
            return (ClinicalScopeGuard.OutsideStudy(), null);
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        return (null, (items, callerUser, callerRole, clock.UtcNow));
    }

    public Task<IResult> RaiseClinicalQueryAsync(
        EhrReconcileQueryRequest request,
        HttpContext httpContext,
        CancellationToken ct) =>
        EsourceQueryAndProvenanceHelper.RaiseClinicalQueryAsync(db, unitOfWork, clock, request, httpContext, ct);

    public Task<IResult> GetProvenanceAuditAsync(
        string candidateId,
        HttpContext httpContext,
        CancellationToken ct) =>
        EsourceQueryAndProvenanceHelper.GetProvenanceAuditAsync(db, candidateId, httpContext, ct);

    internal static EhrCandidateItemDto ToDto(EhrReconciliationCandidateRecord item) =>
        EsourceQueryAndProvenanceHelper.ToDto(item);
}

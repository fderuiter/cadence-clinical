using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Application;

public interface ITrainingCurriculumService
{
    Task<IResult> ListCurriculaAsync(
        string? studyId,
        string? protocolVersion,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetCurriculumAsync(
        string curriculumId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> ListAssignmentsAsync(
        string? studyId,
        string? siteId,
        string? userId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetAssignmentDetailAsync(
        string assignmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class TrainingCurriculumService(
    KnowledgeDbContext dbContext,
    IKnowledgeUnitOfWork unitOfWork,
    IClock clock) : ITrainingCurriculumService
{
    public const int MaxBatchLimit = 200;

    public async Task<IResult> ListCurriculaAsync(
        string? studyId,
        string? protocolVersion,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        var query = dbContext.TrainingCurricula.AsNoTracking().Where(c => c.IsActive);
        if (studies != null)
        {
            query = query.Where(c => studies.Contains(c.StudyId));
        }
        if (!string.IsNullOrEmpty(studyId))
        {
            query = query.Where(c => c.StudyId == studyId);
        }
        if (!string.IsNullOrEmpty(protocolVersion))
        {
            query = query.Where(c => c.ProtocolVersion == protocolVersion);
        }

        // An empty result is an empty list: nothing is invented when no curriculum exists (#5400).
        var list = await query.ToListAsync(cancellationToken);
        var dtos = list.Select(c => new TrainingCurriculumDto(
            c.Id, c.StudyId, c.ProtocolVersion, c.Title, c.Description, c.IsActive, c.CreatedAt)).ToList();

        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> GetCurriculumAsync(
        string curriculumId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // A missing curriculum and one in a study the caller is not assigned to get the same 404 (#5400).
        var c = await dbContext.TrainingCurricula.AsNoTracking().FirstOrDefaultAsync(x => x.Id == curriculumId, cancellationToken);
        if (c == null || !CallerScope.CanAccessStudy(httpContext, c.StudyId))
        {
            return TypedResults.Problem($"Curriculum '{curriculumId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new TrainingCurriculumDto(
            c.Id, c.StudyId, c.ProtocolVersion, c.Title, c.Description, c.IsActive, c.CreatedAt));
    }

    public async Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurriculumId))
        {
            return TypedResults.Problem("Curriculum ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        string actorId = CallerScope.RequiredUserId(httpContext, "An admitted training request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);

        bool namesSites = request.SiteIds is { Count: > 0 };
        bool namesRoles = request.Roles is { Count: > 0 };
        bool namesPeople = request.TargetPersonnelIds is { Count: > 0 };
        if (!namesSites && !namesRoles && !namesPeople)
        {
            return TypedResults.Problem(
                "Name the site_ids, roles or target_personnel_ids to assign; there is no default audience.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The curriculum must exist in the request's study; another study's curriculum gets the same 404 (#5400).
        var curriculum = await dbContext.TrainingCurricula.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CurriculumId, cancellationToken);
        if (curriculum == null || curriculum.StudyId != request.StudyId)
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' not found in study '{request.StudyId}'.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!curriculum.IsActive)
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' is not active.", statusCode: StatusCodes.Status409Conflict);
        }

        var principal = CallerScope.Principal(httpContext);
        if (namesSites && request.SiteIds!.Exists(site => !SiteAccessEvaluator.CanAccessSite(principal, site)))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        // The audience is the study's site staff on the delegation log: the module's record of who works on the study,
        // at which site and in which role. Nobody is synthesized (#5400).
        var staff = await dbContext.DelegationRecords.AsNoTracking()
            .Where(d => d.StudyId == request.StudyId)
            .ToListAsync(cancellationToken);
        var studySites = staff.Select(d => d.SiteId).ToHashSet(StringComparer.Ordinal);

        var unknownSites = namesSites ? request.SiteIds!.Where(site => !studySites.Contains(site)).ToList() : [];
        if (unknownSites.Count > 0)
        {
            return TypedResults.Problem(
                $"Study '{request.StudyId}' has no staff at: {string.Join(", ", unknownSites)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var unknownPeople = namesPeople
            ? request.TargetPersonnelIds!.Where(id => !staff.Exists(d => d.StaffUserId == id)).ToList()
            : [];
        if (unknownPeople.Count > 0)
        {
            return TypedResults.Problem(
                $"No delegation-log entry in study '{request.StudyId}' for: {string.Join(", ", unknownPeople)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // One candidate per person and site, in a site the caller reaches, matching every filter the request names.
        var candidateList = staff
            .Where(d => !namesSites || request.SiteIds!.Contains(d.SiteId))
            .Where(d => !namesRoles || request.Roles!.Contains(d.Role, StringComparer.OrdinalIgnoreCase))
            .Where(d => !namesPeople || request.TargetPersonnelIds!.Contains(d.StaffUserId))
            .Where(d => SiteAccessEvaluator.CanAccessSite(principal, d.SiteId))
            .GroupBy(d => (d.StaffUserId, d.SiteId))
            .Select(g => g.OrderBy(d => d.Id, StringComparer.Ordinal).First())
            .OrderBy(d => d.SiteId, StringComparer.Ordinal).ThenBy(d => d.StaffUserId, StringComparer.Ordinal)
            .Select(d => (UserId: d.StaffUserId, UserName: d.StaffName, SiteId: d.SiteId, Role: d.Role))
            .ToList();

        if (candidateList.Count == 0)
        {
            return TypedResults.Problem(
                "No staff on the study's delegation log match these sites, roles and people. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var now = clock.UtcNow;
        int siteCount = candidateList.Select(c => c.SiteId).Distinct(StringComparer.Ordinal).Count();

        // Request Size Guardrail check (200 limit)
        if (candidateList.Count > MaxBatchLimit)
        {
            return TypedResults.Problem(
                $"Synchronous batch API endpoints enforce a limit of {MaxBatchLimit} target personnel per request to prevent HTTP timeouts. Requested: {candidateList.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string batchTxId = Guid.NewGuid().ToString();
        var createdAssignments = new List<TrainingAssignment>();
        var skippedItems = new List<SkippedAssignmentItemDto>();

        // Load existing assignments for duplicate check
        var existing = await dbContext.TrainingAssignments
            .Where(a => a.CurriculumId == request.CurriculumId && a.Status != "COMPLETED")
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidateList)
        {
            bool hasActive = existing.Any(a =>
                a.UserId == candidate.UserId &&
                a.SiteId == candidate.SiteId);

            if (hasActive)
            {
                skippedItems.Add(new SkippedAssignmentItemDto(
                    candidate.UserId,
                    candidate.SiteId,
                    candidate.Role,
                    "Duplicate active training assignment already exists for this curriculum"));
                continue;
            }

            var asn = new TrainingAssignment
            {
                Id = Guid.NewGuid().ToString(),
                CurriculumId = request.CurriculumId,
                StudyId = request.StudyId,
                SiteId = candidate.SiteId,
                UserId = candidate.UserId,
                UserName = candidate.UserName,
                UserRole = candidate.Role,
                Status = "PENDING",
                BatchTransactionId = batchTxId,
                TimeSpentSeconds = 0,
                CreatedAt = now,
                CreatedBy = actorId,
                ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment"
            };

            dbContext.TrainingAssignments.Add(asn);
            createdAssignments.Add(asn);
        }

        // Log audit
        var audit = new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.curriculum.batch_assign",
            BatchTransactionId = batchTxId,
            Details = $"Batch curriculum assignment for {request.CurriculumId} created {createdAssignments.Count} assignments across {siteCount} sites. {skippedItems.Count} items skipped.",
            ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment"
        };
        dbContext.OrgAuditLogs.Add(audit);

        await unitOfWork.CommitAsync(cancellationToken);

        var assignmentDtos = createdAssignments.Select(a => new TrainingAssignmentDto(
            a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
            a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt)).ToList();

        var response = new BatchAssignmentResponse(
            batchTxId,
            request.CurriculumId,
            request.StudyId,
            candidateList.Count,
            createdAssignments.Count,
            skippedItems.Count,
            assignmentDtos,
            skippedItems,
            now);

        return TypedResults.Created($"/api/v1/training/assignments?batch_transaction_id={batchTxId}", response);
    }

    public async Task<IResult> ListAssignmentsAsync(
        string? studyId,
        string? siteId,
        string? userId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studies, out _, out var denied))
        {
            return denied!;
        }

        var query = dbContext.TrainingAssignments.AsNoTracking();
        if (studies != null) query = query.Where(a => studies.Contains(a.StudyId));
        if (!string.IsNullOrEmpty(studyId)) query = query.Where(a => a.StudyId == studyId);
        if (!string.IsNullOrEmpty(siteId)) query = query.Where(a => a.SiteId == siteId);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(a => a.Status == status);

        var list = (await query.ToListAsync(cancellationToken))
            .Where(a => ClinicalScopeGuard.CanAccessSite(httpContext, a.SiteId))
            .ToList();
        var dtos = list.Select(a => new TrainingAssignmentDto(
            a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
            a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt)).ToList();

        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> GetAssignmentDetailAsync(
        string assignmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var asn = await dbContext.TrainingAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (asn == null || !CallerScope.CanAccessStudy(httpContext, asn.StudyId) || !ClinicalScopeGuard.CanAccessSite(httpContext, asn.SiteId))
        {
            return TypedResults.Problem($"Assignment '{assignmentId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new TrainingAssignmentDto(
            asn.Id, asn.CurriculumId, asn.StudyId, asn.SiteId, asn.UserId, asn.UserName, asn.UserRole,
            asn.Status, asn.BatchTransactionId, asn.QuizScore, asn.TimeSpentSeconds, asn.CompletedAt, asn.CreatedAt));
    }
}

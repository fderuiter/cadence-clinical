using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application.Services;

public interface ISiteMilestoneApplicationService
{
    Task<IResult> GetSiteMilestonesAsync(
        string? studyId,
        string? siteId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> CreateSiteMilestoneAsync(
        SiteMilestoneCreateDto request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> UpdateSiteMilestoneAsync(
        string milestoneId,
        SiteMilestoneUpdateDto request,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class SiteMilestoneApplicationService(
    CtmsDbContext db,
    ICtmsUnitOfWork unitOfWork,
    IClock clock) : ISiteMilestoneApplicationService
{
    public async Task<IResult> GetSiteMilestonesAsync(
        string? studyId,
        string? siteId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studyFilter, out var siteFilter) is { } scopeDenied)
        {
            return scopeDenied;
        }

        IQueryable<CtmsSiteMilestone> query = db.CtmsSiteMilestones.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(m => m.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(m => m.SiteId == siteId);
        }

        var milestones = await query
            .OrderBy(m => m.PlannedDate ?? new DateTimeOffset(m.CreatedAt, TimeSpan.Zero))
            .ToListAsync(ct);

        var scopedMilestones = CtmsScopeGuard.ApplyFilters(milestones, studyFilter, m => m.StudyId, siteFilter, m => m.SiteId);
        var dtos = scopedMilestones.Select(SiteMilestoneEndpoints.ToResponseDto).ToList();
        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> CreateSiteMilestoneAsync(
        SiteMilestoneCreateDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request is null)
        {
            return TypedResults.Problem(
                title: "Invalid request payload",
                detail: "Request body cannot be null.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Site) ||
            string.IsNullOrWhiteSpace(request.Study) ||
            string.IsNullOrWhiteSpace(request.MilestoneType))
        {
            return TypedResults.Problem(
                title: "Validation error",
                detail: "site_id, study_id, and milestone_type are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, request.Study, request.Site) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        var (user, role) = CallerScope.ResolveActor(httpContext);
        DateTimeOffset now = clock.UtcNow;

        CtmsSiteMilestoneStatus initialStatus = CtmsSiteMilestoneStatus.Planned;
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<CtmsSiteMilestoneStatus>(request.Status.Trim(), true, out var parsed))
        {
            initialStatus = parsed;
        }

        var milestone = new CtmsSiteMilestone(Guid.NewGuid().ToString(), initialStatus)
        {
            SiteId = request.Site.Trim(),
            StudyId = request.Study.Trim(),
            MilestoneType = request.MilestoneType.Trim(),
            PlannedDate = request.PlannedDate,
            ActualDate = request.ActualDate,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            ReasonForChange = reason,
            VersionIndex = 1
        };

        db.CtmsSiteMilestones.Add(milestone);

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "CREATE_SITE_MILESTONE",
            Details = $"Created site milestone {milestone.MilestoneType} ({milestone.Id}) for site {milestone.SiteId} in study {milestone.StudyId}.",
            StudyId = milestone.StudyId,
            SiteId = milestone.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/site-milestones/{milestone.Id}", SiteMilestoneEndpoints.ToResponseDto(milestone));
    }

    public async Task<IResult> UpdateSiteMilestoneAsync(
        string milestoneId,
        SiteMilestoneUpdateDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var milestone = await db.CtmsSiteMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct);
        if (milestone is null)
        {
            return TypedResults.Problem(
                title: "Milestone not found",
                detail: $"Site milestone '{milestoneId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, milestone.StudyId, milestone.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, request?.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        var (user, role) = CallerScope.ResolveActor(httpContext);
        DateTimeOffset now = clock.UtcNow;

        if (request is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<CtmsSiteMilestoneStatus>(request.Status.Trim(), true, out var targetStatus))
            {
                var transitionResult = milestone.TransitionTo(targetStatus, user, now, reason, role);
                if (transitionResult.IsFailure)
                {
                    return TypedResults.Problem(
                        title: "Invalid milestone status transition",
                        detail: transitionResult.Error!.Description,
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            if (request.ActualDate.HasValue)
            {
                milestone.ActualDate = request.ActualDate;
            }

            if (request.PlannedDate.HasValue)
            {
                milestone.PlannedDate = request.PlannedDate;
            }
        }

        milestone.ReasonForChange = reason;

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "UPDATE_SITE_MILESTONE",
            Details = $"Updated site milestone {milestone.MilestoneType} ({milestone.Id}) to status {milestone.Status.ToString().ToUpperInvariant()} for site {milestone.SiteId} in study {milestone.StudyId}.",
            StudyId = milestone.StudyId,
            SiteId = milestone.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(SiteMilestoneEndpoints.ToResponseDto(milestone));
    }
}

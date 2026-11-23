using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for CTMS site milestone tracking and updates.
/// </summary>
public static class SiteMilestoneEndpoints
{
    public const string ReadPermission = "ctms_site_milestone:read";
    public const string CreatePermission = "ctms_site_milestone:create";
    public const string UpdatePermission = "ctms_site_milestone:update";

    public static RouteGroupBuilder MapSiteMilestoneEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/site-milestones", GetSiteMilestonesAsync)
            .WithName("ListSiteMilestones")
            .WithSummary("List site milestones filtered by study or site.")
            .Produces<List<SiteMilestoneResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/site-milestones", CreateSiteMilestoneAsync)
            .WithName("CreateSiteMilestone")
            .WithSummary("Create a new site milestone.")
            .Produces<SiteMilestoneResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(CreatePermission).RequireCallerStudyAndSiteScope();

        group.MapPut("/site-milestones/{milestone_id}", UpdateSiteMilestoneAsync)
            .WithName("UpdateSiteMilestone")
            .WithSummary("Update site milestone status, planned date, or actual date.")
            .Produces<SiteMilestoneResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(UpdatePermission).RequireCallerStudyAndSiteScope();

        return group;
    }

    public static async Task<IResult> GetSiteMilestonesAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        CtmsDbContext db,
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
        var dtos = scopedMilestones.Select(ToResponseDto).ToList();
        return TypedResults.Ok(dtos);
    }

    public static async Task<IResult> CreateSiteMilestoneAsync(
        [FromBody] SiteMilestoneCreateDto request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
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

        var (user, role) = ResolveCaller(httpContext);
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

        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/site-milestones/{milestone.Id}", ToResponseDto(milestone));
    }

    public static async Task<IResult> UpdateSiteMilestoneAsync(
        [FromRoute(Name = "milestone_id")] string milestoneId,
        [FromBody] SiteMilestoneUpdateDto request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
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

        var (user, role) = ResolveCaller(httpContext);
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

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(milestone));
    }

    private static (string User, string Role) ResolveCaller(HttpContext httpContext)
    {
        string user = CallerScope.UserId(httpContext) ?? (httpContext.User?.Identity?.Name is { Length: > 0 } n ? n : "system");
        string role = CallerScope.Roles(httpContext);
        if (string.IsNullOrWhiteSpace(role))
        {
            role = "SYSTEM";
        }
        return (user, role);
    }

    public static SiteMilestoneResponseDto ToResponseDto(CtmsSiteMilestone milestone) =>
        new(
            Id: milestone.Id,
            Site: milestone.SiteId,
            Study: milestone.StudyId,
            MilestoneType: milestone.MilestoneType,
            PlannedDate: milestone.PlannedDate?.ToString("o"),
            ActualDate: milestone.ActualDate?.ToString("o"),
            Status: milestone.Status.ToString().ToUpperInvariant(),
            CreatedAt: milestone.CreatedAt.ToString("o"),
            CreatedBy: milestone.CreatedBy,
            ReasonForChange: milestone.ReasonForChange,
            VersionIndex: milestone.VersionIndex
        );
}

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

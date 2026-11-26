using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Domain;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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

    public static Task<IResult> GetSiteMilestonesAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromServices] ISiteMilestoneApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetSiteMilestonesAsync(studyId, siteId, httpContext, ct);

    public static Task<IResult> CreateSiteMilestoneAsync(
        [FromBody] SiteMilestoneCreateDto request,
        [FromServices] ISiteMilestoneApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CreateSiteMilestoneAsync(request, httpContext, ct);

    public static Task<IResult> UpdateSiteMilestoneAsync(
        [FromRoute(Name = "milestone_id")] string milestoneId,
        [FromBody] SiteMilestoneUpdateDto request,
        [FromServices] ISiteMilestoneApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.UpdateSiteMilestoneAsync(milestoneId, request, httpContext, ct);

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

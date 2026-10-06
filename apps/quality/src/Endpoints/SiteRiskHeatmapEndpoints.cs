using System;
using System.Collections.Generic;

using Cadence.Apps.Quality.Application;
using Cadence.Apps.Quality.Domain;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Quality.Endpoints;

public static class SiteRiskHeatmapEndpoints
{
    public static IEndpointRouteBuilder MapSiteRiskHeatmapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/quality/rbqm")
            .WithTags("SiteRiskHeatmap");

        group.MapGet("/site-risk-heatmaps", GetSiteRiskHeatmapAsync)
            .WithName("GetSiteRiskHeatmap")
            .Produces<SiteRiskHeatmapMatrix>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/site-risk-heatmaps/compute", ComputeSiteRiskHeatmapAsync)
            .WithName("ComputeSiteRiskHeatmap")
            .Produces<SiteRiskHeatmapMatrix>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    public static IResult GetSiteRiskHeatmapAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromServices] SiteRiskHeatmapService heatmapService,
        [FromServices] IClock clock)
    {
        string effectiveStudyId = !string.IsNullOrWhiteSpace(studyId) ? studyId : "STUDY-001";
        var matrix = heatmapService.GenerateHeatmap(effectiveStudyId, clock.UtcNow);
        return TypedResults.Ok(matrix);
    }

    public static IResult ComputeSiteRiskHeatmapAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromHeader(Name = "X-Change-Reason")] string? changeReason,
        [FromServices] SiteRiskHeatmapService heatmapService,
        [FromServices] IClock clock)
    {
        string effectiveStudyId = !string.IsNullOrWhiteSpace(studyId) ? studyId : "STUDY-001";
        var matrix = heatmapService.GenerateHeatmap(effectiveStudyId, clock.UtcNow);
        return TypedResults.Ok(matrix);
    }
}

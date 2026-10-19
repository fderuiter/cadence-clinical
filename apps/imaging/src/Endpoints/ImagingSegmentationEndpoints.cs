using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Imaging.Endpoints;

public static class ImagingSegmentationEndpoints
{
    public static IEndpointRouteBuilder MapImagingSegmentationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/imaging/studies")
            .WithTags("Imaging Segmentation");

        group.MapPost("/{studyUid}/series/{seriesUid}/segment", SegmentRoiAsync)
            .WithName("SegmentRoiSeed")
            .RequirePermission("imaging_study:read")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<SegmentationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return routes;
    }

    private static string Caller(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted segmentation request has no signed caller.");

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> SegmentRoiAsync(
        string studyUid,
        string seriesUid,
        SegmentRoiRequest request,
        IImagingSegmentationService segmentationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return TypedResults.BadRequest(new { code = "INVALID_REQUEST", message = "Request body is required." });
        }

        if (!string.IsNullOrWhiteSpace(request.StudyId) && !CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideStudy();
        }

        try
        {
            string callerId = Caller(httpContext);
            var result = await segmentationService.SegmentRoiAsync(studyUid, seriesUid, request, callerId, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { code = "SEGMENTATION_FAILED", message = ex.Message });
        }
    }
}

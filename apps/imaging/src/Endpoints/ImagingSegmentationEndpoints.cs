using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
            // Writing a measurement is a reader's act, not a read: the independent-review readers hold it (ADR-2516).
            .RequirePermission("imaging_adjudication:submit")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return routes;
    }

    private static string Caller(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted segmentation request has no signed caller.");

    private static ProblemHttpResult OutsideStudy() =>
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

        var outcome = await segmentationService.SegmentRoiAsync(
            studyUid,
            seriesUid,
            request,
            Caller(httpContext),
            studyId => CallerScope.CanAccessStudy(httpContext, studyId),
            cancellationToken).ConfigureAwait(false);

        return outcome.Status switch
        {
            SegmentationStatus.OutsideCallerStudies => OutsideStudy(),
            SegmentationStatus.ModelUnavailable => TypedResults.Problem(
                title: "Automatic segmentation is not available",
                detail: "No segmentation model reads the image, so nothing was measured or stored. Measure the lesion with the calipers.",
                statusCode: StatusCodes.Status501NotImplemented),
            _ => TypedResults.Problem(
                title: "Series not found",
                detail: "No stored scan has this study and series.",
                statusCode: StatusCodes.Status404NotFound),
        };
    }
}

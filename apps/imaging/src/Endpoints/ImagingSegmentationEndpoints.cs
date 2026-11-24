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
            .WithSummary("Requests automatic ROI lesion segmentation for a DICOM series (returns 501 Not Implemented until a segmentation model is wired).")
            .WithDescription("HTTP 501 Not Implemented stub: no automatic segmentation model is wired, so this route validates study/site scope and returns RFC 7807 ProblemDetails (501) without storing synthetic measurements.")
            // Writing a measurement is a reader's act, not a read: the independent-review readers hold it (ADR-2516).
            .RequirePermission("imaging_adjudication:submit").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return routes;
    }

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
            CallerScope.RequiredUserId(httpContext, "An admitted segmentation request has no signed caller."),
            studyId => ClinicalScopeGuard.CanAccessStudy(httpContext, studyId),
            cancellationToken,
            siteId => string.IsNullOrWhiteSpace(siteId)
                ? ClinicalScopeGuard.HasValidSiteScopeIfRequired(ClinicalScopeGuard.ResolvePrincipal(httpContext))
                : ClinicalScopeGuard.CanAccessSite(httpContext, siteId)).ConfigureAwait(false);

        return outcome.Status switch
        {
            SegmentationStatus.OutsideCallerStudies => ClinicalScopeGuard.OutsideStudy(),
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

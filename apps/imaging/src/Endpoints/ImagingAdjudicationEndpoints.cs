using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Services;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Imaging.Endpoints;

public static class ImagingAdjudicationEndpoints
{
    public static IEndpointRouteBuilder MapImagingAdjudicationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/imaging/adjudications")
            .WithTags("Imaging Adjudication");

        group.MapPost("/", SubmitOrEvaluateSessionAsync)
            .WithName("SubmitOrEvaluateAdjudicationSession")
            .RequirePermission("imaging_adjudication:submit").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<AdjudicationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{sessionId}", GetSessionByIdAsync)
            .WithName("GetImagingAdjudicationSession")
            .RequirePermission("imaging_adjudication:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<ImagingAdjudicationSession>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{sessionId}/chair-adjudicate", ChairAdjudicateSessionAsync)
            .WithName("ChairAdjudicateImagingSession")
            .RequirePermission("imaging_adjudication:adjudicate").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<AdjudicationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireElectronicSignature("imaging.adjudication.chair_adjudicate", http => http.Request.Path.Value ?? string.Empty);

        return routes;
    }

    private static Task<IResult> SubmitOrEvaluateSessionAsync(
        SubmitAdjudicationSessionRequest request,
        ImagingAdjudicationService adjudicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        adjudicationService.SubmitOrEvaluateSessionAsync(request, httpContext, cancellationToken);

    private static Task<IResult> GetSessionByIdAsync(
        string sessionId,
        ImagingAdjudicationService adjudicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        adjudicationService.GetSessionByIdAsync(sessionId, httpContext, cancellationToken);

    private static Task<IResult> ChairAdjudicateSessionAsync(
        string sessionId,
        ChairAdjudicateImagingRequest request,
        ImagingAdjudicationService adjudicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        adjudicationService.ChairAdjudicateSessionAsync(sessionId, request, httpContext, cancellationToken);
}

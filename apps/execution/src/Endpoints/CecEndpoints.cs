using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Cec;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API endpoints for Clinical Endpoint Adjudication Committee (CEC) operations.
/// Gated by 21 CFR Part 11 step-up electronic signature verification.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// @req:PRD-SYS-124
/// </summary>
public static class CecEndpoints
{
    public const string ChairAdjudicateSemanticAction = "execution.cec.chair_adjudicate";

    public static string ChairAdjudicateAction(string dossierId) => $"/api/v1/execution/cec/dossiers/{dossierId}/chair-adjudicate";

    public static IEndpointRouteBuilder MapCecEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/execution/cec/dossiers");

        group.MapPost("/{id}/chair-adjudicate", ChairAdjudicateAsync)
            .WithName("ChairAdjudicateDossier")
            .Produces<ChairAdjudicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(ChairAdjudicateSemanticAction, ChairAdjudicateActionFor);

        group.MapGet("/{id}", GetDossierByIdAsync)
            .WithName("GetCecDossierById")
            .Produces<CecDossier>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static string ChairAdjudicateActionFor(HttpContext httpContext) =>
        ChairAdjudicateAction(httpContext.Request.RouteValues["id"]?.ToString() ?? string.Empty);

    private static async Task<IResult> ChairAdjudicateAsync(
        string id,
        ChairAdjudicateRequest request,
        ICecApplicationService cecService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        var result = await cecService.ChairAdjudicateAsync(id, request, caller, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.ToProblemResult();
    }

    private static async Task<IResult> GetDossierByIdAsync(
        string id,
        ICecRepository repository,
        CancellationToken cancellationToken)
    {
        var dossier = await repository.GetDossierByIdAsync(id, cancellationToken);
        return dossier is not null
            ? TypedResults.Ok(dossier)
            : TypedResults.NotFound(new { code = "NOT_FOUND", message = $"CEC Dossier '{id}' not found." });
    }
}

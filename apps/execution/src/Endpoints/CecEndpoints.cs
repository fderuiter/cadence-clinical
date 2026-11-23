using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cec;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API endpoints for Clinical Endpoint Adjudication Committee (CEC) operations.
/// Gated by 21 CFR Part 11 step-up electronic signature verification.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("cec_adjudication:adjudicate").RequireCallerStudyScope()
            .RequireElectronicSignature(ChairAdjudicateSemanticAction, ChairAdjudicateActionFor);

        group.MapGet("/{id}", GetDossierByIdAsync)
            .WithName("GetCecDossierById")
            .Produces<CecDossier>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("cec_adjudication:read").RequireCallerStudyScope();

        return routes;
    }

    private static string ChairAdjudicateActionFor(HttpContext httpContext) =>
        ChairAdjudicateAction(httpContext.Request.RouteValues["id"]?.ToString() ?? string.Empty);

    private static async Task<IResult> ChairAdjudicateAsync(
        string id,
        ChairAdjudicateRequest request,
        ICecApplicationService cecService,
        ICecRepository repository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireVerifiedTenant(httpContext, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var existing = await repository.GetDossierByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(
                httpContext,
                existing?.StudyId ?? "STUDY-001",
                existing?.SiteId,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var caller = ClinicalScopeGuard.ResolvePrincipal(httpContext) is { UserId.Length: > 0 } p
            ? new AuthenticatedCaller(p.UserId, p.Roles)
            : null;
        var result = await cecService.ChairAdjudicateAsync(id, request, caller, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.ToProblemResult();
    }

    private static async Task<IResult> GetDossierByIdAsync(
        string id,
        HttpContext httpContext,
        ICecRepository repository,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } callerDenied)
        {
            return callerDenied;
        }

        var dossier = await repository.GetDossierByIdAsync(id, cancellationToken);
        if (dossier is null)
        {
            return TypedResults.NotFound(new { code = "NOT_FOUND", message = $"CEC Dossier '{id}' not found." });
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(
                httpContext,
                dossier.StudyId,
                dossier.SiteId,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true) is { } recordDenied)
        {
            return recordDenied;
        }

        return TypedResults.Ok(dossier);
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Cec;
using Cadence.Apps.Execution.Application.Cec.Sdtm;
using Cadence.Apps.Execution.Domain.Cec;
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
/// Governs blinded dual-reader assessments, concordance/discordance checking, Chair tie-breaker adjudications,
/// and CDISC SDTM CE dataset compilation. Gated by 21 CFR Part 11 step-up electronic signature verification.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-CEC-003
/// @req:PRD-SYS-053
/// @req:PRD-SYS-124
/// </summary>
public static class CecEndpoints
{
    public const string ChairAdjudicateSemanticAction = "execution.cec.chair_adjudicate";

    public static string ChairAdjudicateAction(string dossierId) => $"/api/v1/execution/cec/dossiers/{dossierId}/chair-adjudicate";

    public static IEndpointRouteBuilder MapCecEndpoints(this IEndpointRouteBuilder routes)
    {
        var dossiersGroup = routes.MapGroup("/api/v1/execution/cec/dossiers");

        dossiersGroup.MapGet("", GetDossiersListAsync)
            .WithName("GetCecDossiersList")
            .Produces<List<CecDossierBlindedView>>(StatusCodes.Status200OK);

        dossiersGroup.MapGet("/{id}", GetDossierByIdAsync)
            .WithName("GetCecDossierById")
            .Produces<CecDossierBlindedView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        dossiersGroup.MapPost("/compile", CompileDossierAsync)
            .WithName("CompileCecDossier")
            .Produces<CecDossierBlindedView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        dossiersGroup.MapPost("/{id}/assessments", SubmitReaderAssessmentAsync)
            .WithName("SubmitCecReaderAssessment")
            .Produces<ReaderAssessmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        dossiersGroup.MapPost("/{id}/chair-adjudicate", ChairAdjudicateAsync)
            .WithName("ChairAdjudicateDossier")
            .Produces<ChairAdjudicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(ChairAdjudicateSemanticAction, ChairAdjudicateActionFor);

        var sdtmGroup = routes.MapGroup("/api/v1/execution/cec/sdtm-ce");

        sdtmGroup.MapGet("", ExportSdtmCeAsync)
            .WithName("ExportCecSdtmCe")
            .Produces<SdtmCeDataset>(StatusCodes.Status200OK);

        return routes;
    }

    private static string ChairAdjudicateActionFor(HttpContext httpContext) =>
        ChairAdjudicateAction(httpContext.Request.RouteValues["id"]?.ToString() ?? string.Empty);

    private static async Task<IResult> GetDossiersListAsync(
        string? studyId,
        string? status,
        ICecApplicationService cecService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        var result = await cecService.GetDossiersListAsync(studyId, status, caller, cancellationToken);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> GetDossierByIdAsync(
        string id,
        ICecApplicationService cecService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        var result = await cecService.GetBlindedDossierAsync(id, caller, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.ToProblemResult();
    }

    private static async Task<IResult> CompileDossierAsync(
        CompileDossierRequest request,
        ICecApplicationService cecService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        var result = await cecService.CompileDossierAsync(request, caller, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.ToProblemResult();
    }

    private static async Task<IResult> SubmitReaderAssessmentAsync(
        string id,
        SubmitReaderAssessmentRequest request,
        ICecApplicationService cecService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        var result = await cecService.SubmitReaderAssessmentAsync(id, request, caller, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.ToProblemResult();
    }

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

    private static async Task<IResult> ExportSdtmCeAsync(
        string? studyId,
        ICecApplicationService cecService,
        CancellationToken cancellationToken)
    {
        var result = await cecService.ExportSdtmCeAsync(studyId, cancellationToken);
        return TypedResults.Ok(result.Value);
    }
}

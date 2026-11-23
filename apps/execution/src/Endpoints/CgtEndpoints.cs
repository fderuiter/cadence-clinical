using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class CgtEndpoints
{
    public static IEndpointRouteBuilder MapCgtEndpoints(this IEndpointRouteBuilder routes)
    {
        // Route permissions (ADR-2516): site and pharmacy staff record the chain of custody; sponsor, monitoring and
        // audit roles read it.
        var group = routes.MapGroup("/api/v1/execution/rtsm/cgt")
            .WithTags("CGT");

        group.MapPost("/apheresis", async (
            CgtApheresisCreateRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.CollectionSiteId) is { } denied)
            {
                return denied;
            }

            try
            {
                var result = await service.InitializeApheresisAsync(request, ct);
                return Results.Ok(result);
            }
            catch (InvalidDinException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .WithName("InitializeCgtApheresis")
        .WithSummary("Initializes a cell and gene therapy chain of custody at apheresis collection with ISBT-128 DIN validation.")
        .Produces<CgtChainOfCustodyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/milestone", async (
            CgtMilestoneCreateRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (await GuardStudyAndStoredCocAsync(httpContext, service, request.StudyId, request.ChainOfCustodyId, ct) is { } denied)
            {
                return denied;
            }

            try
            {
                var result = await service.RecordMilestoneAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ChainOfCustodyNotFoundException ex)
            {
                return Results.NotFound(new { detail = ex.Message });
            }
        })
        .WithName("RecordCgtMilestone")
        .WithSummary("Records a tamper-evident manufacturing or cold-chain logistics milestone on a CGT chain of custody.")
        .Produces<CgtChainOfCustodyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyScope();

        group.MapPost("/manufacturing-bind", async (
            CgtManufacturingBindRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (await GuardStudyAndStoredCocAsync(httpContext, service, request.StudyId, request.ChainOfCustodyId, ct) is { } denied)
            {
                return denied;
            }

            try
            {
                var result = await service.BindManufacturingBatchAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ChainOfCustodyNotFoundException ex)
            {
                return Results.NotFound(new { detail = ex.Message });
            }
        })
        .WithName("BindCgtManufacturingBatch")
        .WithSummary("Binds a finished autologous manufacturing lot and SEC/expiry metadata to a chain of custody.")
        .Produces<CgtChainOfCustodyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyScope();

        group.MapPost("/bedside-verify", async (
            CgtBedsideVerificationRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            if (GuardCocScope(httpContext, await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct)) is { } cocDenied)
            {
                return cocDenied;
            }

            try
            {
                var result = await service.VerifyBedsideAsync(request, ct);
                return Results.Ok(result);
            }
            catch (DuplicateVerifierException ex)
            {
                return Results.UnprocessableEntity(new { detail = ex.Message });
            }
            catch (BarcodeVerificationException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .WithName("VerifyCgtBedside")
        .WithSummary("Performs dual-operator bedside barcode verification prior to cell therapy infusion.")
        .Produces<CgtBedsideVerificationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/infuse", async (
            CgtInfusionExecuteRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            if (GuardCocScope(httpContext, await service.GetChainOfCustodyScopeAsync(request.ChainOfCustodyId, ct)) is { } cocDenied)
            {
                return cocDenied;
            }

            try
            {
                var result = await service.ExecuteInfusionAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ChainOfCustodyNotFoundException ex)
            {
                return Results.NotFound(new { detail = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .WithName("ExecuteCgtInfusion")
        .WithSummary("Records autologous cell therapy administration following verified bedside identity match.")
        .Produces<CgtChainOfCustodyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/returns", async (
            CgtVialReturnReconciliationRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            if (GuardCocScope(httpContext, await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct)) is { } cocDenied)
            {
                return cocDenied;
            }

            var result = await service.LogVialReturnAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("LogCgtVialReturn")
        .WithSummary("Logs post-infusion CGT bag or vial return accountability reconciliation.")
        .Produces<CgtVialReturnReconciliationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/destruction-certificate", async (
            CgtDestructionCertificateCreateRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            if (request.CocId is { } cocId
                && GuardCocScope(httpContext, await service.GetChainOfCustodyScopeAsync(cocId, ct)) is { } cocDenied)
            {
                return cocDenied;
            }

            try
            {
                var result = await service.GenerateDestructionCertificateAsync(request, ct);
                return Results.Ok(result);
            }
            catch (DuplicateWitnessException ex)
            {
                return Results.UnprocessableEntity(new { detail = ex.Message });
            }
        })
        .WithName("CreateCgtDestructionCertificate")
        .WithSummary("Generates a dual-witness CGT biohazard destruction certificate.")
        .Produces<CgtDestructionCertificateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/verify-barcode", (
            CgtBarcodeVerifyRequest request,
            HttpContext httpContext,
            ICgtApplicationService service) =>
        {
            if ((ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
                 ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = service.VerifyBarcode(request);
            if (!string.IsNullOrWhiteSpace(result.StudyId)
                && ExecutionScopeGuard.RequireStudy(httpContext, result.StudyId) is { } studyDenied)
            {
                return studyDenied;
            }

            if (!string.IsNullOrWhiteSpace(result.SiteId)
                && ExecutionScopeGuard.RequireSite(httpContext, result.SiteId) is { } siteDenied)
            {
                return siteDenied;
            }

            return Results.Ok(result);
        })
        .WithName("VerifyCgtBarcode")
        .WithSummary("Parses and validates an ISBT-128 or GS1 CGT label barcode within the caller's clinical scope.")
        .Produces<CgtBarcodeVerifyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:read").RequireCallerStudyAndSiteScope();

        group.MapGet("/chain-of-custody/{studyId}/{subjectId}", async (
            string studyId,
            string subjectId,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if ((ExecutionScopeGuard.RequireStudy(httpContext, studyId)
                 ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = await service.GetFullTraceabilityAsync(studyId, subjectId, ct);
            foreach (string? siteId in new[] { result.ChainOfCustody?.SiteId, result.BedsideVerification?.SiteId, result.DestructionCertificate?.SiteId })
            {
                if (!string.IsNullOrWhiteSpace(siteId)
                    && ExecutionScopeGuard.RequireSite(httpContext, siteId) is { } siteDenied)
                {
                    return siteDenied;
                }
            }

            return Results.Ok(result);
        })
        .WithName("GetCgtFullTraceability")
        .WithSummary("Retrieves end-to-end vein-to-vein CGT chain-of-custody traceability for a study subject.")
        .Produces<CgtFullTraceabilityResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        group.MapGet("/certificates/{studyId}", async (
            string studyId,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, studyId, null, out var siteFilter, out _) is { } denied)
            {
                return denied;
            }

            var result = await service.ListDestructionCertificatesAsync(studyId, ct);
            return Results.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
        })
        .WithName("ListCgtDestructionCertificates")
        .WithSummary("Lists CGT biohazard destruction certificates for a study within the caller's site scope.")
        .Produces<System.Collections.Generic.List<CgtDestructionCertificateResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        group.MapGet("/reconciliations/{studyId}", async (
            string studyId,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, studyId, null, out var siteFilter, out _) is { } denied)
            {
                return denied;
            }

            var result = await service.ListVialReconciliationsAsync(studyId, ct);
            return Results.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
        })
        .WithName("ListCgtVialReconciliations")
        .WithSummary("Lists CGT vial and bag return accountability reconciliations for a study.")
        .Produces<System.Collections.Generic.List<CgtVialReturnReconciliationResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        return routes;
    }

    private static async Task<IResult?> GuardStudyAndStoredCocAsync(
        HttpContext httpContext,
        ICgtApplicationService service,
        string studyId,
        Guid chainOfCustodyId,
        CancellationToken ct)
    {
        if ((ExecutionScopeGuard.RequireStudy(httpContext, studyId)
             ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
        {
            return callerDenied;
        }

        return GuardCocScope(httpContext, await service.GetChainOfCustodyScopeAsync(chainOfCustodyId, ct).ConfigureAwait(false));
    }

    private static IResult? GuardCocScope(HttpContext httpContext, (string StudyId, string SiteId)? scope) =>
        scope is { } s ? ExecutionScopeGuard.RequireStudyAndSite(httpContext, s.StudyId, s.SiteId) : null;
}

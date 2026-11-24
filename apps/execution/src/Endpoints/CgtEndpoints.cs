using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class CgtEndpoints
{
    public const string BedsideVerifyAction = "/api/v1/execution/rtsm/cgt/bedside-verify";
    public const string BedsideVerifySemanticAction = "execution.cgt.bedside_verify";
    public const string InfuseAction = "/api/v1/execution/rtsm/cgt/infuse";
    public const string InfuseSemanticAction = "execution.cgt.infuse";
    public const string ReconcileReturnAction = "/api/v1/execution/rtsm/cgt/returns";
    public const string ReconcileReturnSemanticAction = "execution.cgt.reconcile_return";
    public const string CertifyDestructionAction = "/api/v1/execution/rtsm/cgt/destruction-certificate";
    public const string CertifyDestructionSemanticAction = "execution.cgt.certify_destruction";

    public static IEndpointRouteBuilder MapCgtEndpoints(this IEndpointRouteBuilder routes)
    {
        // Route permissions (ADR-2516): site and pharmacy staff record the chain of custody; sponsor, monitoring and
        // audit roles read it.
        var group = routes.MapGroup("/api/v1/execution/rtsm/cgt")
            .WithTags("CGT")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/apheresis", InitializeApheresisAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/milestone", RecordMilestoneAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyScope();

        group.MapPost("/manufacturing-bind", BindManufacturingBatchAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyScope();

        group.MapPost("/bedside-verify", VerifyBedsideAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(BedsideVerifySemanticAction, _ => BedsideVerifyAction);

        group.MapPost("/infuse", ExecuteInfusionAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(InfuseSemanticAction, _ => InfuseAction);

        group.MapPost("/returns", LogVialReturnAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(ReconcileReturnSemanticAction, _ => ReconcileReturnAction);

        group.MapPost("/destruction-certificate", GenerateDestructionCertificateAsync)
            .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(CertifyDestructionSemanticAction, _ => CertifyDestructionAction);

        group.MapPost("/verify-barcode", VerifyBarcode)
            .AllowWithoutChangeReason()
            .RequirePermission("cgt_chain_of_custody:read").RequireCallerStudyAndSiteScope();

        group.MapGet("/chain-of-custody/{studyId}/{subjectId}", GetFullTraceabilityAsync)
            .RequirePermission("cgt_chain_of_custody:read")
            .RequireStudyScope("studyId");

        group.MapGet("/certificates/{studyId}", ListDestructionCertificatesAsync)
            .RequirePermission("cgt_chain_of_custody:read")
            .RequireStudyScope("studyId");

        group.MapGet("/reconciliations/{studyId}", ListVialReconciliationsAsync)
            .RequirePermission("cgt_chain_of_custody:read")
            .RequireStudyScope("studyId");

        return routes;
    }

    public static async Task<IResult> InitializeApheresisAsync(
        CgtApheresisCreateRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> RecordMilestoneAsync(
        CgtMilestoneCreateRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> BindManufacturingBatchAsync(
        CgtManufacturingBindRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> VerifyBedsideAsync(
        CgtBedsideVerificationRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> ExecuteInfusionAsync(
        CgtInfusionExecuteRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> LogVialReturnAsync(
        CgtVialReturnReconciliationRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> GenerateDestructionCertificateAsync(
        CgtDestructionCertificateCreateRequest request,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static IResult VerifyBarcode(
        CgtBarcodeVerifyRequest request,
        HttpContext httpContext,
        ICgtApplicationService service)
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
    }

    public static async Task<IResult> GetFullTraceabilityAsync(
        string studyId,
        string subjectId,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
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
    }

    public static async Task<IResult> ListDestructionCertificatesAsync(
        string studyId,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, studyId, null, out var siteFilter, out _) is { } denied)
        {
            return denied;
        }

        var result = await service.ListDestructionCertificatesAsync(studyId, ct);
        return Results.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
    }

    public static async Task<IResult> ListVialReconciliationsAsync(
        string studyId,
        HttpContext httpContext,
        ICgtApplicationService service,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, studyId, null, out var siteFilter, out _) is { } denied)
        {
            return denied;
        }

        var result = await service.ListVialReconciliationsAsync(studyId, ct);
        return Results.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
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

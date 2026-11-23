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

            if (await ExecutionScopeGuard.RequireUnlockedAsync(
                    httpContext,
                    request.StudyId,
                    request.CollectionSiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } lockDenied)
            {
                return lockDenied;
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

            var cocScope = await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct);
            if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
            {
                return cocDenied;
            }

            if (await GuardCocMutationLocksAsync(httpContext, request.StudyId, request.SiteId, request.SubjectId, cocScope, ct).ConfigureAwait(false) is { } lockDenied)
            {
                return lockDenied;
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

            var cocScope = await service.GetChainOfCustodyScopeAsync(request.ChainOfCustodyId, ct);
            if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
            {
                return cocDenied;
            }

            string? cocSubjectId = await service.GetChainOfCustodySubjectAsync(request.ChainOfCustodyId, ct);
            if (await GuardCocMutationLocksAsync(
                    httpContext,
                    request.StudyId,
                    request.SiteId,
                    cocSubjectId ?? request.SubjectId,
                    cocScope,
                    ct).ConfigureAwait(false) is { } lockDenied)
            {
                return lockDenied;
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

            var cocScope = await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct);
            if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
            {
                return cocDenied;
            }

            if (await GuardCocMutationLocksAsync(httpContext, request.StudyId, request.SiteId, request.SubjectId, cocScope, ct).ConfigureAwait(false) is { } lockDenied)
            {
                return lockDenied;
            }

            var result = await service.LogVialReturnAsync(request, ct);
            return Results.Ok(result);
        })
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

            (string StudyId, string SiteId)? cocScope = null;
            string? cocSubjectId = null;
            if (request.CocId is { } cocId)
            {
                cocScope = await service.GetChainOfCustodyScopeAsync(cocId, ct);
                if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
                {
                    return cocDenied;
                }

                cocSubjectId = await service.GetChainOfCustodySubjectAsync(cocId, ct);
            }

            if (await GuardCocMutationLocksAsync(
                    httpContext,
                    request.StudyId,
                    request.SiteId,
                    cocSubjectId ?? request.SubjectId,
                    cocScope,
                    ct).ConfigureAwait(false) is { } lockDenied)
            {
                return lockDenied;
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

        var cocScope = await service.GetChainOfCustodyScopeAsync(chainOfCustodyId, ct).ConfigureAwait(false);
        if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
        {
            return cocDenied;
        }

        string? cocSubjectId = await service.GetChainOfCustodySubjectAsync(chainOfCustodyId, ct).ConfigureAwait(false);
        return await GuardCocMutationLocksAsync(httpContext, studyId, cocScope?.SiteId, cocSubjectId, cocScope, ct).ConfigureAwait(false);
    }

    private static async Task<IResult?> GuardCocMutationLocksAsync(
        HttpContext httpContext,
        string studyId,
        string? siteId,
        string? subjectId,
        (string StudyId, string SiteId)? cocScope,
        CancellationToken ct)
    {
        if (await ExecutionScopeGuard.RequireUnlockedAsync(httpContext, studyId, siteId, subjectId, ct).ConfigureAwait(false) is { } requestLockDenied)
        {
            return requestLockDenied;
        }

        if (cocScope is { } s
            && (!string.Equals(s.StudyId, studyId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(s.SiteId, siteId, StringComparison.OrdinalIgnoreCase)))
        {
            return await ExecutionScopeGuard.RequireUnlockedAsync(httpContext, s.StudyId, s.SiteId, subjectId, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static IResult? GuardCocScope(HttpContext httpContext, (string StudyId, string SiteId)? scope) =>
        scope is { } s ? ExecutionScopeGuard.RequireStudyAndSite(httpContext, s.StudyId, s.SiteId) : null;
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.CollectionSiteId) is { } denied)
            {
                return denied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    request.StudyId,
                    request.CollectionSiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;

            try
            {
                var result = await service.InitializeApheresisAsync(request, callerUserId, ct);
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            var (denied, cocLockScope) = await GuardStudyAndLoadCocLockScopeAsync(httpContext, service, request.StudyId, request.ChainOfCustodyId, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    cocLockScope?.StudyCode ?? request.StudyId,
                    cocLockScope?.SiteCode,
                    cocLockScope?.SubjectCode,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;

            try
            {
                var result = await service.RecordMilestoneAsync(request, callerUserId, ct);
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            var (denied, cocLockScope) = await GuardStudyAndLoadCocLockScopeAsync(httpContext, service, request.StudyId, request.ChainOfCustodyId, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    cocLockScope?.StudyCode ?? request.StudyId,
                    cocLockScope?.SiteCode,
                    cocLockScope?.SubjectCode,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;

            try
            {
                var result = await service.BindManufacturingBatchAsync(request, callerUserId, ct);
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            var storedScope = await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct).ConfigureAwait(false);
            if (GuardCocScope(httpContext, storedScope) is { } cocDenied)
            {
                return cocDenied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    request.StudyId,
                    request.SiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }

            try
            {
                var result = await service.VerifyBedsideAsync(request, callerUserId, ct);
                return Results.Ok(result);
            }
            catch (DuplicateVerifierException ex)
            {
                return Results.UnprocessableEntity(new { detail = ex.Message });
            }
            catch (CallerIdentityMismatchException ex)
            {
                return SignerMismatch(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }
            catch (BarcodeVerificationException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/infuse", async (
            CgtInfusionExecuteRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            var cocScope = await service.GetChainOfCustodyScopeAsync(request.ChainOfCustodyId, ct).ConfigureAwait(false);
            if (GuardCocScope(httpContext, cocScope) is { } cocDenied)
            {
                return cocDenied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    request.StudyId,
                    request.SiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                return SignerMismatch("Authenticated caller identity is required.");
            }

            try
            {
                var result = await service.ExecuteInfusionAsync(request, callerUserId, ct);
                return Results.Ok(result);
            }
            catch (CallerIdentityMismatchException ex)
            {
                return SignerMismatch(ex.Message);
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
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/returns", async (
            CgtVialReturnReconciliationRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } requestDenied)
            {
                return requestDenied;
            }

            if (GuardCocScope(httpContext, await service.GetSubjectChainOfCustodyScopeAsync(request.StudyId, request.SubjectId, ct)) is { } cocDenied)
            {
                return cocDenied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    request.StudyId,
                    request.SiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;

            var result = await service.LogVialReturnAsync(request, callerUserId, ct);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope();

        group.MapPost("/destruction-certificate", async (
            CgtDestructionCertificateCreateRequest request,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
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

            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    request.StudyId,
                    request.SiteId,
                    request.SubjectId,
                    ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string? callerUserId = httpContext.User.ToAuthenticatedCaller()?.UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }

            try
            {
                var result = await service.GenerateDestructionCertificateAsync(request, callerUserId, ct);
                return Results.Ok(result);
            }
            catch (DuplicateWitnessException ex)
            {
                return Results.UnprocessableEntity(new { detail = ex.Message });
            }
            catch (CallerIdentityMismatchException ex)
            {
                return SignerMismatch(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }
        })
        .RequirePermission("cgt_chain_of_custody:write").RequireCallerStudyAndSiteScope()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

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

    private static async Task<(IResult? Denied, (string StudyCode, string? SiteCode, string? SubjectCode)? Scope)> GuardStudyAndLoadCocLockScopeAsync(
        HttpContext httpContext,
        ICgtApplicationService service,
        string studyId,
        Guid chainOfCustodyId,
        CancellationToken ct)
    {
        if ((ExecutionScopeGuard.RequireStudy(httpContext, studyId)
             ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
        {
            return (callerDenied, null);
        }

        var scope = await service.GetChainOfCustodyScopeAsync(chainOfCustodyId, ct).ConfigureAwait(false);
        var lockScope = await service.GetChainOfCustodyLockScopeAsync(chainOfCustodyId, ct).ConfigureAwait(false);
        if (GuardCocScope(httpContext, scope) is { } cocDenied)
        {
            return (cocDenied, null);
        }

        if (lockScope is { } ls)
        {
            return (null, (ls.StudyCode, ls.SiteCode, ls.SubjectCode));
        }

        return scope is { } s ? (null, (s.StudyId, s.SiteId, null)) : (null, null);
    }

    private static IResult? GuardCocScope(HttpContext httpContext, (string StudyId, string SiteId)? scope) =>
        scope is { } s ? ExecutionScopeGuard.RequireStudyAndSite(httpContext, s.StudyId, s.SiteId) : null;

    private static IResult SignerMismatch(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["error"] = "SIGNER_MISMATCH" });
}

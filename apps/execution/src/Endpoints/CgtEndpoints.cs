using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

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
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.CollectionSiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestDenied)
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestDenied)
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
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestDenied)
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

            var result = await service.LogVialReturnAsync(request, ct);
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
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestDenied)
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
            if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = service.VerifyBarcode(request);
            if (!string.IsNullOrWhiteSpace(result.StudyId)
                && ClinicalScopeGuard.RequireStudy(httpContext, result.StudyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } studyDenied)
            {
                return studyDenied;
            }

            if (!string.IsNullOrWhiteSpace(result.SiteId)
                && ClinicalScopeGuard.RequireSite(httpContext, result.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } siteDenied)
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
            if ((ClinicalScopeGuard.RequireStudy(httpContext, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
                 ?? ClinicalScopeGuard.ResolveSingleSiteFilter(httpContext, null, out _, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = await service.GetFullTraceabilityAsync(studyId, subjectId, ct);
            foreach (string? siteId in new[] { result.ChainOfCustody?.SiteId, result.BedsideVerification?.SiteId, result.DestructionCertificate?.SiteId })
            {
                if (!string.IsNullOrWhiteSpace(siteId)
                    && ClinicalScopeGuard.RequireSite(httpContext, siteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } siteDenied)
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
            if (ClinicalScopeGuard.RequireStudyAndResolveSingleSiteFilter(httpContext, studyId, null, out var siteFilter, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
            {
                return denied;
            }

            var result = await service.ListDestructionCertificatesAsync(studyId, ct);
            return Results.Ok(ClinicalScopeGuard.ApplyFilters(result, null, siteFilter, x => x.StudyId, x => x.SiteId).ToList());
        })
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        group.MapGet("/reconciliations/{studyId}", async (
            string studyId,
            HttpContext httpContext,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            if (ClinicalScopeGuard.RequireStudyAndResolveSingleSiteFilter(httpContext, studyId, null, out var siteFilter, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
            {
                return denied;
            }

            var result = await service.ListVialReconciliationsAsync(studyId, ct);
            return Results.Ok(ClinicalScopeGuard.ApplyFilters(result, null, siteFilter, x => x.StudyId, x => x.SiteId).ToList());
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
        if ((ClinicalScopeGuard.RequireStudy(httpContext, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
             ?? ClinicalScopeGuard.ResolveSingleSiteFilter(httpContext, null, out _, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)) is { } callerDenied)
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
        scope is { } s ? ClinicalScopeGuard.RequireStudyAndSite(httpContext, s.StudyId, s.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) : null;
}

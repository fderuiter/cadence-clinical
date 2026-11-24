using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Supply;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class IpKitEndpoints
{
    public const string ReconcileBatchSemanticAction = "execution.ip_kit.reconcile_batch";
    public const string ReconcileBatchAction = "/api/v1/execution/rtsm/kits/reconcile-batch";

    public static IEndpointRouteBuilder MapIpKitEndpoints(this IEndpointRouteBuilder routes)
    {
        // Route permissions (ADR-2516): kit scans, reconciliation and inventory are blinded supply data, so only the
        // pharmacist (rtsm_supply) reaches them; seeding a site's kit inventory is a sponsor operation.
        var group = routes.MapGroup("/api/v1/execution/rtsm/kits")
            .WithTags("IP Kit Reconciliation");

        group.MapPost("/validate-scan", async (
            IpKitScanValidateRequest request,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            if (await GuardKitSitesAsync(httpContext, service, request.StudyId, [request.KitNumber], request.SiteId, ct) is { } denied)
            {
                return denied;
            }

            var result = await service.ValidateScannedBarcodeAsync(request, ct);
            return Results.Ok(result);
        })
        .RequirePermission("rtsm_supply:read").RequireCallerStudyAndSiteScope();

        group.MapPost("/reconcile-batch", async (
            IpKitBatchReconciliationRequest request,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            var kitNumbers = (request.Items ?? []).Select(i => i.KitNumber).ToList();
            var (denied, existingSites) = await GuardAndLoadKitSitesAsync(httpContext, service, request.StudyId, kitNumbers, request.SiteId, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            if (await EnsureKitSitesUnlockedAsync(lockGuard, request.StudyId, request.SiteId, existingSites, ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            string verifiedSigner = CallerScope.UserId(httpContext)
                ?? throw new InvalidOperationException("Signed IP kit reconciliation requires a verified caller.");
            var verifiedRequest = request with { ProcessedBy = verifiedSigner };

            try
            {
                var result = await service.ProcessBatchReconciliationAsync(verifiedRequest, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .RequirePermission("rtsm_supply:write")
        .RequireCallerStudyAndSiteScope()
        .AddEndpointFilter(PreCheckReconcileBatchScopeFilter)
        .RequireElectronicSignature(ReconcileBatchSemanticAction, _ => ReconcileBatchAction);

        group.MapGet("/certificates/{certificateId}", async (
            string certificateId,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = await service.GetCertificateAsync(certificateId, ct);
            if (result == null)
            {
                return Results.NotFound(new { detail = $"Certificate '{certificateId}' not found." });
            }

            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, result.StudyId, result.SiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } recordDenied)
            {
                return recordDenied;
            }

            return Results.Ok(result);
        })
        .RequirePermission("rtsm_supply:read").RequireCallerStudyAndSiteScope();

        group.MapGet("/inventory/{studyId}/{siteId}", async (
            string studyId,
            string siteId,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
            {
                return denied;
            }

            var result = await service.GetSiteInventoryAsync(studyId, siteId, ct);
            return Results.Ok(result);
        })
        .RequirePermission("rtsm_supply:read")
        .RequireStudyScope("studyId")
        .RequireSiteScope("siteId");

        group.MapPost("/seed-inventory", async (
            SeedInventoryRequest request,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct,
            [FromServices] IKernelLockGuard? lockGuard = null) =>
        {
            var kitNumbers = (request.Kits ?? []).Select(k => k.KitNumber).ToList();
            var (denied, existingSites) = await GuardAndLoadKitSitesAsync(httpContext, service, request.StudyId, kitNumbers, request.SiteId, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            if (await EnsureKitSitesUnlockedAsync(lockGuard, request.StudyId, request.SiteId, existingSites, ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }

            var result = await service.SeedSiteInventoryAsync(request.StudyId, request.SiteId, request.Kits ?? [], ct);
            return Results.Ok(result);
        })
        .RequirePermission("ip_kit_inventory:seed").RequireCallerStudyAndSiteScope();

        return routes;
    }

    private static async ValueTask<object?> PreCheckReconcileBatchScopeFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<IpKitBatchReconciliationRequest>().FirstOrDefault();
        if (request is not null)
        {
            var service = context.HttpContext.RequestServices.GetRequiredService<IIpKitApplicationService>();
            var kitNumbers = (request.Items ?? []).Select(i => i.KitNumber);
            var (denied, existingSites) = await GuardAndLoadKitSitesAsync(
                context.HttpContext,
                service,
                request.StudyId,
                kitNumbers,
                request.SiteId,
                context.HttpContext.RequestAborted).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            var lockGuard = context.HttpContext.RequestServices.GetService<IKernelLockGuard>();
            if (await EnsureKitSitesUnlockedAsync(
                lockGuard,
                request.StudyId,
                request.SiteId,
                existingSites,
                context.HttpContext.RequestAborted).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async Task<IResult?> GuardKitSitesAsync(
        HttpContext httpContext,
        IIpKitApplicationService service,
        string studyId,
        IEnumerable<string> kitNumbers,
        string siteId,
        CancellationToken ct)
    {
        var (denied, _) = await GuardAndLoadKitSitesAsync(httpContext, service, studyId, kitNumbers, siteId, ct).ConfigureAwait(false);
        return denied;
    }

    private static async Task<(IResult? Denied, IReadOnlyCollection<string> ExistingSites)> GuardAndLoadKitSitesAsync(
        HttpContext httpContext,
        IIpKitApplicationService service,
        string studyId,
        IEnumerable<string> kitNumbers,
        string siteId,
        CancellationToken ct)
    {
        if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestDenied)
        {
            return (requestDenied, []);
        }

        var existingSites = await service.GetExistingKitSitesAsync(studyId, kitNumbers, ct).ConfigureAwait(false);
        foreach (string existingSite in existingSites)
        {
            if (ClinicalScopeGuard.RequireSite(httpContext, existingSite, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } existingDenied)
            {
                return (existingDenied, []);
            }
        }

        return (null, existingSites);
    }

    private static async Task<IResult?> EnsureKitSitesUnlockedAsync(
        IKernelLockGuard? lockGuard,
        string studyId,
        string siteId,
        IEnumerable<string> existingSites,
        CancellationToken ct)
    {
        if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, studyId, siteId, cancellationToken: ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        foreach (string existingSite in existingSites)
        {
            if (!string.Equals(existingSite, siteId, StringComparison.Ordinal)
                && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, studyId, existingSite, cancellationToken: ct).ConfigureAwait(false) is { } existingLocked)
            {
                return existingLocked;
            }
        }

        return null;
    }
}

public record SeedInventoryRequest(
    string StudyId,
    string SiteId,
    List<IpKitSeedItem> Kits
);

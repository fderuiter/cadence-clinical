using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Supply;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class IpKitEndpoints
{
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
        .RequirePermission("rtsm_supply:read");

        group.MapPost("/reconcile-batch", async (
            IpKitBatchReconciliationRequest request,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            var kitNumbers = (request.Items ?? []).Select(i => i.KitNumber);
            if (await GuardKitSitesAsync(httpContext, service, request.StudyId, kitNumbers, request.SiteId, ct) is { } denied)
            {
                return denied;
            }

            try
            {
                var result = await service.ProcessBatchReconciliationAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        })
        .RequirePermission("rtsm_supply:write");

        group.MapGet("/certificates/{certificateId}", async (
            string certificateId,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            if ((ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
                 ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
            {
                return callerDenied;
            }

            var result = await service.GetCertificateAsync(certificateId, ct);
            if (result == null)
            {
                return Results.NotFound(new { detail = $"Certificate '{certificateId}' not found." });
            }

            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, result.StudyId, result.SiteId) is { } recordDenied)
            {
                return recordDenied;
            }

            return Results.Ok(result);
        })
        .RequirePermission("rtsm_supply:read");

        group.MapGet("/inventory/{studyId}/{siteId}", async (
            string studyId,
            string siteId,
            HttpContext httpContext,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } denied)
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
            CancellationToken ct) =>
        {
            var kitNumbers = (request.Kits ?? []).Select(k => k.KitNumber);
            if (await GuardKitSitesAsync(httpContext, service, request.StudyId, kitNumbers, request.SiteId, ct) is { } denied)
            {
                return denied;
            }

            var result = await service.SeedSiteInventoryAsync(request.StudyId, request.SiteId, request.Kits ?? [], ct);
            return Results.Ok(result);
        })
        .RequirePermission("ip_kit_inventory:seed");

        return routes;
    }

    private static async Task<IResult?> GuardKitSitesAsync(
        HttpContext httpContext,
        IIpKitApplicationService service,
        string studyId,
        IEnumerable<string> kitNumbers,
        string siteId,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } requestDenied)
        {
            return requestDenied;
        }

        foreach (string existingSite in await service.GetExistingKitSitesAsync(studyId, kitNumbers, ct).ConfigureAwait(false))
        {
            if (ExecutionScopeGuard.RequireSite(httpContext, existingSite) is { } existingDenied)
            {
                return existingDenied;
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Supply;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class IpKitEndpoints
{
    public static IEndpointRouteBuilder MapIpKitEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/execution/rtsm/kits")
            .WithTags("IP Kit Reconciliation");

        group.MapPost("/validate-scan", async (
            IpKitScanValidateRequest request,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.ValidateScannedBarcodeAsync(request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/reconcile-batch", async (
            IpKitBatchReconciliationRequest request,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.ProcessBatchReconciliationAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { detail = ex.Message });
            }
        });

        group.MapGet("/certificates/{certificateId}", async (
            string certificateId,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetCertificateAsync(certificateId, ct);
            if (result == null)
            {
                return Results.NotFound(new { detail = $"Certificate '{certificateId}' not found." });
            }
            return Results.Ok(result);
        });

        group.MapGet("/inventory/{studyId}/{siteId}", async (
            string studyId,
            string siteId,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetSiteInventoryAsync(studyId, siteId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/seed-inventory", async (
            SeedInventoryRequest request,
            IIpKitApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.SeedSiteInventoryAsync(request.StudyId, request.SiteId, request.Kits, ct);
            return Results.Ok(result);
        });

        return routes;
    }
}

public record SeedInventoryRequest(
    string StudyId,
    string SiteId,
    List<IpKitInventoryResponse> Kits
);

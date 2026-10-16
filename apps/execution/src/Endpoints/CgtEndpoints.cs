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
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/milestone", async (
            CgtMilestoneCreateRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/manufacturing-bind", async (
            CgtManufacturingBindRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/bedside-verify", async (
            CgtBedsideVerificationRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/infuse", async (
            CgtInfusionExecuteRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/returns", async (
            CgtVialReturnReconciliationRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.LogVialReturnAsync(request, ct);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/destruction-certificate", async (
            CgtDestructionCertificateCreateRequest request,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
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
        .RequirePermission("cgt_chain_of_custody:write");

        group.MapPost("/verify-barcode", (
            CgtBarcodeVerifyRequest request,
            ICgtApplicationService service) =>
        {
            var result = service.VerifyBarcode(request);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:read");

        group.MapGet("/chain-of-custody/{studyId}/{subjectId}", async (
            string studyId,
            string subjectId,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetFullTraceabilityAsync(studyId, subjectId, ct);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        group.MapGet("/certificates/{studyId}", async (
            string studyId,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.ListDestructionCertificatesAsync(studyId, ct);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        group.MapGet("/reconciliations/{studyId}", async (
            string studyId,
            ICgtApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.ListVialReconciliationsAsync(studyId, ct);
            return Results.Ok(result);
        })
        .RequirePermission("cgt_chain_of_custody:read")
        .RequireStudyScope("studyId");

        return routes;
    }
}

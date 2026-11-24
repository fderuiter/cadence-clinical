using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API endpoints for investigational medical device UDI barcode parsing, RTSM inventory management,
/// deficiency reporting, dispensation gating, and safety quarantine controls.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public static class DeviceEndpoints
{
    public const string ReleaseQuarantineAction = "/api/v1/execution/devices/quarantine/release";
    public const string ReleaseQuarantineSemanticAction = "execution.device.release_quarantine";

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/execution/devices")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/parse-udi", ParseUdi)
            .AllowWithoutChangeReason()
            .WithName("ParseUdi")
            .WithSummary("Parse UDI Barcode")
            .WithDescription("Parses and validates GS1-128, GS1-DataMatrix, and HIBCC barcode strings into structured UDI attributes.")
            .Produces<ParsedUdiResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        group.MapPost("/inventory", RegisterInventoryAsync)
            .WithName("RegisterDeviceInventory")
            .WithSummary("Register Device Inventory")
            .WithDescription("Registers serialized investigational medical device items in RTSM.")
            .Produces<IReadOnlyList<DeviceInventoryResponse>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:register").RequireCallerStudyAndSiteScope();

        group.MapGet("/inventory", ListInventoryAsync)
            .WithName("ListDeviceInventory")
            .WithSummary("List Device Inventory")
            .WithDescription("Retrieves investigational medical device inventory matching query filters.")
            .Produces<IReadOnlyList<DeviceInventoryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        group.MapPost("/dispense", DispenseDeviceAsync)
            .WithName("DispenseDevice")
            .WithSummary("Dispense Device")
            .WithDescription("Dispenses an investigational device to a study subject while enforcing quarantine and expiration gating rules.")
            .Produces<DeviceDispensationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:dispense").RequireCallerStudyAndSiteScope();

        group.MapPost("/deficiencies", LogDeficiencyAsync)
            .WithName("LogDeviceDeficiency")
            .WithSummary("Log Device Deficiency")
            .WithDescription("Logs a device deficiency/malfunction and triggers automated quarantine + safety cases.")
            .Produces<DeviceDeficiencyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:report_deficiency").RequireCallerStudyAndSiteScope();

        group.MapGet("/deficiencies", ListDeficienciesAsync)
            .WithName("ListDeviceDeficiencies")
            .WithSummary("List Device Deficiencies")
            .Produces<IReadOnlyList<DeviceDeficiencyResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        group.MapGet("/deficiencies/{deficiency_id}", GetDeficiencyByIdAsync)
            .WithName("GetDeviceDeficiencyById")
            .WithSummary("Get Device Deficiency")
            .Produces<DeviceDeficiencyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        group.MapPost("/quarantine", InitiateQuarantineAsync)
            .WithName("InitiateDeviceQuarantine")
            .WithSummary("Initiate Quarantine")
            .Produces<DeviceQuarantineResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:quarantine").RequireCallerStudyAndSiteScope();

        group.MapGet("/quarantine", ListQuarantinesAsync)
            .WithName("ListDeviceQuarantines")
            .WithSummary("List Device Quarantines")
            .Produces<IReadOnlyList<DeviceQuarantineResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        group.MapPost("/quarantine/release", ReleaseQuarantineAsync)
            .WithName("ReleaseDeviceQuarantine")
            .WithSummary("Release Quarantine")
            .Produces<DeviceQuarantineResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:release").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(ReleaseQuarantineSemanticAction, _ => ReleaseQuarantineAction);

        group.MapGet("/investigation-summary", GetInvestigationSummaryAsync)
            .WithName("GetDeviceInvestigationSummary")
            .WithSummary("Get Investigation Summary")
            .Produces<DeviceInvestigationSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("device_accountability:read").RequireCallerStudyAndSiteScope();

        return routes;
    }

    private static IResult ParseUdi(
        UdiParseRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService)
    {
        if ((ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
             ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } denied)
        {
            return denied;
        }

        var result = deviceService.ParseUdi(request);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> RegisterInventoryAsync(
        DeviceInventoryCreateRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } denied)
        {
            return denied;
        }

        if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, request.StudyId, request.SiteId, cancellationToken: ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await deviceService.RegisterInventoryAsync(request, ct).ConfigureAwait(false);
        return TypedResults.Created("/api/v1/execution/devices/inventory", result);
    }

    private static async Task<IResult> ListInventoryAsync(
        string study_id,
        string? site_id,
        string? status,
        string? udi_di,
        string? lot_number,
        string? serial_number,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, study_id, site_id, out var siteFilter, out var effectiveSiteId) is { } denied)
        {
            return denied;
        }

        var result = await deviceService.ListInventoryAsync(study_id, effectiveSiteId, status, udi_di, lot_number, serial_number, ct).ConfigureAwait(false);
        return TypedResults.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
    }

    private static async Task<IResult> DispenseDeviceAsync(
        DeviceDispensationRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        var (denied, storedScope) = await GuardAndResolveDeviceMutationAsync(
            httpContext,
            deviceService,
            request.StudyId,
            request.SiteId,
            request.DeviceInventoryId,
            request.ScannedBarcode,
            ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        if (await EnsureDeviceUnlockedAsync(lockGuard, request.StudyId, request.SiteId, request.SubjectId, storedScope, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        try
        {
            var result = await deviceService.DispenseDeviceAsync(request, ct).ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.NotFound(new { code = "DEVICE_NOT_FOUND", detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.UnprocessableEntity(new { code = "DISPENSATION_BLOCKED", detail = ex.Message });
        }
    }

    private static async Task<IResult> LogDeficiencyAsync(
        DeviceDeficiencyCreateRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        var (denied, storedScope) = await GuardAndResolveDeviceMutationAsync(
            httpContext,
            deviceService,
            request.StudyId,
            request.SiteId,
            request.DeviceInventoryId,
            null,
            ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        if (await EnsureDeviceUnlockedAsync(lockGuard, request.StudyId, request.SiteId, request.SubjectId, storedScope, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await deviceService.LogDeficiencyAsync(request, ct).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/execution/devices/deficiencies/{result.Id}", result);
    }

    private static async Task<IResult> ListDeficienciesAsync(
        string study_id,
        string? site_id,
        string? status,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, study_id, site_id, out var siteFilter, out var effectiveSiteId) is { } denied)
        {
            return denied;
        }

        var result = await deviceService.ListDeficienciesAsync(study_id, effectiveSiteId, status, ct).ConfigureAwait(false);
        return TypedResults.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
    }

    private static async Task<IResult> GetDeficiencyByIdAsync(
        string deficiency_id,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct)
    {
        if ((ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
             ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
        {
            return callerDenied;
        }

        if (!Guid.TryParse(deficiency_id, out var devGuid))
        {
            return TypedResults.NotFound(new { code = "DEFICIENCY_NOT_FOUND", detail = $"Deficiency '{deficiency_id}' not found." });
        }

        var result = await deviceService.GetDeficiencyByIdAsync(devGuid, ct).ConfigureAwait(false);
        if (result is null)
        {
            return TypedResults.NotFound(new { code = "DEFICIENCY_NOT_FOUND", detail = $"Deficiency '{deficiency_id}' not found." });
        }

        if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, result.StudyId, result.SiteId) is { } recordDenied)
        {
            return recordDenied;
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> InitiateQuarantineAsync(
        DeviceQuarantineCreateRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        var (denied, storedScope) = await GuardAndResolveDeviceMutationAsync(
            httpContext,
            deviceService,
            request.StudyId,
            request.SiteId,
            request.DeviceInventoryId,
            null,
            ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        if (await EnsureDeviceUnlockedAsync(lockGuard, request.StudyId, request.SiteId, null, storedScope, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await deviceService.InitiateQuarantineAsync(request, ct).ConfigureAwait(false);
        return TypedResults.Created("/api/v1/execution/devices/quarantine", result);
    }

    private static async Task<IResult> ListQuarantinesAsync(
        string study_id,
        string? site_id,
        string? status,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, study_id, site_id, out var siteFilter, out var effectiveSiteId) is { } denied)
        {
            return denied;
        }

        var result = await deviceService.ListQuarantinesAsync(study_id, effectiveSiteId, status, ct).ConfigureAwait(false);
        return TypedResults.Ok(ExecutionScopeGuard.ApplyFilters(result, null, x => x.StudyId, siteFilter, x => x.SiteId));
    }

    private static async Task<IResult> ReleaseQuarantineAsync(
        DeviceQuarantineReleaseRequest request,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        if ((ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
             ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _)) is { } callerDenied)
        {
            return callerDenied;
        }

        if (Guid.TryParse(request.QuarantineId, out var qGuid)
            && await deviceService.GetQuarantineByIdAsync(qGuid, ct).ConfigureAwait(false) is { } quarantine)
        {
            if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, quarantine.StudyId, quarantine.SiteId) is { } recordDenied)
            {
                return recordDenied;
            }

            if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, quarantine.StudyId, quarantine.SiteId, cancellationToken: ct).ConfigureAwait(false) is { } locked)
            {
                return locked;
            }
        }

        try
        {
            var result = await deviceService.ReleaseQuarantineAsync(request, ct).ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.NotFound(new { code = "QUARANTINE_NOT_FOUND", detail = ex.Message });
        }
    }

    private static async Task<IResult> GetInvestigationSummaryAsync(
        string study_id,
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndResolveSiteFilter(httpContext, study_id, null, out var siteFilter, out _) is { } denied)
        {
            return denied;
        }

        var result = siteFilter is null
            ? await deviceService.GetInvestigationSummaryAsync(study_id, ct).ConfigureAwait(false)
            : await deviceService.GetInvestigationSummaryAsync(study_id, siteFilter, ct).ConfigureAwait(false)
              ?? await deviceService.GetInvestigationSummaryAsync(study_id, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<(IResult? Denied, (string StudyId, string SiteId)? StoredScope)> GuardAndResolveDeviceMutationAsync(
        HttpContext httpContext,
        IDeviceApplicationService deviceService,
        string studyId,
        string siteId,
        string? deviceInventoryId,
        string? scannedBarcode,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } requestDenied)
        {
            return (requestDenied, null);
        }

        var storedScope = await deviceService.GetInventoryScopeAsync(studyId, deviceInventoryId, scannedBarcode, ct).ConfigureAwait(false);
        var storedDenied = storedScope is { } s
            ? ExecutionScopeGuard.RequireStudyAndSite(httpContext, s.StudyId, s.SiteId)
            : null;
        return (storedDenied, storedScope);
    }

    private static async Task<IResult?> EnsureDeviceUnlockedAsync(
        IKernelLockGuard? lockGuard,
        string studyId,
        string siteId,
        string? subjectId,
        (string StudyId, string SiteId)? storedScope,
        CancellationToken ct)
    {
        if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, studyId, siteId, subjectId, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (storedScope is { } s
            && (!string.Equals(s.StudyId, studyId, StringComparison.Ordinal)
                || !string.Equals(s.SiteId, siteId, StringComparison.Ordinal))
            && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, s.StudyId, s.SiteId, subjectId, ct).ConfigureAwait(false) is { } storedLocked)
        {
            return storedLocked;
        }

        return null;
    }
}

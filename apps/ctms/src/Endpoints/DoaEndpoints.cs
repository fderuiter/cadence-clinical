using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for Delegation of Authority (DoA) site task assignment,
/// revocation, and Principal Investigator sign-off.
/// </summary>
public static class DoaEndpoints
{
    public const string ReadPermission = "ctms_doa:read";
    public const string AssignPermission = "ctms_doa:assign";
    public const string RevokePermission = "ctms_doa:revoke";
    public const string SignOffPermission = "ctms_doa:sign_off";

    public static RouteGroupBuilder MapDoaEndpoints(this RouteGroupBuilder group)
    {
        var doa = group.MapGroup("/doa");

        doa.MapPost("/delegate", DelegateTasksAsync)
            .WithName("DelegateSiteTasks")
            .WithSummary("Assign site trial task delegation requiring Principal Investigator sign-off.")
            .Produces<DelegationResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(AssignPermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/revoke", RevokeDelegationAsync)
            .WithName("RevokeSiteTasks")
            .WithSummary("Revoke or end a delegated trial duty with reason for change.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(RevokePermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/sign-off", SignOffDelegationAsync)
            .WithName("SignOffDelegation")
            .WithSummary("Endorse Delegation of Authority task assignment with Principal Investigator eSignature.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature("ctms.doa.sign_off", http => http.Request.Path.Value ?? string.Empty);

        group.MapDoaQueryEndpoints();

        return group;
    }

    public static Task<IResult> DelegateTasksAsync(
        [FromBody] DelegationTaskRequest request,
        [FromServices] IDoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.DelegateTasksAsync(request, httpContext, ct, studyId);

    public static Task<IResult> RevokeDelegationAsync(
        [FromBody] RevokeDelegationRequest request,
        [FromServices] IDoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.RevokeDelegationAsync(request, httpContext, ct, studyId);

    public static Task<IResult> SignOffDelegationAsync(
        [FromBody] DoaSignOffRequest request,
        [FromServices] IDoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.SignOffDelegationAsync(request, httpContext, ct, studyId);

    public static Task<IResult> GetSiteDoaLogAsync(
        string siteId,
        IDoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null) =>
        DoaQueryEndpoints.GetSiteDoaLogAsync(siteId, service, httpContext, ct, studyId, validAt, knownAt);

    public static Task<IResult> ExportSiteDoaPdfAsync(
        string siteId,
        IDoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null) =>
        DoaQueryEndpoints.ExportSiteDoaPdfAsync(siteId, service, httpContext, ct, studyId);

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem) =>
        MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, bodyReason, out reason, out problem);

    public static DelegationResponseDto ToResponseDto(CtmsDoaTaskEndorsement d) =>
        DoaMutationService.ToResponseDto(d);

    public static DoaAuditHistoryItemDto ToAuditDto(CtmsAuditLog a) =>
        DoaQueryAndExportService.ToAuditDto(a);
}

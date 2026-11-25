using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for Delegation of Authority (DoA) site task assignment,
/// revocation, Principal Investigator sign-off, log retrieval, and GxP PDF export.
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

        doa.MapGet("/sites/{site_id}/log", GetSiteDoaLogAsync)
            .WithName("GetSiteDoaLog")
            .WithSummary("Fetch active and historical DOA log matrix for a site.")
            .Produces<DoaLogResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id");

        doa.MapGet("/sites/{site_id}/export-pdf", ExportSiteDoaPdfAsync)
            .WithName("ExportSiteDoaPdf")
            .WithSummary("Export 21 CFR Part 11 signed DOA PDF log.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id")
            .RequireElectronicSignature("ctms.doa.export_pdf", http => http.Request.Path.Value ?? string.Empty);

        return group;
    }

    public static Task<IResult> DelegateTasksAsync(
        [FromBody] DelegationTaskRequest request,
        DoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.DelegateTasksAsync(request, httpContext, ct, studyId);

    public static Task<IResult> RevokeDelegationAsync(
        [FromBody] RevokeDelegationRequest request,
        DoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.RevokeDelegationAsync(request, httpContext, ct, studyId);

    public static Task<IResult> SignOffDelegationAsync(
        [FromBody] DoaSignOffRequest request,
        DoaMutationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.SignOffDelegationAsync(request, httpContext, ct, studyId);

    public static Task<IResult> GetSiteDoaLogAsync(
        [FromRoute(Name = "site_id")] string siteId,
        DoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt = null,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt = null) =>
        service.GetSiteDoaLogAsync(siteId, httpContext, ct, studyId, validAt, knownAt);

    public static Task<IResult> ExportSiteDoaPdfAsync(
        [FromRoute(Name = "site_id")] string siteId,
        DoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.ExportSiteDoaPdfAsync(siteId, httpContext, ct, studyId);

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem) =>
        CtmsScopeGuard.TryExtractReasonForChange(httpContext, bodyReason, out reason, out problem);
}

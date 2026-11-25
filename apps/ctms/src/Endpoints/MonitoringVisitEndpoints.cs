using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for CTMS site monitoring visits and Part 11 sign-offs.
/// </summary>
public static class MonitoringVisitEndpoints
{
    public const string ReadPermission = "ctms_monitoring_visit:read";
    public const string CreatePermission = "ctms_monitoring_visit:create";
    public const string UpdatePermission = "ctms_monitoring_visit:update";
    public const string SignOffPermission = "ctms_monitoring_visit:sign_off";
    public const string ReadLetterPermission = "ctms_monitoring_letter:read";
    public const string ReadLetterTypePermission = "ctms_monitoring_letter:read_type";

    public static RouteGroupBuilder MapMonitoringVisitEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/monitoring-visits", GetMonitoringVisitsAsync)
            .WithName("ListMonitoringVisits")
            .WithSummary("List monitoring visits filtered by study, site, CRA, or status.")
            .Produces<List<MonitoringVisitResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits", ScheduleMonitoringVisitAsync)
            .WithName("ScheduleMonitoringVisit")
            .WithSummary("Schedule a new monitoring visit.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(CreatePermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits/{visit_id}/complete", CompleteMonitoringVisitAsync)
            .WithName("CompleteMonitoringVisit")
            .WithSummary("Complete a scheduled monitoring visit with actual date and clinical findings.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(UpdatePermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits/{visit_id}/sign-off", SignOffMonitoringVisitAsync)
            .WithName("SignOffMonitoringVisit")
            .WithSummary("Perform supervisory Part 11 sign-off on a completed monitoring visit.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature("ctms.monitoring_visit.sign_off", http => http.Request.Path.Value ?? string.Empty);

        group.MapGet("/monitoring-visits/{visit_id}/letters", GetMonitoringVisitLettersAsync)
            .WithName("GetMonitoringVisitLetters")
            .WithSummary("Get generated letters for a monitoring visit.")
            .Produces<List<GeneratedLetterResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadLetterPermission).RequireCallerStudyAndSiteScope();

        group.MapGet("/monitoring-visits/{visit_id}/letters/{letter_type}", GetMonitoringVisitLetterByTypeAsync)
            .WithName("GetMonitoringVisitLetterByType")
            .WithSummary("Get a generated visit letter by type.")
            .Produces<GeneratedLetterResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadLetterTypePermission).RequireCallerStudyAndSiteScope();

        return group;
    }

    public static Task<IResult> GetMonitoringVisitsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "cra_id")] string? craId,
        [FromQuery(Name = "status")] string? status,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitsAsync(studyId, siteId, craId, status, httpContext, ct);

    public static Task<IResult> ScheduleMonitoringVisitAsync(
        [FromBody] MonitoringVisitCreateDto request,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ScheduleMonitoringVisitAsync(request, httpContext, ct);

    public static Task<IResult> CompleteMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromBody] MonitoringVisitCompleteDto request,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CompleteMonitoringVisitAsync(visitId, request, httpContext, ct);

    public static Task<IResult> SignOffMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromServices] IPublishEndpoint? publishEndpoint = null) =>
        service.SignOffMonitoringVisitAsync(visitId, httpContext, ct, publishEndpoint);

    public static Task<IResult> GetMonitoringVisitLettersAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitLettersAsync(visitId, httpContext, ct);

    public static Task<IResult> GetMonitoringVisitLetterByTypeAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromRoute(Name = "letter_type")] string letterType,
        MonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitLetterByTypeAsync(visitId, letterType, httpContext, ct);

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem) =>
        CtmsScopeGuard.TryExtractReasonForChange(httpContext, bodyReason, out reason, out problem);
}

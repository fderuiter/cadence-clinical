using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Domain;
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

        group.MapMonitoringVisitLetterEndpoints();

        return group;
    }

    public static Task<IResult> GetMonitoringVisitsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "cra_id")] string? craId,
        [FromQuery(Name = "status")] string? status,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitsAsync(studyId, siteId, craId, status, httpContext, ct);

    public static Task<IResult> ScheduleMonitoringVisitAsync(
        [FromBody] MonitoringVisitCreateDto request,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ScheduleMonitoringVisitAsync(request, httpContext, ct);

    public static Task<IResult> CompleteMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromBody] MonitoringVisitCompleteDto request,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CompleteMonitoringVisitAsync(visitId, request, httpContext, ct);

    public static Task<IResult> SignOffMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromServices] IPublishEndpoint? publishEndpoint = null) =>
        service.SignOffMonitoringVisitAsync(visitId, httpContext, ct, publishEndpoint);

    public static Task<IResult> GetMonitoringVisitLettersAsync(
        string visitId,
        IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        MonitoringVisitLetterEndpoints.GetMonitoringVisitLettersAsync(visitId, service, httpContext, ct);

    public static Task<IResult> GetMonitoringVisitLetterByTypeAsync(
        string visitId,
        string letterType,
        IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        MonitoringVisitLetterEndpoints.GetMonitoringVisitLetterByTypeAsync(visitId, letterType, service, httpContext, ct);

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem)
    {
        reason = string.Empty;
        problem = null;

        if (httpContext.Request.Headers.TryGetValue("X-Change-Reason", out var headerVal))
        {
            string headerStr = headerVal.ToString();
            if (!ChangeReasonHeader.TryDecode(headerStr, out string? decoded))
            {
                problem = TypedResults.Problem(
                    title: "Invalid reason for change header",
                    detail: "X-Change-Reason must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8.",
                    statusCode: StatusCodes.Status400BadRequest);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(decoded))
            {
                reason = decoded.Trim();
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(bodyReason))
        {
            reason = bodyReason.Trim();
            return true;
        }

        problem = TypedResults.Problem(
            title: "Reason for change required",
            detail: "Reason for change is required.",
            statusCode: StatusCodes.Status400BadRequest);
        return false;
    }

    public static MonitoringVisitResponseDto ToResponseDto(CtmsMonitoringVisit visit) =>
        new(
            Id: visit.Id,
            Study: visit.StudyId,
            Site: visit.SiteId,
            CraId: visit.CraId,
            VisitType: visit.VisitType,
            ScheduledDate: visit.ScheduledDate.ToString("o"),
            ActualDate: visit.ActualDate?.ToString("o"),
            Status: visit.Status.ToString().ToUpperInvariant(),
            CreatedAt: visit.CreatedAt.ToString("o"),
            CreatedBy: visit.CreatedBy,
            ReasonForChange: visit.ReasonForChange,
            VersionIndex: visit.VersionIndex,
            SignatureManifestation: visit.SignatureManifestation,
            Signer: visit.Signer,
            SigningTimestamp: visit.SigningTimestamp?.ToString("o")
        );

    public static GeneratedLetterResponseDto ToLetterResponseDto(CtmsGeneratedLetter letter) =>
        new(
            Id: letter.Id,
            VisitId: letter.VisitId,
            LetterType: letter.LetterType,
            RenderedContent: letter.RenderedContent,
            CreatedAt: letter.CreatedAt.ToString("o"),
            CreatedBy: letter.CreatedBy,
            ReasonForChange: letter.ReasonForChange,
            VersionIndex: letter.VersionIndex
        );
}

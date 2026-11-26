using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

public static class MonitoringVisitLetterEndpoints
{
    public static RouteGroupBuilder MapMonitoringVisitLetterEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/monitoring-visits/{visit_id}/letters", GetMonitoringVisitLettersAsync)
            .WithTags("MonitoringVisitEndpoints")
            .WithName("GetMonitoringVisitLetters")
            .WithSummary("Get generated letters for a monitoring visit.")
            .Produces<List<GeneratedLetterResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(MonitoringVisitEndpoints.ReadLetterPermission).RequireCallerStudyAndSiteScope();

        group.MapGet("/monitoring-visits/{visit_id}/letters/{letter_type}", GetMonitoringVisitLetterByTypeAsync)
            .WithTags("MonitoringVisitEndpoints")
            .WithName("GetMonitoringVisitLetterByType")
            .WithSummary("Get a generated visit letter by type.")
            .Produces<GeneratedLetterResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(MonitoringVisitEndpoints.ReadLetterTypePermission).RequireCallerStudyAndSiteScope();

        return group;
    }

    public static Task<IResult> GetMonitoringVisitLettersAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitLettersAsync(visitId, httpContext, ct);

    public static Task<IResult> GetMonitoringVisitLetterByTypeAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromRoute(Name = "letter_type")] string letterType,
        [FromServices] IMonitoringVisitApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetMonitoringVisitLetterByTypeAsync(visitId, letterType, httpContext, ct);
}

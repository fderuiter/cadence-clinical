using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Safety.Endpoints;

public static class DsurEndpoints
{
    /// <summary>Compile and update a draft periodic safety report.</summary>
    public const string SafetyPermission = "safety_report:create";

    /// <summary>Read periodic safety reports.</summary>
    public const string ReadPermission = "safety_report:read";

    /// <summary>Approve (sign and lock) a periodic safety report: a qualified safety physician or medical monitor (#5462).</summary>
    public const string ApprovePermission = "safety_report:approve";

    /// <summary>The meaning a DSUR approval's step-up signature token must carry.</summary>
    public const string ApproveSemanticAction = "safety.periodic_report.approve";

    /// <summary>
    /// The concrete action a DSUR approval's step-up token must carry, on either route, so a token for one report cannot
    /// sign another.
    /// </summary>
    public static string ApproveAction(string reportId) => $"/api/v1/safety/dsur/{reportId}/approve";

    public static RouteGroupBuilder MapDsurEndpoints(this RouteGroupBuilder group)
    {
        // Primary requirement endpoints
        group.MapPost("/dsur/compile", CompileDsurAsync)
            .WithName("CompileDsurReport")
            .WithSummary("Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequirePermission(SafetyPermission).RequireCallerStudyScope();

        MapApprove(group.MapPost("/dsur/{id}/approve", ApproveDsurAsync)
            .WithName("ApproveDsurReport")
            .WithSummary("Signs and locks a DSUR as the verified caller: needs safety_report:approve, a step-up signature token bound to the report and a reason for change; the compiler cannot approve."));

        group.MapPut("/dsur/{id}", UpdateDsurAsync)
            .WithName("UpdateDsurReport")
            .WithSummary("Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SafetyPermission).RequireCallerStudyScope();

        group.MapGet("/dsur/{id}", GetDsurByIdAsync)
            .WithName("GetDsurReportById")
            .WithSummary("Retrieves a DSUR periodic safety report record by identifier.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireCallerStudyScope();

        group.MapGet("/dsur", ListDsurReportsAsync)
            .WithName("ListDsurReports")
            .WithSummary("Lists all DSUR periodic safety report records.")
            .Produces<PeriodicSafetyReportRecord[]>(StatusCodes.Status200OK)
            .RequirePermission(ReadPermission).RequireCallerStudyScope();

        // Alias routes for periodic-reports endpoint path conventions
        group.MapPost("/periodic-reports/compile", CompileDsurAsync)
            .WithName("CompilePeriodicReport")
            .RequirePermission(SafetyPermission).RequireCallerStudyScope();

        MapApprove(group.MapPost("/periodic-reports/{id}/approve", ApproveDsurAsync)
            .WithName("ApprovePeriodicReport"));

        group.MapPut("/periodic-reports/{id}", UpdateDsurAsync)
            .WithName("UpdatePeriodicReport")
            .RequirePermission(SafetyPermission).RequireCallerStudyScope();

        group.MapGet("/periodic-reports/{id}", GetDsurByIdAsync)
            .WithName("GetPeriodicReportById")
            .RequirePermission(ReadPermission).RequireCallerStudyScope();

        group.MapGet("/periodic-reports", ListDsurReportsAsync)
            .WithName("ListPeriodicReports")
            .RequirePermission(ReadPermission).RequireCallerStudyScope();

        return group;
    }

    private static void MapApprove(RouteHandlerBuilder route) =>
        route
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(ApprovePermission).RequireCallerStudyScope()
            .RequireElectronicSignature(ApproveSemanticAction, ApproveActionFor);

    private static string ApproveActionFor(HttpContext httpContext) =>
        ApproveAction(httpContext.Request.RouteValues["id"]?.ToString() ?? string.Empty);

    public static Task<IResult> CompileDsurAsync(
        DsurCompileRequest request,
        [FromServices] DsurApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.CompileDsurAsync(request, httpContext, cancellationToken);

    public static Task<IResult> ApproveDsurAsync(
        string id,
        DsurApproveRequest? request,
        [FromServices] DsurApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.ApproveDsurAsync(id, request, httpContext, cancellationToken);

    public static Task<IResult> UpdateDsurAsync(
        string id,
        DsurUpdateRequest request,
        [FromServices] DsurApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.UpdateDsurAsync(id, request, httpContext, cancellationToken);

    public static Task<IResult> GetDsurByIdAsync(
        string id,
        [FromServices] DsurApplicationService service,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null,
        [FromQuery(Name = "revision")] int? revision = null,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf = null) =>
        service.GetDsurByIdAsync(id, cancellationToken, httpContext, revision, asOf);

    public static Task<IResult> ListDsurReportsAsync(
        [FromServices] DsurApplicationService service,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null) =>
        service.ListDsurReportsAsync(cancellationToken, httpContext);
}

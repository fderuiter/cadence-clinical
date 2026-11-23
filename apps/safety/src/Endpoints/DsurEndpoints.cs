using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

    public static async Task<IResult> CompileDsurAsync(
        DsurCompileRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.VerifiedUserId() is not { } callerUserId)
        {
            return VerifiedCallerRequired();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.CompoundName))
        {
            return TypedResults.Problem("CompoundName is required for DSUR compilation.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.StudyId))
            {
                if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
                {
                    return TypedResults.Problem(ClinicalScopeGuard.StudyAccessDeniedDetail, statusCode: StatusCodes.Status403Forbidden);
                }
            }
            else if (ClinicalScopeGuard.RequireAllStudies(httpContext) is { } allStudiesDenied)
            {
                return allStudiesDenied;
            }
        }

        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.CompileDsurAsync(request, callerUserId, cancellationToken);
        return TypedResults.Created($"/api/v1/safety/dsur/{result.Record.Id}", result.Record);
    }

    public static async Task<IResult> ApproveDsurAsync(
        string id,
        DsurApproveRequest? request,
        SafetyDbContext dbContext,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.VerifiedUserId() is not { } callerUserId)
        {
            return VerifiedCallerRequired();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ReasonForChange))
        {
            return TypedResults.Problem("A reason for change is required to approve and lock a DSUR.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null)
        {
            var existing = await dbContext.PeriodicSafetyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is not null && !CanCallerAccessReport(httpContext, existing))
            {
                return TypedResults.Problem(ClinicalScopeGuard.StudyAccessDeniedDetail, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.ApproveDsurAsync(id, request, callerUserId, cancellationToken, httpContext);

        return ToResult(result.Status, result.Message, result.Record);
    }

    public static async Task<IResult> UpdateDsurAsync(
        string id,
        DsurUpdateRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.VerifiedUserId() is not { } callerUserId)
        {
            return VerifiedCallerRequired();
        }

        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null)
        {
            var existing = await dbContext.PeriodicSafetyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is not null && !CanCallerAccessReport(httpContext, existing))
            {
                return TypedResults.Problem(ClinicalScopeGuard.StudyAccessDeniedDetail, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!string.IsNullOrWhiteSpace(request.StudyId) && !ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
            {
                return TypedResults.Problem(ClinicalScopeGuard.StudyAccessDeniedDetail, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.UpdateDsurAsync(id, request, callerUserId, cancellationToken, httpContext);

        return ToResult(result.Status, result.Message, result.Record);
    }

    public static async Task<IResult> GetDsurByIdAsync(
        string id,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null,
        [FromQuery(Name = "revision")] int? revision = null,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf = null)
    {
        var record = await dbContext.PeriodicSafetyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (record == null || (httpContext is not null && ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null && !CanCallerAccessReport(httpContext, record)))
        {
            return TypedResults.Problem($"DSUR periodic report record '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (revision.HasValue || asOf.HasValue)
        {
            var revQuery = dbContext.PeriodicSafetyReportRevisions
                .AsNoTracking()
                .Where(r => r.ReportId == record.Id);

            PeriodicSafetyReportRevision? matchedRevision = revision.HasValue
                ? await revQuery.FirstOrDefaultAsync(r => r.RevisionNumber == revision.Value, cancellationToken)
                : await revQuery
                    .Where(r => r.RecordedAt <= asOf!.Value)
                    .OrderByDescending(r => r.RevisionNumber)
                    .FirstOrDefaultAsync(cancellationToken);

            if (matchedRevision is null)
            {
                return TypedResults.Problem($"DSUR periodic report revision for '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
            }

            return TypedResults.Ok(matchedRevision.ToRecord(record.CreatedAt, record.CreatedBy));
        }

        return TypedResults.Ok(record);
    }

    public static async Task<IResult> ListDsurReportsAsync(
        SafetyDbContext dbContext,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null)
    {
        var query = dbContext.PeriodicSafetyReports.AsNoTracking();
        if (httpContext is not null && ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null)
        {
            string? requestedStudyId = httpContext.Request.Query["study_id"].FirstOrDefault();
            if (!ClinicalScopeGuard.ResolveStudyFilter(httpContext, requestedStudyId, out var studies, out var errorResult))
            {
                return errorResult!;
            }

            if (studies is not null)
            {
                query = query.Where(r => r.StudyId != null && studies.Contains(r.StudyId));
            }
        }

        var records = await query.ToArrayAsync(cancellationToken);
        return TypedResults.Ok(records);
    }

    private static bool CanCallerAccessReport(HttpContext httpContext, PeriodicSafetyReportRecord record) =>
        string.IsNullOrWhiteSpace(record.StudyId)
            ? ClinicalScopeGuard.RequireAllStudies(httpContext) is null
            : ClinicalScopeGuard.CanAccessStudy(httpContext, record.StudyId);

    private static IResult ToResult(DsurOperationStatus status, string message, PeriodicSafetyReportRecord? record) =>
        status switch
        {
            DsurOperationStatus.Success => TypedResults.Ok(record),
            DsurOperationStatus.Invalid => TypedResults.Problem(message, statusCode: StatusCodes.Status400BadRequest),
            DsurOperationStatus.Forbidden => TypedResults.Problem(message, statusCode: StatusCodes.Status403Forbidden),
            DsurOperationStatus.NotFound => TypedResults.Problem(message, statusCode: StatusCodes.Status404NotFound),
            DsurOperationStatus.Conflict => TypedResults.Problem(message, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(message, statusCode: StatusCodes.Status500InternalServerError)
        };

    /// <summary>A request without a verified caller is refused, never attributed to a default user (#5462).</summary>
    private static IResult VerifiedCallerRequired() =>
        TypedResults.Problem("A verified caller identity is required.", statusCode: StatusCodes.Status401Unauthorized);
}

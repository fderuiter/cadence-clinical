using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Endpoints;

public static class DsurEndpoints
{
    public const string SafetyPermission = "safety_report:create";

    public static RouteGroupBuilder MapDsurEndpoints(this RouteGroupBuilder group)
    {
        // Primary requirement endpoints
        group.MapPost("/dsur/compile", CompileDsurAsync)
            .WithName("CompileDsurReport")
            .WithSummary("Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SafetyPermission);

        group.MapPost("/dsur/{id}/approve", ApproveDsurAsync)
            .WithName("ApproveDsurReport")
            .WithSummary("Applies a 21 CFR Part 11 PKCS#7 e-signature block via GxPSigner and locks the DSUR record.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SafetyPermission);

        group.MapPut("/dsur/{id}", UpdateDsurAsync)
            .WithName("UpdateDsurReport")
            .WithSummary("Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SafetyPermission);

        group.MapGet("/dsur/{id}", GetDsurByIdAsync)
            .WithName("GetDsurReportById")
            .WithSummary("Retrieves a DSUR periodic safety report record by identifier.")
            .Produces<PeriodicSafetyReportRecord>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SafetyPermission);

        group.MapGet("/dsur", ListDsurReportsAsync)
            .WithName("ListDsurReports")
            .WithSummary("Lists all DSUR periodic safety report records.")
            .Produces<PeriodicSafetyReportRecord[]>(StatusCodes.Status200OK)
            .RequirePermission(SafetyPermission);

        // Alias routes for periodic-reports endpoint path conventions
        group.MapPost("/periodic-reports/compile", CompileDsurAsync)
            .WithName("CompilePeriodicReport")
            .RequirePermission(SafetyPermission);

        group.MapPost("/periodic-reports/{id}/approve", ApproveDsurAsync)
            .WithName("ApprovePeriodicReport")
            .RequirePermission(SafetyPermission);

        group.MapPost("/periodic-reports/{id}/sign", ApproveDsurAsync)
            .WithName("SignPeriodicReport")
            .RequirePermission(SafetyPermission);

        group.MapPut("/periodic-reports/{id}", UpdateDsurAsync)
            .WithName("UpdatePeriodicReport")
            .RequirePermission(SafetyPermission);

        group.MapGet("/periodic-reports/{id}", GetDsurByIdAsync)
            .WithName("GetPeriodicReportById")
            .RequirePermission(SafetyPermission);

        group.MapGet("/periodic-reports", ListDsurReportsAsync)
            .WithName("ListPeriodicReports")
            .RequirePermission(SafetyPermission);

        return group;
    }

    public static async Task<IResult> CompileDsurAsync(
        DsurCompileRequest request,
        SafetyDbContext dbContext,
        [FromServices] IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CompoundName))
        {
            return TypedResults.Problem("CompoundName is required for DSUR compilation.", statusCode: StatusCodes.Status400BadRequest);
        }

        var callerUserId = httpContext.User?.Identity?.Name ?? "safety-officer";
        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.CompileDsurAsync(request, callerUserId, cancellationToken);
        return TypedResults.Created($"/api/v1/safety/dsur/{result.Record.Id}", result.Record);
    }

    public static async Task<IResult> ApproveDsurAsync(
        string id,
        DsurApproveRequest? request,
        SafetyDbContext dbContext,
        [FromServices] IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerUserId = httpContext.User?.Identity?.Name ?? "safety-physician";
        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.ApproveDsurAsync(id, request ?? new DsurApproveRequest(null, null, null), callerUserId, cancellationToken);

        return result.Status switch
        {
            DsurOperationStatus.Success => TypedResults.Ok(result.Record),
            DsurOperationStatus.NotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            DsurOperationStatus.Conflict => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    public static async Task<IResult> UpdateDsurAsync(
        string id,
        DsurUpdateRequest request,
        SafetyDbContext dbContext,
        [FromServices] IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerUserId = httpContext.User?.Identity?.Name ?? "safety-officer";
        var compiler = new DsurCompilerEngine(dbContext, clock);

        var result = await compiler.UpdateDsurAsync(id, request, callerUserId, cancellationToken);

        return result.Status switch
        {
            DsurOperationStatus.Success => TypedResults.Ok(result.Record),
            DsurOperationStatus.NotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            DsurOperationStatus.Conflict => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    public static async Task<IResult> GetDsurByIdAsync(
        string id,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.PeriodicSafetyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (record == null)
        {
            return TypedResults.Problem($"DSUR periodic report record '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(record);
    }

    public static async Task<IResult> ListDsurReportsAsync(
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.PeriodicSafetyReports.AsNoTracking().ToArrayAsync(cancellationToken);
        return TypedResults.Ok(records);
    }
}

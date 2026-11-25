using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Application;

public sealed class DsurApplicationService(SafetyDbContext dbContext, IClock clock)
{
    public async Task<IResult> CompileDsurAsync(
        DsurCompileRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } callerUserId)
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

    public async Task<IResult> ApproveDsurAsync(
        string id,
        DsurApproveRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } callerUserId)
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

    public async Task<IResult> UpdateDsurAsync(
        string id,
        DsurUpdateRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } callerUserId)
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

    public async Task<IResult> GetDsurByIdAsync(
        string id,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null,
        int? revision = null,
        DateTimeOffset? asOf = null)
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

    public async Task<IResult> ListDsurReportsAsync(
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

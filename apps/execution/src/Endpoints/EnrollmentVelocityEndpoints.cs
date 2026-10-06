using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Enrollment;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Endpoints;

public static class EnrollmentVelocityEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentVelocityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/studies")
            .WithTags("EnrollmentVelocity");

        group.MapGet("/{studyId}/enrollment-velocity", GetEnrollmentVelocityAsync)
            .WithName("GetEnrollmentVelocity")
            .Produces<EnrollmentVelocityForecastResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{studyId}/enrollment-velocity/forecast", ForecastEnrollmentVelocityAsync)
            .WithName("ForecastEnrollmentVelocity")
            .Produces<EnrollmentVelocityForecastResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    public static async Task<IResult> GetEnrollmentVelocityAsync(
        [FromRoute] string studyId,
        [FromServices] ExecutionDbContext db,
        [FromServices] IClock clock,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return TypedResults.Problem("Study identifier is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var targetStudyCode = new StudyCode(studyId);
        int totalEnrolled = await db.ClinicalSubjects
            .AsNoTracking()
            .CountAsync(s => s.StudyCode == targetStudyCode, cancellationToken);

        int siteCount = await db.ClinicalSubjects
            .AsNoTracking()
            .Where(s => s.StudyCode == targetStudyCode)
            .Select(s => s.SiteCode)
            .Distinct()
            .CountAsync(cancellationToken);

        siteCount = Math.Max(1, siteCount);

        var paramsVal = new EnrollmentVelocityParams(
            StudyId: studyId,
            TargetSubjects: Math.Max(100, (int)Math.Ceiling(totalEnrolled * 1.5)),
            TargetCompletionDate: today.AddMonths(6),
            HistoricalWeeklyRate: Math.Max(2.5, Math.Round(totalEnrolled / 4.0, 2)),
            CurrentlyEnrolled: totalEnrolled,
            ActiveSiteCount: siteCount,
            SimulationHorizonWeeks: 24);

        var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(paramsVal, today);
        return TypedResults.Ok(result);
    }

    public static IResult ForecastEnrollmentVelocityAsync(
        [FromRoute] string studyId,
        [FromBody] EnrollmentVelocityParams request,
        [FromServices] IClock clock)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return TypedResults.Problem("Study identifier is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var effectiveParams = request with { StudyId = studyId };

        try
        {
            var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(effectiveParams, today);
            return TypedResults.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

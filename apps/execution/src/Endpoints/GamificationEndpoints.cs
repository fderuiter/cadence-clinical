using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Gamification;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for participant gamification progress, streak tracking, and milestone evaluations.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/gamification")
            .WithTags("Gamification");

        group.MapGet("/progress", GetGamificationProgressAsync)
            .WithName("GetGamificationProgress")
            .Produces<GamificationProgressResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/process-submission", ProcessSubmissionGamificationAsync)
            .WithName("ProcessSubmissionGamification")
            .Produces<GamificationProgressResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetGamificationProgressAsync(
        [FromQuery] string subjectId,
        [FromQuery] string studyId,
        IGamificationService gamificationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(studyId))
        {
            return Results.Problem(
                detail: "subjectId and studyId query parameters are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await gamificationService.GetProgressAsync(subjectId, studyId, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ProcessSubmissionGamificationAsync(
        [FromBody] ProcessGamificationSubmissionRequest request,
        IGamificationService gamificationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.StudyId))
        {
            return Results.Problem(
                detail: "SubjectId and StudyId are required in submission request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await gamificationService.ProcessSubmissionAsync(request, cancellationToken);
        return Results.Ok(result);
    }
}

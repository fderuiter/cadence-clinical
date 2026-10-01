using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Exports;
using Cadence.Apps.Execution.Endpoints.Filters;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API endpoints for background dataset export queue and status polling.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/exports")
            .WithTags("Exports");

        group.MapPost("/", SubmitExportJobAsync)
            .WithName("SubmitExportJob")
            .Produces<ExportJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AddEndpointFilter<ValidationFilter<ExportJobCreateRequest>>();

        group.MapGet("/{id}", GetExportJobAsync)
            .WithName("GetExportJob")
            .Produces<ExportJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListExportJobsAsync)
            .WithName("ListExportJobs")
            .Produces<IReadOnlyList<ExportJobResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id}/download", DownloadExportArtifactAsync)
            .WithName("DownloadExportArtifact")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> SubmitExportJobAsync(
        ExportJobCreateRequest request,
        ClaimsPrincipal user,
        IExportJobService exportJobService,
        CancellationToken ct)
    {
        var caller = user.ToAuthenticatedCaller() ?? new AuthenticatedCaller("system.export", System.Array.Empty<string>());
        var response = await exportJobService.CreateJobAsync(request, caller, ct).ConfigureAwait(false);

        return Results.Accepted($"/api/v1/execution/exports/{response.JobId}", response);
    }

    public static IResult GetExportJobAsync(
        string id,
        IExportJobService exportJobService)
    {
        var job = exportJobService.GetJob(id);
        if (job is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Export Job Not Found", detail: $"Export job with ID '{id}' was not found.");
        }

        return Results.Ok(job);
    }

    public static IResult ListExportJobsAsync(
        [FromQuery] string? studyId,
        IExportJobService exportJobService)
    {
        var jobs = exportJobService.ListJobs(studyId);
        return Results.Ok(jobs);
    }

    public static IResult DownloadExportArtifactAsync(
        string id,
        IExportJobService exportJobService)
    {
        var artifact = exportJobService.GetArtifact(id);
        if (artifact is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Export Artifact Not Found or Expired", detail: $"Export artifact for job '{id}' was not found or has expired.");
        }

        return Results.Bytes(artifact.Content, contentType: artifact.ContentType, fileDownloadName: artifact.FileName);
    }
}

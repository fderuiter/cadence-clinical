using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

public static class SceExecutionEndpoints
{
    public static IEndpointRouteBuilder MapSceExecutionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/interop/sce")
            .WithTags("Cadence.Apps.Interop");

        group.MapPost("/execute", ExecuteProgramAsync)
            .RequirePermission("sce_run:execute")
            .RequireCallerStudyScope()
            .Produces<ExecuteProgramResponseDto>(StatusCodes.Status200OK);

        group.MapGet("/runs", ListRunsAsync)
            .RequirePermission("sce_run:read")
            .RequireCallerStudyScope()
            .Produces<ExecutionRunListResponseDto>(StatusCodes.Status200OK);

        group.MapGet("/runs/{run_id}", GetRunAsync)
            .RequirePermission("sce_run:read")
            .RequireCallerStudyScope()
            .Produces<ExecutionRunDetailResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/runs/{run_id}/verify", VerifyRunManifestAsync)
            .RequirePermission("sce_run:read")
            .RequireCallerStudyScope()
            .Produces<VerifyManifestResponseDto>(StatusCodes.Status200OK);

        group.MapGet("/runs/{run_id}/artifacts/{artifact_id}", GetRunArtifactAsync)
            .RequirePermission("sce_run:read")
            .RequireCallerStudyScope()
            .Produces<Dictionary<string, object?>>(StatusCodes.Status200OK);

        return routes;
    }

    private static Task<IResult> ExecuteProgramAsync(
        ExecuteProgramRequestDto req,
        [FromServices] ISceExecutionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ExecuteProgramAsync(req, httpContext, ct);

    private static Task<IResult> ListRunsAsync(
        string study_id,
        string? program_id,
        string? snapshot_id,
        [FromServices] ISceExecutionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListRunsAsync(study_id, program_id, snapshot_id, httpContext, ct);

    private static Task<IResult> GetRunAsync(
        string run_id,
        [FromServices] ISceExecutionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetRunAsync(run_id, httpContext, ct);

    private static Task<IResult> VerifyRunManifestAsync(
        string run_id,
        [FromServices] ISceExecutionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.VerifyRunManifestAsync(run_id, httpContext, ct);

    private static Task<IResult> GetRunArtifactAsync(
        string run_id,
        string artifact_id,
        [FromServices] ISceExecutionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetRunArtifactAsync(run_id, artifact_id, httpContext, ct);
}

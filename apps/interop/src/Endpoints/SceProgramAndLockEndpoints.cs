using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

public static class SceProgramAndLockEndpoints
{
    public static IEndpointRouteBuilder MapSceProgramAndLockEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/interop/sce")
            .WithTags("Cadence.Apps.Interop");

        group.MapGet("/programs", ListProgramsAsync)
            .RequirePermission("sce_program:read")
            .RequireCallerStudyScope()
            .Produces<ProgramListResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/programs", CreateProgramAsync)
            .RequirePermission("sce_program:create")
            .RequireCallerStudyScope()
            .Produces<ProgramResponseDto>(StatusCodes.Status201Created);

        group.MapGet("/programs/{program_id}", GetProgramByIdAsync)
            .RequirePermission("sce_program:read")
            .RequireCallerStudyScope()
            .Produces<ProgramResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/validate-lockfile", ValidateLockfile)
            .RequirePermission("sce_program:read")
            .RequireTenantScope()
            .Produces<ValidateLockfileResponseDto>(StatusCodes.Status200OK);

        group.MapGet("/lock-tokens", ListLockTokensAsync)
            .RequirePermission("sce_lock_token:read")
            .RequireCallerStudyScope()
            .Produces<LockTokenListResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/lock-tokens", IssueLockTokenAsync)
            .RequirePermission("sce_lock_token:issue")
            .RequireCallerStudyScope()
            .Produces<LockTokenResponseDto>(StatusCodes.Status201Created);

        group.MapPost("/lock-tokens/{token_id}/revoke", RevokeLockTokenAsync)
            .RequirePermission("sce_lock_token:revoke")
            .RequireCallerStudyScope()
            .Produces<Dictionary<string, object>>(StatusCodes.Status200OK);

        return routes;
    }

    public static Task<IResult> ListProgramsAsync(
        string study_id,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListProgramsAsync(study_id, httpContext, ct);

    public static Task<IResult> CreateProgramAsync(
        CreateProgramRequestDto req,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CreateProgramAsync(req, httpContext, ct);

    public static Task<IResult> GetProgramByIdAsync(
        string program_id,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetProgramByIdAsync(program_id, httpContext, ct);

    public static IResult ValidateLockfile(
        ValidateLockfileRequestDto req,
        SceProgramAndLockService service) =>
        service.ValidateLockfile(req);

    public static Task<IResult> ListLockTokensAsync(
        string study_id,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListLockTokensAsync(study_id, httpContext, ct);

    public static Task<IResult> IssueLockTokenAsync(
        IssueLockTokenRequestDto req,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.IssueLockTokenAsync(req, httpContext, ct);

    public static Task<IResult> RevokeLockTokenAsync(
        string token_id,
        string? reason,
        SceProgramAndLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RevokeLockTokenAsync(token_id, reason, httpContext, ct);
}

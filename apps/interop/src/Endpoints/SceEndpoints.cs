using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// The statistical computing environment. Every route needs an <c>sce_*</c> permission from the permission matrix,
/// and identity and roles come only from the verified gateway principal (#5382). An unblinded run needs
/// <c>sce_run:execute_unblinded</c> and a database-lock token issued to the caller and signed with
/// <c>CADENCE_SCE_SIGNING_KEY</c>; unblinded runs are visible only with <c>sce_run:read_unblinded</c>. Every route that
/// names a study also requires the caller to be assigned to it in the signed <c>study_ids</c> claim (ADR-2516, #5402): a
/// list outside the caller's studies is empty, one record outside them is not found, and a write outside them is
/// refused with <c>403</c> and writes nothing.
/// @req:PRD-CDR-006
/// </summary>
public static class SceEndpoints
{
    /// <summary>Permission to run an unblinded analysis with a lock token.</summary>
    public const string ExecuteUnblindedPermission = "sce_run:execute_unblinded";

    /// <summary>Permission to see unblinded runs and their artifacts.</summary>
    public const string ReadUnblindedPermission = "sce_run:read_unblinded";

    /// <summary>The roles a lock token authorizes: exactly those the matrix lets run unblinded analyses.</summary>
    public static IReadOnlyList<string> UnblindedExecutorRoles { get; } = PermissionMatrix.ROLE_PERMISSIONS
        .Where(r => r.Value.TryGetValue("sce_run", out var actions) && actions.Contains("execute_unblinded"))
        .Select(r => r.Key)
        .Order(StringComparer.Ordinal)
        .ToList();

    public static IEndpointRouteBuilder MapSceEndpoints(this IEndpointRouteBuilder routes)
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

        group.MapGet("/programs/{program_id}", GetProgramAsync)
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

        routes.MapSceExecutionEndpoints();
        return routes;
    }

    internal static bool TryBuildSandboxExecutionRequest(
        StatisticalProgram program,
        string lockfileContent,
        ExecutionSandboxConfigDto? sandboxConfig,
        out SandboxExecutionRequest sandboxReq,
        [NotNullWhen(false)] out IResult? errorResult) =>
        SceExecutionService.TryBuildSandboxExecutionRequest(
            program,
            lockfileContent,
            sandboxConfig,
            out sandboxReq,
            out errorResult);

    private static Task<IResult> ListProgramsAsync(
        string study_id,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListProgramsAsync(study_id, httpContext, ct);

    private static Task<IResult> CreateProgramAsync(
        CreateProgramRequestDto req,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CreateProgramAsync(req, httpContext, ct);

    private static Task<IResult> GetProgramAsync(
        string program_id,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetProgramAsync(program_id, httpContext, ct);

    private static IResult ValidateLockfile(
        ValidateLockfileRequestDto req,
        [FromServices] ISceProgramLockService service) =>
        service.ValidateLockfile(req);

    private static Task<IResult> ListLockTokensAsync(
        string study_id,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListLockTokensAsync(study_id, httpContext, ct);

    private static Task<IResult> IssueLockTokenAsync(
        IssueLockTokenRequestDto req,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.IssueLockTokenAsync(req, httpContext, ct);

    private static Task<IResult> RevokeLockTokenAsync(
        string token_id,
        string? reason,
        [FromServices] ISceProgramLockService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RevokeLockTokenAsync(token_id, reason, httpContext, ct);
}

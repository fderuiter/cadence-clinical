using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// The statistical computing environment.
/// @req:PRD-CDR-006
/// </summary>
public static class SceEndpoints
{
    public const string ExecuteUnblindedPermission = SceExecutionService.ExecuteUnblindedPermission;
    public const string ReadUnblindedPermission = SceExecutionService.ReadUnblindedPermission;

    public static IReadOnlyList<string> UnblindedExecutorRoles => SceProgramAndLockService.UnblindedExecutorRoles;

    public static IEndpointRouteBuilder MapSceEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapSceProgramAndLockEndpoints();
        routes.MapSceExecutionEndpoints();
        return routes;
    }

    internal static bool TryBuildSandboxExecutionRequest(
        StatisticalProgram program,
        string lockfileContent,
        ExecutionSandboxConfigDto? sandboxConfig,
        out SandboxExecutionRequest sandboxReq,
        [NotNullWhen(false)] out IResult? errorResult) =>
        SceExecutionService.TryBuildSandboxExecutionRequest(program, lockfileContent, sandboxConfig, out sandboxReq, out errorResult);
}

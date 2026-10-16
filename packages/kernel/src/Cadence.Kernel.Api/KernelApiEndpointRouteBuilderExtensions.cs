using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Minimal API surface of the Clinical Kernel (spec §2.4). Endpoint groups are added by the slices that introduce
/// their commands; every mutating endpoint goes through the kernel command pipeline (K-AUTH-1).
/// @req:PRD-SYS-053
/// </summary>
public static class KernelApiEndpointRouteBuilderExtensions
{
    /// <summary>Route prefix for all kernel endpoints.</summary>
    public const string RoutePrefix = "/api/v1/kernel";

    /// <summary>
    /// The host policy every kernel route requires: a signed-in caller (ADR-2516). The kernel then authorizes each
    /// object itself from the caller's grants: reads mask what the grants do not cover (ADR-2457, ADR-2471) and every
    /// command goes through the authority evaluator (K-AUTH-1), so the route needs no role permission.
    /// </summary>
    public const string SignedInCallerPolicy = "SignedInCaller";

    /// <summary>Maps the kernel route group and returns it so slices can add endpoints.</summary>
    public static RouteGroupBuilder MapKernelApi(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGroup(RoutePrefix).WithTags("Kernel").RequireAuthorization(SignedInCallerPolicy);
    }
}

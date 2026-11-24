using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Finds the kernel tenant a request acts in, from the caller's verified claims (K-ID-2). The host implements it; the
/// endpoints never take a tenant from the route, the query or the body (ADR-2540).
/// </summary>
public interface IKernelTenantResolver
{
    /// <summary>The kernel tenant of <paramref name="caller"/>, or why there is none.</summary>
    ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken);
}

/// <summary>The outcome of resolving a request's kernel tenant.</summary>
public sealed record KernelTenantResolution
{
    private KernelTenantResolution(TenantId? tenant, int failureStatus, string? failureDetail)
    {
        Tenant = tenant;
        FailureStatus = failureStatus;
        FailureDetail = failureDetail;
    }

    /// <summary>The caller's kernel tenant, or null when it could not be resolved.</summary>
    public TenantId? Tenant { get; }

    /// <summary>The HTTP status to answer with when <see cref="Tenant"/> is null.</summary>
    public int FailureStatus { get; }

    /// <summary>Why the tenant could not be resolved, for the problem response.</summary>
    public string? FailureDetail { get; }

    /// <summary>The caller acts in <paramref name="tenant"/>.</summary>
    public static KernelTenantResolution Resolved(TenantId tenant)
    {
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("A resolved tenant is never empty.", nameof(tenant));
        }

        return new(tenant, 0, null);
    }

    /// <summary>The caller's tenant is not an active kernel tenant: the request is refused with 403.</summary>
    public static KernelTenantResolution Unknown(string detail) =>
        new(null, StatusCodes.Status403Forbidden, detail);

    /// <summary>The host cannot resolve tenants yet: the request is refused with 503.</summary>
    public static KernelTenantResolution Unavailable(string detail) =>
        new(null, StatusCodes.Status503ServiceUnavailable, detail);
}

/// <summary>
/// Resolves the kernel tenant before a kernel endpoint runs, and answers with problem details when there is none.
/// Endpoints read the resolved tenant with <see cref="KernelRequestTenant.Get"/>.
/// </summary>
public sealed class KernelTenantFilter(IKernelTenantResolver resolver) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var resolution = await resolver.ResolveAsync(http.User, http.RequestAborted).ConfigureAwait(false);
        if (resolution.Tenant is not { } tenant)
        {
            return TypedResults.Problem(
                title: resolution.FailureStatus == StatusCodes.Status503ServiceUnavailable
                    ? "Kernel tenant resolution is not available"
                    : "No kernel tenant",
                detail: resolution.FailureDetail,
                statusCode: resolution.FailureStatus);
        }

        KernelRequestTenant.Set(http, tenant);
        var result = await next(context).ConfigureAwait(false);
        await KernelErrorEndpoints.MaskProblemResultAsync(result, tenant, http, http.RequestAborted).ConfigureAwait(false);
        return result;
    }
}

/// <summary>The kernel tenant <see cref="KernelTenantFilter"/> resolved for the current request.</summary>
public static class KernelRequestTenant
{
    private static readonly object Key = new();

    /// <summary>The request's kernel tenant. Throws when the endpoint does not run behind <see cref="KernelTenantFilter"/>.</summary>
    public static TenantId Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(Key, out var value) && value is TenantId tenant
            ? tenant
            : throw new InvalidOperationException("The kernel tenant is resolved by KernelTenantFilter, which this endpoint does not run behind.");
    }

    internal static void Set(HttpContext context, TenantId tenant) => context.Items[Key] = tenant;
}

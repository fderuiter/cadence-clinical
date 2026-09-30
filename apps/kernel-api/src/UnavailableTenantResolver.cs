using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api;

namespace Cadence.Kernel.Host;

/// <summary>
/// Refuses every request with 503 until the kernel can find the kernel tenant for the tenant code the gateway signs
/// (the tenant directory read port). There is deliberately no configured mapping in the meantime (ADR-2540).
/// </summary>
internal sealed class UnavailableTenantResolver : IKernelTenantResolver
{
    public const string Detail = "The kernel can't match your sign-in to a kernel tenant yet, so it serves no data.";

    public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
        ValueTask.FromResult(KernelTenantResolution.Unavailable(Detail));
}

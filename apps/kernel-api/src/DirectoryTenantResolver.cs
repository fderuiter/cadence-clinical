using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;

namespace Cadence.Kernel.Host;

/// <summary>
/// Finds the caller's kernel tenant from the tenant code in the gateway-signed identity (K-ID-2, ADR-2540), through the
/// kernel's tenant directory. The code is never read from the route, the query or the body. A code that names no active
/// kernel tenant is refused with 403; there is no fallback tenant.
/// </summary>
internal sealed class DirectoryTenantResolver(ITenantDirectory directory) : IKernelTenantResolver
{
    public const string NoTenantDetail = "Your sign-in names no tenant.";
    public const string UnknownTenantDetail = "Your sign-in's tenant is not an active kernel tenant.";

    public async ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        if (caller.FindFirst(CadenceClaimTypes.TenantId)?.Value is not { Length: > 0 } code)
        {
            return KernelTenantResolution.Unknown(NoTenantDetail);
        }

        return await directory.FindActiveByCodeAsync(code, cancellationToken).ConfigureAwait(false) is { } tenant
            ? KernelTenantResolution.Resolved(tenant)
            : KernelTenantResolution.Unknown(UnknownTenantDetail);
    }
}

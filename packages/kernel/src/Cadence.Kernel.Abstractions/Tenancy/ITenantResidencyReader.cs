using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Where a tenant's data must stay (spec §13, ADR-2458). <paramref name="Region"/> is the residency code set when the
/// tenant was provisioned, two to eight upper-case letters, or null when the tenant is unrestricted.
/// </summary>
/// <param name="Region">The residency code, or null.</param>
public sealed record TenantResidency(string? Region);

/// <summary>
/// Reads a tenant's residency, for modules that must keep the tenant's data in its region, such as the Intelligence
/// module routing model calls (ADR-2636). Reads through a context bound to that tenant, so it sees nothing else.
/// </summary>
public interface ITenantResidencyReader
{
    /// <summary>The residency of <paramref name="tenant"/>, or null when the tenant does not exist or is inactive.</summary>
    Task<TenantResidency?> FindAsync(TenantId tenant, CancellationToken cancellationToken);
}

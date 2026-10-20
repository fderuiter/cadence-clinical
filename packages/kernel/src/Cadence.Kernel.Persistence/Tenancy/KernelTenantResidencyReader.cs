using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Reads a tenant's own row through a context bound to it; row-level security shows a tenant its own row only (K-ID-2).
/// </summary>
internal sealed class KernelTenantResidencyReader(KernelTenantDbContextFactory contexts) : ITenantResidencyReader
{
    public async Task<TenantResidency?> FindAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        if (tenant.IsEmpty)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant && t.IsActive)
            .Select(t => new TenantResidency(t.Residency))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
}

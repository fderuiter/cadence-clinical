using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Visits each tenant id returned by <see cref="TenantIsolationSql.DirectoryFunction"/> under its own RLS-bound
/// <see cref="KernelDbContext"/> (ADR-2250 rule 3) to verify that a trial-onboarding caller holds no foreign grant
/// or tenant membership.
/// </summary>
internal sealed class KernelSubjectTenantBindingProbe(KernelTenantDbContextFactory tenantFactory) : ISubjectTenantBindingProbe
{
    public async Task<bool> HasForeignBindingAsync(
        string subjectId,
        PersonId derivedPersonId,
        TenantId excludedTenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        PersonId? directPersonId = Guid.TryParse(subjectId, out var parsed)
            ? PersonId.FromGuid(parsed)
            : null;

        var tenants = await tenantFactory.ListTenantsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var tenantId in tenants)
        {
            if (tenantId == excludedTenantId)
            {
                continue;
            }

            await using var db = tenantFactory.Create(tenantId);
            if (await db.Grants
                    .AsNoTracking()
                    .AnyAsync(g => g.GranteeKind == ActorKind.Person && g.GranteeId == subjectId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }

            if (await db.TenantMemberships
                    .AsNoTracking()
                    .AnyAsync(
                        m => m.PersonId == derivedPersonId
                            || (directPersonId != null && m.PersonId == directPersonId.Value),
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}

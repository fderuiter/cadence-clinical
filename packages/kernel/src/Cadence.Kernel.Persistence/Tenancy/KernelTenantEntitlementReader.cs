using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Reads commercial <c>Subscription</c> and <c>ProductEntitlement</c> rows from the tenant-isolated kernel database.
/// Never consulted by <c>AuthorityEvaluator</c> or <c>GrantNodeVisibility</c>: commercial plans govern module
/// packaging and post-expiry workspace mutability only, never clinical data access.
/// </summary>
internal sealed class KernelTenantEntitlementReader(KernelTenantDbContextFactory contexts) : ITenantEntitlementReader
{
    public async Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
        TenantId tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("Entitlements are read within a tenant.", nameof(tenantId));
        }

        await using var db = contexts.Create(tenantId);
        var subscription = await db.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return new TenantEntitlementSnapshot(
                tenantId,
                SubscriptionId: null,
                CommercialTier.Individual,
                SubscriptionStatus.Active,
                StartedAt: null,
                TrialEndsAt: null,
                IsReadOnly: false,
                ProductFamilies: [ProductFamily.Blueprint, ProductFamily.Capture]);
        }

        var instant = KernelTimestamps.ToStoredPrecision(asOf);
        var isExpired = subscription.IsExpiredAt(instant);
        var status = subscription.EffectiveStatusAt(instant);

        var families = await db.ProductEntitlements
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.SubscriptionId == subscription.Id && e.IsEnabled)
            .OrderBy(e => e.Family)
            .Select(e => e.Family)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TenantEntitlementSnapshot(
            tenantId,
            subscription.Id,
            subscription.Tier,
            status,
            subscription.StartedAt,
            subscription.TrialEndsAt,
            IsReadOnly: isExpired,
            ProductFamilies: families);
    }
}

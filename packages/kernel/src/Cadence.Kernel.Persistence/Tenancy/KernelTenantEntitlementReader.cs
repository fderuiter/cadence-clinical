using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Reads commercial <c>Subscription</c>, <c>SubscriptionPeriod</c>, and <c>ProductEntitlement</c> rows from the
/// tenant-isolated kernel database, supporting both current recorded state and bitemporal <c>(asOf, knownAt)</c> queries.
/// Never consulted by <c>AuthorityEvaluator</c> or <c>GrantNodeVisibility</c>: commercial plans govern module
/// packaging, contracted scoping caps, and post-expiry workspace mutability only, never clinical data access.
/// </summary>
internal sealed class KernelTenantEntitlementReader(KernelTenantDbContextFactory contexts) : ITenantEntitlementReader
{
    public async Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
        TenantId tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken,
        DateTimeOffset? knownAt = null)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("Entitlements are read within a tenant.", nameof(tenantId));
        }

        await using var lease = await contexts.AcquireReadLeaseAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var db = lease.Db;
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

        var validInstant = KernelTimestamps.ToStoredPrecision(asOf);
        if (knownAt is { } explicitKnownAt)
        {
            var recordedInstant = KernelTimestamps.ToStoredPrecision(explicitKnownAt);
            var historicalPeriod = subscription.Periods
                .FirstOrDefault(p => p.Period.IsEffectiveAsOf(validInstant, recordedInstant));
            if (historicalPeriod is null)
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

            var historicalFamilies = subscription.Entitlements
                .Where(e => e.Period.IsEffectiveAsOf(validInstant, recordedInstant) && e.IsEnabled)
                .Select(e => e.Family)
                .Distinct()
                .OrderBy(f => (int)f)
                .ToArray();

            return new TenantEntitlementSnapshot(
                tenantId,
                subscription.Id,
                historicalPeriod.Tier,
                historicalPeriod.EffectiveStatusAt(validInstant),
                historicalPeriod.StartedAt,
                historicalPeriod.TrialEndsAt,
                IsReadOnly: historicalPeriod.IsExpiredAt(validInstant),
                ProductFamilies: historicalFamilies,
                Scoping: historicalPeriod.GetScoping());
        }

        var currentPeriod = subscription.Periods
            .Where(p => p.IsCurrent)
            .FirstOrDefault(p => p.Period.Valid.Contains(validInstant))
            ?? subscription.CurrentPeriod;

        var currentFamilies = subscription.Entitlements
            .Where(e => e.IsCurrent && e.IsEnabled && (e.Period.Valid.Contains(validInstant) || e.Period.Valid.End is null))
            .Select(e => e.Family)
            .Distinct()
            .OrderBy(f => (int)f)
            .ToArray();

        return new TenantEntitlementSnapshot(
            tenantId,
            subscription.Id,
            currentPeriod.Tier,
            currentPeriod.EffectiveStatusAt(validInstant),
            currentPeriod.StartedAt,
            currentPeriod.TrialEndsAt,
            IsReadOnly: currentPeriod.IsExpiredAt(validInstant),
            ProductFamilies: currentFamilies,
            Scoping: currentPeriod.GetScoping());
    }
}

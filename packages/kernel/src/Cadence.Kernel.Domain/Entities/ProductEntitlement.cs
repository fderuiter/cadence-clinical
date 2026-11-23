using System;

using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// One bitemporal revision of a commercial <see cref="ProductEntitlement"/> enabling or disabling a
/// <see cref="ProductFamily"/> under a tenant's <see cref="Subscription"/> (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Guarded by K-TIME-1 (<c>KT001</c> append-only trigger) and K-TIME-2 (<c>(tenant_id, subscription_id, family)</c>
/// exclusion constraint) so revisions never overwrite <see cref="CreatedBy"/> or <see cref="ReasonForChange"/>.
/// </summary>
public class ProductEntitlement : IBitemporalEntity
{
    protected ProductEntitlement() { }

    internal ProductEntitlement(
        ProductEntitlementId id,
        TenantId tenantId,
        SubscriptionId subscriptionId,
        ProductFamily family,
        bool isEnabled,
        DateTimeOffset grantedAt,
        TemporalPeriod period,
        string createdBy,
        string reasonForChange)
    {
        Id = id;
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        Family = family;
        IsEnabled = isEnabled;
        GrantedAt = KernelTimestamps.ToStoredPrecision(grantedAt);
        Period = period;
        CreatedAt = period.Recorded.Start.UtcDateTime;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
        VersionIndex = 1L;
    }

    public ProductEntitlementId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public SubscriptionId SubscriptionId { get; private set; }

    public ProductFamily Family { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public TemporalPeriod Period { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string ReasonForChange { get; private set; } = string.Empty;

    public long VersionIndex { get; private set; } = 1L;

    /// <summary>True when this row has not been superseded in recorded time.</summary>
    public bool IsCurrent => Period.Recorded.IsOpen;

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}

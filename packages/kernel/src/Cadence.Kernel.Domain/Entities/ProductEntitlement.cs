using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Commercial entitlement enabling one <see cref="ProductFamily"/> for a tenant's <see cref="Subscription"/>
/// (<c>docs/strategy/PRODUCT_CATALOG.md</c>). Baseline kernel capabilities (audit trails, electronic signatures, and
/// authorization checks) are never gated by product entitlements, and an entitlement never confers clinical grants.
/// </summary>
public class ProductEntitlement : AggregateRoot<ProductEntitlementId>
{
    public TenantId TenantId { get; private set; }

    public SubscriptionId SubscriptionId { get; private set; }

    public ProductFamily Family { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset GrantedAt { get; private set; }

    protected ProductEntitlement() { }

    public ProductEntitlement(
        ProductEntitlementId id,
        TenantId tenantId,
        SubscriptionId subscriptionId,
        ProductFamily family,
        DateTimeOffset grantedAt,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        Family = family;
        IsEnabled = true;
        GrantedAt = KernelTimestamps.ToStoredPrecision(grantedAt);
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void Enable(SubscriptionId subscriptionId, DateTimeOffset grantedAt, string actorId, string reasonForChange)
    {
        SubscriptionId = subscriptionId;
        IsEnabled = true;
        GrantedAt = KernelTimestamps.ToStoredPrecision(grantedAt);
        CreatedBy = actorId;
        ReasonForChange = reasonForChange;
    }

    public void Disable(string actorId, string reasonForChange)
    {
        IsEnabled = false;
        CreatedBy = actorId;
        ReasonForChange = reasonForChange;
    }
}

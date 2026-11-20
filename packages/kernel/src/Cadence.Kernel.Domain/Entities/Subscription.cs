using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Commercial subscription boundary for a tenant (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Keeps commercial lifecycle state (<see cref="CommercialTier"/>, <see cref="SubscriptionStatus"/>, and trial window)
/// separate from <see cref="Tenant"/>, which owns only isolation and data residency.
/// Never grants clinical data access or bypasses kernel authority, blinding, or consent.
/// </summary>
public class Subscription : AggregateRoot<SubscriptionId>
{
    public TenantId TenantId { get; private set; }

    public CommercialTier Tier { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? TrialEndsAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    protected Subscription() { }

    public Subscription(
        SubscriptionId id,
        TenantId tenantId,
        CommercialTier tier,
        SubscriptionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? trialEndsAt,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        Tier = tier;
        Status = status;
        StartedAt = KernelTimestamps.ToStoredPrecision(startedAt);
        TrialEndsAt = trialEndsAt.HasValue ? KernelTimestamps.ToStoredPrecision(trialEndsAt.Value) : null;
        ExpiredAt = status == SubscriptionStatus.Expired ? StartedAt : null;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public bool IsExpiredAt(DateTimeOffset asOf)
    {
        var instant = KernelTimestamps.ToStoredPrecision(asOf);
        return Status == SubscriptionStatus.Expired
            || (Status == SubscriptionStatus.Trialing && TrialEndsAt is { } endsAt && endsAt <= instant);
    }

    public SubscriptionStatus EffectiveStatusAt(DateTimeOffset asOf) =>
        IsExpiredAt(asOf) ? SubscriptionStatus.Expired : Status;

    public void Expire(DateTimeOffset expiredAt, string actorId, string reasonForChange)
    {
        var instant = KernelTimestamps.ToStoredPrecision(expiredAt);
        Status = SubscriptionStatus.Expired;
        ExpiredAt ??= instant;
        CreatedBy = actorId;
        ReasonForChange = reasonForChange;
    }

    public void Activate(CommercialTier tier, string actorId, string reasonForChange)
    {
        Tier = tier;
        Status = SubscriptionStatus.Active;
        TrialEndsAt = null;
        ExpiredAt = null;
        CreatedBy = actorId;
        ReasonForChange = reasonForChange;
    }
}

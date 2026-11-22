using System;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;

namespace Cadence.Kernel.Events;

/// <summary>A commercial subscription was provisioned for a tenant.</summary>
[KernelEvent("SubscriptionProvisioned", 1)]
public sealed record SubscriptionProvisioned(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    CommercialTier Tier,
    SubscriptionStatus Status,
    DateTimeOffset? TrialEndsAt);

/// <summary>A commercial subscription expired into read-and-export-only mode.</summary>
[KernelEvent("SubscriptionExpired", 1)]
public sealed record SubscriptionExpired(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    DateTimeOffset ExpiredAt);

/// <summary>A trial or active subscription switched commercial tier without resetting its trial window.</summary>
[KernelEvent("SubscriptionTrialTierSwitched", 1)]
public sealed record SubscriptionTrialTierSwitched(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    CommercialTier Tier,
    SubscriptionStatus Status,
    DateTimeOffset? TrialEndsAt);

/// <summary>A commercial subscription was activated with validated study scoping.</summary>
[KernelEvent("SubscriptionActivated", 1)]
public sealed record SubscriptionActivated(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    CommercialTier Tier,
    SubscriptionScoping? Scoping = null);

/// <summary>A product family entitlement was granted to a tenant subscription.</summary>
[KernelEvent("ProductEntitlementGranted", 1)]
public sealed record ProductEntitlementGranted(
    TenantId TenantId,
    ProductEntitlementId ProductEntitlementId,
    SubscriptionId SubscriptionId,
    ProductFamily Family);

/// <summary>A product family entitlement was disabled on a tenant subscription.</summary>
[KernelEvent("ProductEntitlementRevoked", 1)]
public sealed record ProductEntitlementRevoked(
    TenantId TenantId,
    ProductEntitlementId ProductEntitlementId,
    SubscriptionId SubscriptionId,
    ProductFamily Family);

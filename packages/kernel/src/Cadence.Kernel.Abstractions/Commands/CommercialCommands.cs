using System;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Tenancy;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Provisions a commercial <c>Subscription</c> for a tenant (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Commercial state never grants clinical data access or bypasses kernel authority, blinding, or consent.
/// </summary>
[KernelCapability("subscription.provision", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ProvisionSubscription(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    CommercialTier Tier,
    SubscriptionStatus Status,
    DateTimeOffset? TrialEndsAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<SubscriptionId>;

/// <summary>
/// Transitions a tenant's commercial <c>Subscription</c> to <see cref="SubscriptionStatus.Expired"/>, blocking new
/// study mutations while preserving read, export, electronic signature, and audit capabilities.
/// </summary>
[KernelCapability("subscription.expire", VerbClass.Approve, Resource = nameof(SubscriptionId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ExpireSubscription(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<SubscriptionId>;

/// <summary>
/// Activates or upgrades a tenant's commercial <c>Subscription</c>, clearing trial read-only restrictions.
/// </summary>
[KernelCapability("subscription.activate", VerbClass.Approve, Resource = nameof(SubscriptionId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ActivateSubscription(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    CommercialTier Tier,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<SubscriptionId>;

/// <summary>
/// Grants a commercial <c>ProductEntitlement</c> for one product family under a tenant's <c>Subscription</c>.
/// Never confers clinical study grants or unmasks clinical records.
/// </summary>
[KernelCapability("entitlement.grant", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record GrantProductEntitlement(
    TenantId TenantId,
    ProductEntitlementId ProductEntitlementId,
    SubscriptionId SubscriptionId,
    ProductFamily Family,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProductEntitlementId>;

/// <summary>Validation and domain errors raised by commercial subscription and entitlement commands.</summary>
public static class CommercialErrors
{
    public static readonly KernelError SubscriptionRequired =
        new("K.Commercial.SubscriptionRequired", 422, "A subscription identifier is required.");

    public static readonly KernelError EntitlementRequired =
        new("K.Commercial.EntitlementRequired", 422, "A product entitlement identifier is required.");

    public static readonly KernelError TrialWindowInvalid =
        new("K.Commercial.TrialWindowInvalid", 422, "A trialing subscription requires a trial end timestamp after its start.");

    public static readonly KernelError SubscriptionUnknown =
        new("K.Commercial.SubscriptionUnknown", 404, "The subscription does not exist in this tenant.");

    public static readonly KernelError SubscriptionAlreadyProvisioned =
        new("K.Commercial.SubscriptionAlreadyProvisioned", 409, "A subscription is already provisioned for this tenant.");

    public static readonly KernelError OnboardingCallerInvalid =
        new("K.Commercial.OnboardingCallerInvalid", 403, "Self-service trial onboarding requires an accountable signed-in person.");

    public static readonly KernelError TierInvalid =
        new("K.Commercial.TierInvalid", 422, "The requested commercial tier must be Individual, Select, or Complete.");

    public static readonly KernelError IdentityProviderSyncFailed =
        new("K.Commercial.IdentityProviderSyncFailed", 502, "The trial workspace was provisioned, but updating identity provider claims failed; retry to complete synchronization.");
}

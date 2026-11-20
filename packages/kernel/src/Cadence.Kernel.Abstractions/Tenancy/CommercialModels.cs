using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Commercial packaging tiers defined in <c>docs/strategy/PRODUCT_CATALOG.md</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommercialTier>))]
public enum CommercialTier
{
    Individual,
    Select,
    Complete,
}

/// <summary>
/// Lifecycle state of a tenant's commercial <c>Subscription</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionStatus>))]
public enum SubscriptionStatus
{
    Trialing,
    Active,
    Expired,
}

/// <summary>
/// Commercial product families built over the shared Clinical Kernel (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Baseline kernel capabilities (audit trails, electronic signatures, and authorization checks) are never gated by family.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductFamily>))]
public enum ProductFamily
{
    Blueprint,
    Capture,
    Orbit,
    Engage,
    Allocate,
    Vigil,
    Lens,
    Assure,
}

/// <summary>
/// Read-only commercial entitlement snapshot for a resolved kernel tenant.
/// </summary>
public sealed record TenantEntitlementSnapshot(
    TenantId TenantId,
    SubscriptionId? SubscriptionId,
    CommercialTier Tier,
    SubscriptionStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? TrialEndsAt,
    bool IsReadOnly,
    IReadOnlyList<ProductFamily> ProductFamilies);

/// <summary>
/// Reads commercial subscription and product-family entitlements for a tenant.
/// Never consulted by <c>AuthorityEvaluator</c> or <c>GrantNodeVisibility</c> to permit clinical data access.
/// </summary>
public interface ITenantEntitlementReader
{
    Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
        TenantId tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// Canonical product-family bundles per commercial tier (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// </summary>
public static class CommercialCatalog
{
    private static readonly ProductFamily[] IndividualFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
    ];

    private static readonly ProductFamily[] SelectDefaultFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
    ];

    private static readonly ProductFamily[] CompleteFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

    public static IReadOnlyList<ProductFamily> FamiliesForTier(CommercialTier tier) =>
        tier switch
        {
            CommercialTier.Individual => IndividualFamilies,
            CommercialTier.Select => SelectDefaultFamilies,
            CommercialTier.Complete => CompleteFamilies,
            _ => IndividualFamilies,
        };
}

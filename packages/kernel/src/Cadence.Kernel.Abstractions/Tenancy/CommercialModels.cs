using System;
using System.Collections.Generic;
using System.Linq;
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
/// Structured study scoping parameters and purchase-order / quote confirmation required before a commercial
/// subscription transitions to <see cref="SubscriptionStatus.Active"/> (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Live payment card processing is intentionally out of scope.
/// </summary>
public sealed record SubscriptionScoping(
    string StudyPhase,
    int StudyDurationMonths,
    IReadOnlyList<string> Countries,
    int SiteCount,
    int ParticipantCount,
    int ActiveStudyCount,
    string BillingConfirmation);

/// <summary>
/// Validates and normalizes <see cref="SubscriptionScoping"/> inputs, returning RFC 9457 field-keyed validation messages.
/// </summary>
public static class SubscriptionScopingValidator
{
    public const int MaxStudyDurationMonths = 240;
    public const int MaxSiteCount = 10_000;
    public const int MaxParticipantCount = 1_000_000;
    public const int MaxActiveStudyCount = 1_000;

    public static IReadOnlyDictionary<string, string[]> Validate(
        string? studyPhase,
        int? studyDurationMonths,
        IReadOnlyList<string>? countries,
        int? siteCount,
        int? participantCount,
        int? activeStudyCount,
        string? billingConfirmation)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        string trimmedPhase = (studyPhase ?? string.Empty).Trim();
        if (trimmedPhase.Length == 0)
        {
            errors["study_phase"] = ["Study phase is required."];
        }
        else if (trimmedPhase.Length > 64)
        {
            errors["study_phase"] = ["Study phase must be 64 characters or fewer."];
        }

        if (studyDurationMonths is not { } duration || duration <= 0 || duration > MaxStudyDurationMonths)
        {
            errors["study_duration_months"] = [$"Study duration in months must be between 1 and {MaxStudyDurationMonths}."];
        }

        var normalizedCountries = NormalizeCountries(countries);
        if (normalizedCountries.Length == 0)
        {
            errors["countries"] = ["At least one participating country is required."];
        }
        else if (normalizedCountries.Length > 250 || normalizedCountries.Any(c => c.Length > 64))
        {
            errors["countries"] = ["Countries must contain at most 250 entries of 64 characters or fewer."];
        }

        if (siteCount is not { } sites || sites <= 0 || sites > MaxSiteCount)
        {
            errors["site_count"] = [$"Planned site count must be between 1 and {MaxSiteCount:N0}."];
        }

        if (participantCount is not { } participants || participants <= 0 || participants > MaxParticipantCount)
        {
            errors["participant_count"] = [$"Planned participant count must be between 1 and {MaxParticipantCount:N0}."];
        }

        if (activeStudyCount is not { } studies || studies <= 0 || studies > MaxActiveStudyCount)
        {
            errors["active_study_count"] = [$"Concurrent active study count must be between 1 and {MaxActiveStudyCount:N0}."];
        }

        string trimmedBilling = (billingConfirmation ?? string.Empty).Trim();
        if (trimmedBilling.Length < 3)
        {
            errors["billing_confirmation"] = ["A purchase order or signed quote reference of at least 3 characters is required."];
        }
        else if (trimmedBilling.Length > 128)
        {
            errors["billing_confirmation"] = ["Billing confirmation reference must be 128 characters or fewer."];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(SubscriptionScoping? scoping) =>
        scoping is null
            ? Validate(null, null, null, null, null, null, null)
            : Validate(
                scoping.StudyPhase,
                scoping.StudyDurationMonths,
                scoping.Countries,
                scoping.SiteCount,
                scoping.ParticipantCount,
                scoping.ActiveStudyCount,
                scoping.BillingConfirmation);

    public static SubscriptionScoping Normalize(SubscriptionScoping scoping)
    {
        ArgumentNullException.ThrowIfNull(scoping);
        return new SubscriptionScoping(
            scoping.StudyPhase.Trim(),
            scoping.StudyDurationMonths,
            NormalizeCountries(scoping.Countries),
            scoping.SiteCount,
            scoping.ParticipantCount,
            scoping.ActiveStudyCount,
            scoping.BillingConfirmation.Trim());
    }

    public static string[] NormalizeCountries(IReadOnlyList<string>? countries) =>
        countries is null
            ? []
            : countries
                .SelectMany(entry => (entry ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
    IReadOnlyList<ProductFamily> ProductFamilies,
    SubscriptionScoping? Scoping = null);

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

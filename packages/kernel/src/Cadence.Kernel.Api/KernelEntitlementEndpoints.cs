using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Commercial subscription and product-family entitlement endpoints (<c>/api/v1/kernel/entitlements</c>).
/// Commercial entitlements control workspace packaging and trial mutation boundaries; they never grant clinical
/// data access or bypass study scope, blinding, or consent, and baseline regulated capabilities (audit trails,
/// electronic signatures, authorization, and record export) are never gated behind paid add-ons.
/// </summary>
public static class KernelEntitlementEndpoints
{
    /// <summary>Header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Notice displayed across pre-release workspaces (<c>docs/strategy/PRODUCT_CATALOG.md</c>).</summary>
    public const string PreReleaseEnvironmentNotice =
        "Pre-release unvalidated environment — not qualified for live clinical trial conduct.";

    private const string DefaultTierSwitchReason =
        "Switch commercial subscription tier in place while preserving trial lifecycle and clinical records";

    private const string DefaultActivateReason =
        "Activate commercial subscription with validated study scoping and billing confirmation";

    private static readonly string[] BaselineRegulatedCapabilities =
    [
        "audit_trail",
        "electronic_signatures",
        "authorization",
        "record_export",
    ];

    /// <summary>Maps <c>/entitlements</c> on the kernel route group behind <see cref="KernelTenantFilter"/>.</summary>
    public static RouteGroupBuilder MapKernelEntitlements(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup("/entitlements").AddEndpointFilter<KernelTenantFilter>();

        group.MapGet(string.Empty, GetAsync)
            .WithName("KernelGetEntitlements")
            .WithSummary("The caller's tenant commercial subscription tier, trial state, and enabled product families.")
            .WithDescription(
                "Commercial packaging boundary: never grants clinical study access or bypasses Grant, blinding, or consent. "
                + "Audit trails, electronic signatures, authorization checks, and regulated record exports remain active regardless of tier or trial expiry.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/tier", SwitchTierAsync)
            .WithName("KernelUpgradeSubscriptionTier")
            .WithSummary("Switches the tenant's commercial evaluation tier in place while preserving trial countdown and clinical records.")
            .WithDescription(
                "Updates enabled product families on an active Trialing or Active subscription without activating billing or extending "
                + "the original 30-day trial window. Refused with K.Subscription.Expired when the trial has expired.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                CommercialErrors.TierInvalid,
                CommercialErrors.SubscriptionRequired,
                CommercialErrors.SubscriptionUnknown,
                CommercialErrors.EntitlementRequired,
            ]);

        group.MapPost("/activate", ActivateAsync)
            .AllowExpiredTenantWrite()
            .WithName("KernelActivateSubscription")
            .WithSummary("Activates a Trialing or Expired commercial subscription after validating study scoping and billing confirmation.")
            .WithDescription(
                "Requires study phase, study duration, participating countries, site count, participant count, active study count, "
                + "and a purchase order or quote reference before transitioning a Trialing or Expired workspace to Active and clearing read-only restrictions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                CommercialErrors.TierInvalid,
                CommercialErrors.ScopingInvalid,
                CommercialErrors.SubscriptionRequired,
                CommercialErrors.SubscriptionUnknown,
                CommercialErrors.EntitlementRequired,
            ]);

        return kernel;
    }

    internal static async Task<Ok<KernelEntitlementsResponse>> GetAsync(
        HttpContext context,
        ITenantEntitlementReader reader,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var snapshot = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelEntitlementsResponse.From(snapshot));
    }

    internal static async Task<Results<Ok<KernelEntitlementsResponse>, ProblemHttpResult>> SwitchTierAsync(
        KernelUpgradeSubscriptionRequest request,
        HttpContext context,
        ITenantEntitlementReader reader,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Tier)
            || !Enum.TryParse<CommercialTier>(request.Tier.Trim(), ignoreCase: true, out var tier))
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.TierInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        var current = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (current.SubscriptionId is not { } subscriptionId)
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.SubscriptionUnknown);
        }

        if (current.IsReadOnly)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
        }

        string baseKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault() is { Length: > 0 } headerKey
            ? headerKey.Trim()
            : $"entitlement-tier-switch:{tenant}:{tier}:{current.Status}";
        string reason = changeReason.Read(context) ?? DefaultTierSwitchReason;

        var switched = await sender.SendAsync(
            new SwitchTrialTier(tenant, subscriptionId, tier, $"{baseKey}:switch", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!switched.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(switched.Error!);
        }

        if (await SyncTierProductFamiliesAsync(tenant, subscriptionId, tier, current.ProductFamilies, baseKey, reason, sender, cancellationToken)
            .ConfigureAwait(false) is { } familyError)
        {
            return KernelErrorEndpoints.Problem(familyError);
        }

        var updated = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelEntitlementsResponse.From(updated));
    }

    internal static async Task<Results<Ok<KernelEntitlementsResponse>, ProblemHttpResult>> ActivateAsync(
        KernelActivateSubscriptionRequest request,
        HttpContext context,
        ITenantEntitlementReader reader,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Tier)
            || !Enum.TryParse<CommercialTier>(request.Tier.Trim(), ignoreCase: true, out var tier))
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.TierInvalid);
        }

        var validationErrors = SubscriptionScopingValidator.Validate(
            request.StudyPhase,
            request.StudyDurationMonths,
            request.Countries,
            request.SiteCount,
            request.ParticipantCount,
            request.ActiveStudyCount,
            request.BillingConfirmation);
        if (validationErrors.Count > 0)
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.ScopingInvalid with { Errors = validationErrors });
        }

        var tenant = KernelRequestTenant.Get(context);
        var current = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (current.SubscriptionId is not { } subscriptionId)
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.SubscriptionUnknown);
        }

        var scoping = SubscriptionScopingValidator.Normalize(new SubscriptionScoping(
            request.StudyPhase!,
            request.StudyDurationMonths!.Value,
            request.Countries!,
            request.SiteCount!.Value,
            request.ParticipantCount!.Value,
            request.ActiveStudyCount!.Value,
            request.BillingConfirmation!));

        string baseKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault() is { Length: > 0 } headerKey
            ? headerKey.Trim()
            : $"entitlement-activate:{tenant}:{tier}:{scoping.BillingConfirmation}";
        string reason = changeReason.Read(context) ?? DefaultActivateReason;

        var activated = await sender.SendAsync(
            new ActivateSubscription(tenant, subscriptionId, tier, scoping, $"{baseKey}:activate", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!activated.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(activated.Error!);
        }

        if (await SyncTierProductFamiliesAsync(tenant, subscriptionId, tier, current.ProductFamilies, baseKey, reason, sender, cancellationToken)
            .ConfigureAwait(false) is { } familyError)
        {
            return KernelErrorEndpoints.Problem(familyError);
        }

        var updated = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelEntitlementsResponse.From(updated));
    }

    private static async Task<KernelError?> SyncTierProductFamiliesAsync(
        TenantId tenant,
        SubscriptionId subscriptionId,
        CommercialTier tier,
        IReadOnlyList<ProductFamily> currentFamilies,
        string baseKey,
        string reason,
        IKernelCommandSender sender,
        CancellationToken cancellationToken)
    {
        var targetFamilies = CommercialCatalog.FamiliesForTier(tier);
        foreach (var family in targetFamilies)
        {
            var granted = await sender.SendAsync(
                new GrantProductEntitlement(
                    tenant,
                    DeriveEntitlementId(tenant, family),
                    subscriptionId,
                    family,
                    $"{baseKey}:family:{family}",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!granted.IsSuccess)
            {
                return granted.Error!;
            }
        }

        foreach (var enabledFamily in currentFamilies)
        {
            if (targetFamilies.Contains(enabledFamily))
            {
                continue;
            }

            var revoked = await sender.SendAsync(
                new RevokeProductEntitlement(
                    tenant,
                    subscriptionId,
                    enabledFamily,
                    $"{baseKey}:revoke:{enabledFamily}",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!revoked.IsSuccess)
            {
                return revoked.Error!;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> BaselineCapabilities => BaselineRegulatedCapabilities;

    private static ProductEntitlementId DeriveEntitlementId(TenantId tenant, ProductFamily family)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-commercial-entitlement:{tenant}:{family}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return ProductEntitlementId.FromGuid(new Guid(bytes, bigEndian: true));
    }
}

/// <summary>Body of <c>POST /entitlements/tier</c>.</summary>
/// <param name="Tier">Target commercial tier (<c>Individual</c>, <c>Select</c>, or <c>Complete</c>).</param>
public sealed record KernelUpgradeSubscriptionRequest(string Tier);

/// <summary>Body of <c>POST /entitlements/activate</c>.</summary>
/// <param name="Tier">Target commercial tier (<c>Individual</c>, <c>Select</c>, or <c>Complete</c>).</param>
/// <param name="StudyPhase">Clinical trial phase (for example <c>Phase I</c>, <c>Phase II</c>, <c>Phase III</c>, <c>Phase IV</c>, or <c>Observational</c>).</param>
/// <param name="StudyDurationMonths">Planned study duration in months.</param>
/// <param name="Countries">Participating country codes or names.</param>
/// <param name="SiteCount">Planned investigative site count.</param>
/// <param name="ParticipantCount">Planned participant enrollment count.</param>
/// <param name="ActiveStudyCount">Concurrent active study count.</param>
/// <param name="BillingConfirmation">Purchase order or signed quote reference confirming activation.</param>
public sealed record KernelActivateSubscriptionRequest(
    string Tier,
    string? StudyPhase = null,
    int? StudyDurationMonths = null,
    IReadOnlyList<string>? Countries = null,
    int? SiteCount = null,
    int? ParticipantCount = null,
    int? ActiveStudyCount = null,
    string? BillingConfirmation = null);

/// <summary>Recorded study scoping and purchase-order / quote confirmation for an activated subscription.</summary>
/// <param name="StudyPhase">Clinical trial phase.</param>
/// <param name="StudyDurationMonths">Planned study duration in months.</param>
/// <param name="Countries">Participating countries.</param>
/// <param name="SiteCount">Planned site count.</param>
/// <param name="ParticipantCount">Planned participant count.</param>
/// <param name="ActiveStudyCount">Concurrent active study count.</param>
/// <param name="BillingConfirmation">Purchase order or quote reference.</param>
public sealed record KernelSubscriptionScopingResponse(
    string StudyPhase,
    int StudyDurationMonths,
    IReadOnlyList<string> Countries,
    int SiteCount,
    int ParticipantCount,
    int ActiveStudyCount,
    string BillingConfirmation)
{
    /// <summary>Maps a <see cref="SubscriptionScoping"/> domain value to its wire representation.</summary>
    public static KernelSubscriptionScopingResponse From(SubscriptionScoping scoping)
    {
        ArgumentNullException.ThrowIfNull(scoping);
        return new KernelSubscriptionScopingResponse(
            scoping.StudyPhase,
            scoping.StudyDurationMonths,
            scoping.Countries,
            scoping.SiteCount,
            scoping.ParticipantCount,
            scoping.ActiveStudyCount,
            scoping.BillingConfirmation);
    }
}

/// <summary>Commercial subscription and product-family entitlement state of the caller's resolved kernel tenant.</summary>
/// <param name="TenantRef">The resolved kernel tenant, as a NodeRef.</param>
/// <param name="TenantId">The resolved kernel tenant UUID.</param>
/// <param name="SubscriptionRef">The tenant's commercial subscription NodeRef, or null when unprovisioned.</param>
/// <param name="Tier">Commercial tier (<c>Individual</c>, <c>Select</c>, or <c>Complete</c>).</param>
/// <param name="Status">Subscription lifecycle state (<c>Trialing</c>, <c>Active</c>, or <c>Expired</c>).</param>
/// <param name="StartedAt">When the commercial subscription began, or null when unprovisioned.</param>
/// <param name="TrialEndsAt">When the trial window ends, when applicable.</param>
/// <param name="IsReadOnly">Whether trial expiration currently places new clinical mutations into read-and-export mode.</param>
/// <param name="ProductFamilies">Enabled product families (<c>Blueprint</c>, <c>Capture</c>, <c>Orbit</c>, <c>Engage</c>, <c>Allocate</c>, <c>Vigil</c>, <c>Lens</c>, <c>Assure</c>).</param>
/// <param name="BaselineCapabilities">Regulated kernel capabilities always active regardless of tier or trial state.</param>
/// <param name="EnvironmentNotice">Pre-release validation notice required on trial workspaces.</param>
/// <param name="Scoping">Recorded study scoping and quote/PO confirmation when activated, or null during trial.</param>
public sealed record KernelEntitlementsResponse(
    string TenantRef,
    Guid TenantId,
    string? SubscriptionRef,
    string Tier,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? TrialEndsAt,
    bool IsReadOnly,
    IReadOnlyList<string> ProductFamilies,
    IReadOnlyList<string> BaselineCapabilities,
    string EnvironmentNotice,
    KernelSubscriptionScopingResponse? Scoping = null)
{
    /// <summary>Maps a <see cref="TenantEntitlementSnapshot"/> to its wire representation.</summary>
    public static KernelEntitlementsResponse From(TenantEntitlementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var families = snapshot.ProductFamilies
            .OrderBy(f => (int)f)
            .Select(f => f.ToString())
            .ToArray();

        return new KernelEntitlementsResponse(
            NodeRef.For(snapshot.TenantId).ToString(),
            snapshot.TenantId.Value,
            snapshot.SubscriptionId is { } sub ? NodeRef.For(sub).ToString() : null,
            snapshot.Tier.ToString(),
            snapshot.Status.ToString(),
            snapshot.StartedAt,
            snapshot.TrialEndsAt,
            snapshot.IsReadOnly,
            families,
            KernelEntitlementEndpoints.BaselineCapabilities,
            KernelEntitlementEndpoints.PreReleaseEnvironmentNotice,
            snapshot.Scoping is { } scoping ? KernelSubscriptionScopingResponse.From(scoping) : null);
    }
}

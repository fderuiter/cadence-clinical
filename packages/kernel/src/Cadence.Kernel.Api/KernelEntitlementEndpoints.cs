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

    private const string DefaultUpgradeReason =
        "Update commercial subscription tier and product entitlements in place";

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

        group.MapPost("/tier", UpgradeTierAsync)
            .WithName("KernelUpgradeSubscriptionTier")
            .WithSummary("Activates or upgrades the tenant's commercial tier in place while preserving all clinical and audit records.")
            .WithDescription(
                "Transitions a trial or active subscription to Individual, Select, or Complete and grants the tier's product families "
                + "through the kernel command pipeline without altering clinical Grants or existing study data.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                CommercialErrors.TierInvalid,
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

    internal static async Task<Results<Ok<KernelEntitlementsResponse>, ProblemHttpResult>> UpgradeTierAsync(
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

        string baseKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault() is { Length: > 0 } headerKey
            ? headerKey.Trim()
            : $"entitlement-upgrade:{tenant}:{tier}";
        string reason = changeReason.Read(context) ?? DefaultUpgradeReason;

        var activated = await sender.SendAsync(
            new ActivateSubscription(tenant, subscriptionId, tier, $"{baseKey}:activate", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!activated.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(activated.Error!);
        }

        foreach (var family in CommercialCatalog.FamiliesForTier(tier))
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
                return KernelErrorEndpoints.Problem(granted.Error!);
            }
        }

        var updated = await reader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelEntitlementsResponse.From(updated));
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
    string EnvironmentNotice)
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
            KernelEntitlementEndpoints.PreReleaseEnvironmentNotice);
    }
}

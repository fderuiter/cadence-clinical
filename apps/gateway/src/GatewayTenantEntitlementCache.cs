using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Gateway;

/// <summary>
/// Resolved workspace commercial posture signed onto downstream requests by the gateway.
/// </summary>
public sealed record TenantPosture(
    bool IsReadOnly,
    IReadOnlyList<string> ProductFamilies,
    TenantScopingCaps? Scoping = null);

/// <summary>
/// Raised when the gateway cannot obtain a verified workspace entitlement posture from the Clinical Kernel host, so
/// downstream requests fail closed (<c>503 Service Unavailable</c>) rather than defaulting to writable/entitled.
/// </summary>
public sealed class GatewayEntitlementUnavailableException : Exception
{
    public GatewayEntitlementUnavailableException(string message)
        : base(message)
    {
    }

    public GatewayEntitlementUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Resolves and caches a tenant's commercial posture at the gateway edge so every downstream service receives signed
/// <c>X-Tenant-Read-Only</c>, <c>X-Tenant-Product-Families</c>, and optional <c>X-Tenant-Scoping</c> assertions without
/// referencing kernel persistence directly (K-XS-2).
/// </summary>
public interface ITenantEntitlementCache
{
    /// <summary>Resolves the current workspace commercial posture for <paramref name="tenantCode"/>, failing closed on upstream error.</summary>
    ValueTask<TenantPosture> GetPostureAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when <paramref name="tenantCode"/> currently has an expired trial or subscription.</summary>
    ValueTask<bool> IsTenantReadOnlyAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Evicts any cached entitlement state for <paramref name="tenantCode"/> immediately.</summary>
    void Evict(string tenantCode);

    /// <summary>Evicts all cached tenant entitlement entries.</summary>
    void EvictAll();
}

/// <summary>
/// Queries <c>GET /api/v1/kernel/entitlements/posture</c> on the Clinical Kernel host with the dedicated
/// <c>svc:gateway-entitlements</c> service identity and caches the resolved <see cref="TenantPosture"/> per tenant for
/// 30 seconds against <see cref="IClock"/>. Fails closed with <see cref="GatewayEntitlementUnavailableException"/>
/// whenever the upstream kernel posture endpoint is unreachable, rejects the probe, or returns malformed JSON.
/// </summary>
public sealed class GatewayTenantEntitlementCache(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewaySigningOptions> signingOptions,
    IClock clock) : ITenantEntitlementCache
{
    /// <summary>Named <see cref="HttpClient"/> used to query the Clinical Kernel entitlement posture endpoint.</summary>
    public const string HttpClientName = "KernelEntitlements";

    /// <summary>Dedicated internal posture endpoint on the Clinical Kernel host.</summary>
    public const string PostureEndpointPath = "/api/v1/kernel/entitlements/posture";

    /// <summary>Maximum lifetime of a cached tenant entitlement snapshot before re-querying the kernel.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyList<string> ProbeRoles = ["service"];

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<bool> IsTenantReadOnlyAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        var posture = await GetPostureAsync(tenantCode, cancellationToken).ConfigureAwait(false);
        return posture.IsReadOnly;
    }

    public async ValueTask<TenantPosture> GetPostureAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            throw new GatewayEntitlementUnavailableException("Cannot resolve entitlement posture for an empty tenant code.");
        }

        string normalized = tenantCode.Trim();
        var now = clock.UtcNow;
        if (_entries.TryGetValue(normalized, out var cached) && now < cached.ExpiresAt)
        {
            return cached.Posture;
        }

        byte[] secretBytes = signingOptions.Value.GetSecretBytes();
        if (secretBytes.Length == 0)
        {
            throw new GatewayEntitlementUnavailableException("Gateway signing secret is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, PostureEndpointPath);
            var probeIdentity = new GatewayIdentity(
                UserId: CommercialOperators.GatewayPostureProbe.Id,
                Roles: ProbeRoles,
                SiteIds: [],
                TenantId: normalized);

            foreach (var (name, value) in probeIdentity.ToSignedHeaders(now, secretBytes))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new GatewayEntitlementUnavailableException(
                    $"Kernel posture endpoint returned HTTP {(int)response.StatusCode} for tenant '{normalized}'.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            if (!root.TryGetProperty("is_read_only", out var readOnlyElement)
                || (readOnlyElement.ValueKind != JsonValueKind.True && readOnlyElement.ValueKind != JsonValueKind.False))
            {
                throw new GatewayEntitlementUnavailableException("Kernel posture response is missing boolean 'is_read_only'.");
            }

            if (!root.TryGetProperty("product_families", out var familiesElement)
                || familiesElement.ValueKind != JsonValueKind.Array)
            {
                throw new GatewayEntitlementUnavailableException("Kernel posture response is missing array 'product_families'.");
            }

            var rawFamilies = new List<string>();
            foreach (var item in familiesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    throw new GatewayEntitlementUnavailableException("Kernel posture response contains a non-string product family.");
                }

                rawFamilies.Add(item.GetString()!);
            }

            string? canonicalFamilies = GatewayIdentity.CanonicalizeProductFamilies(rawFamilies, emitNoneWhenEmpty: true);
            if (!GatewayIdentity.TryParseProductFamiliesHeader(canonicalFamilies, out var parsedFamilies))
            {
                throw new GatewayEntitlementUnavailableException("Kernel posture response contains unknown product families.");
            }

            TenantScopingCaps? scoping = null;
            if (root.TryGetProperty("scoping", out var scopingElement)
                && scopingElement.ValueKind != JsonValueKind.Null
                && scopingElement.ValueKind != JsonValueKind.Undefined)
            {
                scoping = ParseScopingElement(scopingElement);
            }

            var posture = new TenantPosture(readOnlyElement.GetBoolean(), parsedFamilies, scoping);
            _entries[normalized] = new CacheEntry(posture, clock.UtcNow.Add(CacheTtl));
            return posture;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GatewayEntitlementUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            throw new GatewayEntitlementUnavailableException(
                $"Failed to resolve workspace entitlement posture for tenant '{normalized}'.",
                ex);
        }
    }

    public void Evict(string tenantCode)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            _entries.TryRemove(tenantCode.Trim(), out _);
        }
    }

    public void EvictAll() => _entries.Clear();

    private static TenantScopingCaps ParseScopingElement(JsonElement scopingElement)
    {
        if (scopingElement.ValueKind != JsonValueKind.Object)
        {
            throw new GatewayEntitlementUnavailableException("Kernel posture 'scoping' field must be a JSON object.");
        }

        if (!scopingElement.TryGetProperty("study_phase", out var phaseEl) || phaseEl.ValueKind != JsonValueKind.String
            || !scopingElement.TryGetProperty("study_duration_months", out var monthsEl) || !monthsEl.TryGetInt32(out int months)
            || !scopingElement.TryGetProperty("site_count", out var sitesEl) || !sitesEl.TryGetInt32(out int sites)
            || !scopingElement.TryGetProperty("participant_count", out var participantsEl) || !participantsEl.TryGetInt32(out int participants)
            || !scopingElement.TryGetProperty("active_study_count", out var studiesEl) || !studiesEl.TryGetInt32(out int studies)
            || !scopingElement.TryGetProperty("countries", out var countriesEl) || countriesEl.ValueKind != JsonValueKind.Array)
        {
            throw new GatewayEntitlementUnavailableException("Kernel posture 'scoping' object is missing required fields.");
        }

        var countries = new List<string>();
        foreach (var c in countriesEl.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(c.GetString()))
            {
                throw new GatewayEntitlementUnavailableException("Kernel posture 'scoping.countries' contains an invalid entry.");
            }

            countries.Add(c.GetString()!);
        }

        var caps = new TenantScopingCaps(phaseEl.GetString()!, months, countries, sites, participants, studies);
        _ = caps.ToHeaderValue();
        return caps;
    }

    private readonly record struct CacheEntry(TenantPosture Posture, DateTimeOffset ExpiresAt);
}

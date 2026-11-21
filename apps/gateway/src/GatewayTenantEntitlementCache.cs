using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Gateway;

/// <summary>
/// Resolves and caches a tenant's commercial read-only state at the gateway edge so every downstream service
/// receives a signed <c>X-Tenant-Read-Only</c> assertion without referencing kernel persistence directly (K-XS-2).
/// </summary>
public interface ITenantEntitlementCache
{
    /// <summary>Returns <c>true</c> when <paramref name="tenantCode"/> currently has an expired trial or subscription.</summary>
    ValueTask<bool> IsTenantReadOnlyAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Evicts any cached entitlement state for <paramref name="tenantCode"/> immediately.</summary>
    void Evict(string tenantCode);

    /// <summary>Evicts all cached tenant entitlement entries.</summary>
    void EvictAll();
}

/// <summary>
/// Queries <c>GET /api/v1/kernel/entitlements</c> on the Clinical Kernel host with a signed gateway probe identity
/// and caches the resolved <c>is_read_only</c> boolean per tenant for 30 seconds against <see cref="IClock"/>.
/// </summary>
public sealed class GatewayTenantEntitlementCache(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewaySigningOptions> signingOptions,
    IClock clock) : ITenantEntitlementCache
{
    /// <summary>Named <see cref="HttpClient"/> used to query the Clinical Kernel entitlement read endpoint.</summary>
    public const string HttpClientName = "KernelEntitlements";

    /// <summary>Maximum lifetime of a cached tenant entitlement snapshot before re-querying the kernel.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private const string ProbeUserId = "gateway-entitlement-probe";

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<bool> IsTenantReadOnlyAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return false;
        }

        string normalized = tenantCode.Trim();
        var now = clock.UtcNow;
        if (_entries.TryGetValue(normalized, out var cached) && now < cached.ExpiresAt)
        {
            return cached.IsReadOnly;
        }

        byte[] secretBytes = signingOptions.Value.GetSecretBytes();
        if (secretBytes.Length == 0)
        {
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/kernel/entitlements");
            var probeIdentity = new GatewayIdentity(
                UserId: ProbeUserId,
                Roles: [],
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
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("is_read_only", out var readOnlyElement)
                || (readOnlyElement.ValueKind != JsonValueKind.True && readOnlyElement.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            bool isReadOnly = readOnlyElement.GetBoolean();
            _entries[normalized] = new CacheEntry(isReadOnly, clock.UtcNow.Add(CacheTtl));
            return isReadOnly;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return false;
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

    private readonly record struct CacheEntry(bool IsReadOnly, DateTimeOffset ExpiresAt);
}

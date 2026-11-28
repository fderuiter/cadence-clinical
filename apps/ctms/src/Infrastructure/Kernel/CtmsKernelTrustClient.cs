using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Ctms.Infrastructure.Kernel;

/// <summary>Bitemporal validity and recorded window of a canonical Kernel grant (<c>K-TIME-1</c>).</summary>
public sealed record CtmsKernelGrantPeriod(
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset RecordedFrom,
    DateTimeOffset? RecordedTo);

/// <summary>Projected canonical Kernel grant or delegation scoped to a clinical study site.</summary>
public sealed record CtmsKernelGrantRecord(
    Guid GrantId,
    string StudyCode,
    string SiteCode,
    string GranteeId,
    string? RoleCode,
    IReadOnlyList<string> Capabilities,
    string IssuedBy,
    bool InEffect,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    IReadOnlyList<CtmsKernelGrantPeriod> Periods);

/// <summary>
/// Port through which CTMS Delegation of Authority (DoA) reads, issues, delegates, and revokes canonical
/// bitemporal <c>Grant</c> records in <c>Cadence.Kernel</c> (ADR-2455, <c>K-AUTH-3</c>, <c>K-TIME-1</c>).
/// </summary>
public interface ICtmsKernelTrustClient
{
    Task<IReadOnlyList<CtmsKernelGrantRecord>> ListSiteGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        bool includeInactive,
        CancellationToken ct);

    Task<CtmsKernelGrantRecord> IssueOrDelegateSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        string granteeId,
        IReadOnlyList<string> taskCodes,
        DateTimeOffset validFrom,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct);

    Task<CtmsKernelGrantRecord?> RevokeSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        Guid grantId,
        string studyCode,
        string siteCode,
        DateTimeOffset effectiveAt,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct);
}

/// <summary>
/// HTTP adapter invoking <c>apps/kernel-api</c> (<c>/api/v1/kernel/grants</c> and <c>/api/v1/kernel/roles</c>)
/// with gateway-signed caller and operator identities.
/// </summary>
public sealed class HttpCtmsKernelTrustClient(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : ICtmsKernelTrustClient
{
    private static readonly JsonSerializerOptions SnakeJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly string[] DefaultProductFamilies =
    [
        nameof(ProductFamily.Blueprint),
        nameof(ProductFamily.Capture),
        nameof(ProductFamily.Orbit),
        nameof(ProductFamily.Assure),
    ];

    private static readonly string[] PreferredCoordinatorRoleCodes =
    [
        "DEMO_CRC",
        "SITE_CRC",
        "SITE_COORDINATOR",
        "CRC",
    ];

    public async Task<IReadOnlyList<CtmsKernelGrantRecord>> ListSiteGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        bool includeInactive,
        CancellationToken ct)
    {
        string effectiveTenant = ResolveTenantHeader(tenantId, tenantCode);
        if (string.IsNullOrWhiteSpace(effectiveTenant) || string.IsNullOrWhiteSpace(studyCode) || string.IsNullOrWhiteSpace(siteCode))
        {
            return [];
        }

        var query = new List<string>
        {
            $"study_code={Uri.EscapeDataString(studyCode.Trim())}",
            $"site_code={Uri.EscapeDataString(siteCode.Trim())}",
            $"include_inactive={(includeInactive ? "true" : "false")}",
        };
        if (validAt.HasValue)
        {
            query.Add($"valid_at={Uri.EscapeDataString(validAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (knownAt.HasValue)
        {
            query.Add($"known_at={Uri.EscapeDataString(knownAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/kernel/grants?{string.Join('&', query)}");
        AttachServiceHeaders(request, effectiveTenant);

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<List<WireGrantResponse>>(SnakeJson, ct).ConfigureAwait(false) ?? [];
        return wire
            .Where(g => g.GrantId != Guid.Empty && string.Equals(g.GranteeKind, "Person", StringComparison.OrdinalIgnoreCase))
            .Select(g => ToRecord(g, studyCode, siteCode))
            .ToList();
    }

    public async Task<CtmsKernelGrantRecord> IssueOrDelegateSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        string granteeId,
        IReadOnlyList<string> taskCodes,
        DateTimeOffset validFrom,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        string effectiveTenant = ResolveTenantHeader(tenantId, tenantCode);
        var (capability, roleCode) = await ResolveTargetGrantBindingAsync(effectiveTenant, taskCodes, ct).ConfigureAwait(false);
        var delegableParent = await FindDelegableParentGrantAsync(effectiveTenant, studyCode, siteCode, actorSubject, capability, roleCode, ct).ConfigureAwait(false);

        HttpRequestMessage request;
        if (delegableParent is not null)
        {
            request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/kernel/grants/{delegableParent.GrantId:D}/delegate")
            {
                Content = JsonContent.Create(new
                {
                    granteeId,
                    granteeKind = "Person",
                    capability,
                    roleCode,
                    scopeLevel = "StudySite",
                    studyCode,
                    siteCode,
                    validFrom,
                    withGrantOption = false,
                }, options: SnakeJson),
            };
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/grants")
            {
                Content = JsonContent.Create(new
                {
                    granteeId,
                    granteeKind = "Person",
                    capability,
                    roleCode,
                    scopeLevel = "StudySite",
                    studyCode,
                    siteCode,
                    validFrom,
                    withGrantOption = false,
                }, options: SnakeJson),
            };
        }

        using (request)
        {
            AttachActorHeaders(request, effectiveTenant, actorSubject, actorRoles, studyCode, siteCode, reasonForChange, idempotencyKey);
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<WireGrantResponse>(SnakeJson, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Kernel grant mutation returned an empty payload.");
            return ToRecord(created, studyCode, siteCode);
        }
    }

    public async Task<CtmsKernelGrantRecord?> RevokeSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        Guid grantId,
        string studyCode,
        string siteCode,
        DateTimeOffset effectiveAt,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        string effectiveTenant = ResolveTenantHeader(tenantId, tenantCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/kernel/grants/{grantId:D}/revoke")
        {
            Content = JsonContent.Create(new { effectiveAt }, options: SnakeJson),
        };
        AttachActorHeaders(request, effectiveTenant, actorSubject, actorRoles, studyCode, siteCode, reasonForChange, idempotencyKey);

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<WireGrantResponse>(SnakeJson, ct).ConfigureAwait(false);
        return updated is null ? null : ToRecord(updated, studyCode, siteCode);
    }

    private static string ResolveTenantHeader(TenantId tenantId, string? tenantCode)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            return tenantCode.Trim();
        }

        return !tenantId.IsEmpty ? tenantId.ToString() : string.Empty;
    }

    private async Task<(string? Capability, string? RoleCode)> ResolveTargetGrantBindingAsync(
        string effectiveTenant,
        IReadOnlyList<string> taskCodes,
        CancellationToken ct)
    {
        if (taskCodes.Count == 1 && taskCodes[0].Contains('.', StringComparison.Ordinal))
        {
            return (taskCodes[0].Trim(), null);
        }

        using var rolesReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/kernel/roles");
        AttachServiceHeaders(rolesReq, effectiveTenant);
        using var rolesRes = await httpClient.SendAsync(rolesReq, ct).ConfigureAwait(false);
        if (rolesRes.IsSuccessStatusCode)
        {
            var roles = await rolesRes.Content.ReadFromJsonAsync<List<WireRoleResponse>>(SnakeJson, ct).ConfigureAwait(false) ?? [];
            foreach (string preferred in PreferredCoordinatorRoleCodes)
            {
                var match = roles.FirstOrDefault(r => string.Equals(r.Code, preferred, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return (null, match.Code);
                }
            }

            var captureRole = roles.FirstOrDefault(r =>
                r.Capabilities is not null
                && r.Capabilities.Contains("collection.record", StringComparer.Ordinal)
                && !r.Capabilities.Contains("grant.issue", StringComparer.Ordinal));
            if (captureRole is not null)
            {
                return (null, captureRole.Code);
            }
        }

        return ("collection.record", null);
    }

    private async Task<WireGrantResponse?> FindDelegableParentGrantAsync(
        string effectiveTenant,
        string studyCode,
        string siteCode,
        string actorSubject,
        string? capability,
        string? roleCode,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/kernel/grants?grantee_id={Uri.EscapeDataString(actorSubject)}");
        AttachServiceHeaders(req, effectiveTenant);
        using var res = await httpClient.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        var actorGrants = await res.Content.ReadFromJsonAsync<List<WireGrantResponse>>(SnakeJson, ct).ConfigureAwait(false) ?? [];
        var delegable = actorGrants
            .Where(g => g.WithGrantOption && g.InEffect && !g.Revoked && CoversSite(g, studyCode, siteCode))
            .ToList();
        if (delegable.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(capability))
        {
            return delegable.FirstOrDefault(g =>
                g.Capabilities is not null
                && g.Capabilities.Contains(capability, StringComparer.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(roleCode))
        {
            using var rolesReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/kernel/roles");
            AttachServiceHeaders(rolesReq, effectiveTenant);
            using var rolesRes = await httpClient.SendAsync(rolesReq, ct).ConfigureAwait(false);
            if (rolesRes.IsSuccessStatusCode)
            {
                var roles = await rolesRes.Content.ReadFromJsonAsync<List<WireRoleResponse>>(SnakeJson, ct).ConfigureAwait(false) ?? [];
                var targetRole = roles.FirstOrDefault(r => string.Equals(r.Code, roleCode, StringComparison.OrdinalIgnoreCase));
                if (targetRole?.Capabilities is { Count: > 0 } needed)
                {
                    return delegable.FirstOrDefault(g =>
                        g.Capabilities is not null
                        && needed.All(cap => g.Capabilities.Contains(cap, StringComparer.Ordinal)));
                }
            }
        }

        return delegable[0];
    }

    private static bool CoversSite(WireGrantResponse grant, string studyCode, string siteCode)
    {
        if (string.Equals(grant.ScopeLevel, "Tenant", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(grant.ScopeLevel, "Study", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(grant.StudyCode, studyCode, StringComparison.Ordinal);
        }

        if (string.Equals(grant.ScopeLevel, "StudySite", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(grant.StudyCode, studyCode, StringComparison.Ordinal)
                && string.Equals(grant.SiteCode, siteCode, StringComparison.Ordinal);
        }

        return false;
    }

    private void AttachServiceHeaders(HttpRequestMessage request, string effectiveTenant)
    {
        string? secret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        var identity = new GatewayIdentity(
            UserId: TrustOperators.CtmsDelegationReader.Id,
            Roles: ["service"],
            SiteIds: [],
            TenantId: effectiveTenant,
            StudyIds: ["*"],
            TenantReadOnly: false,
            ProductFamilies: DefaultProductFamilies);

        foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(secret)))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private void AttachActorHeaders(
        HttpRequestMessage request,
        string effectiveTenant,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string studyCode,
        string siteCode,
        string reasonForChange,
        string idempotencyKey)
    {
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        string? secret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reasonForChange));
            return;
        }

        var identity = new GatewayIdentity(
            UserId: actorSubject,
            Roles: actorRoles.Count > 0 ? actorRoles : ["principal_investigator"],
            SiteIds: string.IsNullOrWhiteSpace(siteCode) ? [] : [siteCode],
            TenantId: effectiveTenant,
            ChangeReason: reasonForChange,
            StudyIds: string.IsNullOrWhiteSpace(studyCode) ? ["*"] : [studyCode],
            TenantReadOnly: false,
            ProductFamilies: DefaultProductFamilies);

        foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(secret)))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static CtmsKernelGrantRecord ToRecord(WireGrantResponse g, string fallbackStudyCode, string fallbackSiteCode) =>
        new(
            GrantId: g.GrantId,
            StudyCode: string.IsNullOrWhiteSpace(g.StudyCode) ? fallbackStudyCode : g.StudyCode!,
            SiteCode: string.IsNullOrWhiteSpace(g.SiteCode) ? fallbackSiteCode : g.SiteCode!,
            GranteeId: g.GranteeId ?? string.Empty,
            RoleCode: g.RoleCode,
            Capabilities: g.Capabilities ?? [],
            IssuedBy: g.IssuedBy ?? "principal_investigator",
            InEffect: g.InEffect,
            ValidFrom: g.ValidFrom ?? DateTimeOffset.UnixEpoch,
            ValidTo: g.ValidTo,
            Revoked: g.Revoked,
            Periods: (g.Periods ?? [])
                .Select(p => new CtmsKernelGrantPeriod(p.ValidFrom, p.ValidTo, p.RecordedFrom, p.RecordedTo))
                .ToList());

    private sealed record WireRoleResponse(string? Code, IReadOnlyList<string>? Capabilities);

    private sealed record WireBitemporalPeriod(
        DateTimeOffset ValidFrom,
        DateTimeOffset? ValidTo,
        DateTimeOffset RecordedFrom,
        DateTimeOffset? RecordedTo);

    private sealed record WireGrantResponse(
        Guid GrantId,
        string? GranteeKind,
        string? GranteeId,
        string? RoleCode,
        IReadOnlyList<string>? Capabilities,
        string? ScopeLevel,
        string? StudyCode,
        string? SiteCode,
        bool WithGrantOption,
        string? IssuedBy,
        bool InEffect,
        DateTimeOffset? ValidFrom,
        DateTimeOffset? ValidTo,
        bool Revoked,
        IReadOnlyList<WireBitemporalPeriod>? Periods);
}

/// <summary>
/// Deterministic in-memory implementation of <see cref="ICtmsKernelTrustClient"/> used in isolated CTMS hosts where
/// <c>KERNEL_SERVICE_URL</c> is not configured. Preserves full <c>K-TIME-1</c> bitemporal period semantics and tenant isolation.
/// </summary>
public sealed class InMemoryCtmsKernelTrustClient(IClock clock) : ICtmsKernelTrustClient
{
    private readonly ConcurrentDictionary<(TenantId TenantId, Guid GrantId), StoredGrant> _grants = new();
    private readonly ConcurrentDictionary<(TenantId TenantId, string IdempotencyKey), Guid> _idempotency = new();
    private readonly object _gate = new();

    public Task<IReadOnlyList<CtmsKernelGrantRecord>> ListSiteGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        bool includeInactive,
        CancellationToken ct)
    {
        lock (_gate)
        {
            DateTimeOffset now = clock.UtcNow;
            DateTimeOffset effectiveValidAt = validAt ?? knownAt ?? now;
            DateTimeOffset effectiveKnownAt = knownAt ?? validAt ?? now;
            bool pointInTimeQuery = validAt.HasValue || knownAt.HasValue;

            var results = new List<CtmsKernelGrantRecord>();
            foreach (var kvp in _grants)
            {
                if (kvp.Key.TenantId != tenantId)
                {
                    continue;
                }

                var stored = kvp.Value;
                if (!string.Equals(stored.StudyCode, studyCode, StringComparison.Ordinal)
                    || !string.Equals(stored.SiteCode, siteCode, StringComparison.Ordinal))
                {
                    continue;
                }

                var knownPeriods = stored.Periods
                    .Where(p => p.RecordedFrom <= effectiveKnownAt && (p.RecordedTo is null || effectiveKnownAt < p.RecordedTo.Value))
                    .OrderBy(p => p.ValidFrom)
                    .ToList();

                if (pointInTimeQuery && knownPeriods.Count == 0)
                {
                    continue;
                }

                var activePeriod = knownPeriods.FirstOrDefault(p =>
                    p.ValidFrom <= effectiveValidAt && (p.ValidTo is null || effectiveValidAt < p.ValidTo.Value));

                bool inEffect = activePeriod is not null;
                if (!includeInactive && !inEffect)
                {
                    continue;
                }

                var displayPeriod = activePeriod ?? knownPeriods.LastOrDefault() ?? stored.Periods[^1];
                bool revokedAtKnownTime = stored.RevokedAt.HasValue && stored.RevokedAt.Value <= effectiveKnownAt;

                results.Add(new CtmsKernelGrantRecord(
                    GrantId: stored.GrantId,
                    StudyCode: stored.StudyCode,
                    SiteCode: stored.SiteCode,
                    GranteeId: stored.GranteeId,
                    RoleCode: "SITE_COORDINATOR",
                    Capabilities: stored.Capabilities,
                    IssuedBy: stored.IssuedBy,
                    InEffect: inEffect,
                    ValidFrom: displayPeriod.ValidFrom,
                    ValidTo: displayPeriod.ValidTo,
                    Revoked: revokedAtKnownTime,
                    Periods: stored.Periods.ToList()));
            }

            return Task.FromResult<IReadOnlyList<CtmsKernelGrantRecord>>(
                results.OrderBy(r => r.ValidFrom).ThenBy(r => r.GrantId).ToList());
        }
    }

    public Task<CtmsKernelGrantRecord> IssueOrDelegateSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        string siteCode,
        string granteeId,
        IReadOnlyList<string> taskCodes,
        DateTimeOffset validFrom,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_idempotency.TryGetValue((tenantId, idempotencyKey), out Guid existingId)
                && _grants.TryGetValue((tenantId, existingId), out var existing))
            {
                return Task.FromResult(ToCurrentRecord(existing, clock.UtcNow));
            }

            DateTimeOffset now = clock.UtcNow;
            Guid grantId = Guid.NewGuid();
            var initialPeriod = new CtmsKernelGrantPeriod(validFrom, null, now, null);
            var stored = new StoredGrant(
                GrantId: grantId,
                StudyCode: studyCode,
                SiteCode: siteCode,
                GranteeId: granteeId,
                Capabilities: taskCodes.Count > 0 ? taskCodes.ToArray() : ["collection.record"],
                IssuedBy: actorSubject,
                Periods: [initialPeriod],
                RevokedAt: null);

            _grants[(tenantId, grantId)] = stored;
            _idempotency[(tenantId, idempotencyKey)] = grantId;
            return Task.FromResult(ToCurrentRecord(stored, now));
        }
    }

    public Task<CtmsKernelGrantRecord?> RevokeSiteGrantAsync(
        TenantId tenantId,
        string? tenantCode,
        Guid grantId,
        string studyCode,
        string siteCode,
        DateTimeOffset effectiveAt,
        string actorSubject,
        IReadOnlyList<string> actorRoles,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_grants.TryGetValue((tenantId, grantId), out var stored))
            {
                return Task.FromResult<CtmsKernelGrantRecord?>(null);
            }

            DateTimeOffset now = clock.UtcNow;
            var updatedPeriods = new List<CtmsKernelGrantPeriod>();
            foreach (var period in stored.Periods)
            {
                if (period.RecordedTo is not null)
                {
                    updatedPeriods.Add(period);
                    continue;
                }

                updatedPeriods.Add(period with { RecordedTo = now });
                if (period.ValidFrom < effectiveAt)
                {
                    DateTimeOffset? boundedTo = period.ValidTo.HasValue && period.ValidTo.Value < effectiveAt
                        ? period.ValidTo
                        : effectiveAt;
                    updatedPeriods.Add(new CtmsKernelGrantPeriod(period.ValidFrom, boundedTo, now, null));
                }
            }

            var updated = stored with
            {
                Periods = updatedPeriods,
                RevokedAt = now,
            };
            _grants[(tenantId, grantId)] = updated;
            return Task.FromResult<CtmsKernelGrantRecord?>(ToCurrentRecord(updated, now));
        }
    }

    private static CtmsKernelGrantRecord ToCurrentRecord(StoredGrant stored, DateTimeOffset now)
    {
        var openPeriods = stored.Periods.Where(p => p.RecordedTo is null).ToList();
        var activePeriod = openPeriods.FirstOrDefault(p =>
            p.ValidFrom <= now && (p.ValidTo is null || now < p.ValidTo.Value));
        var display = activePeriod ?? openPeriods.LastOrDefault() ?? stored.Periods[^1];
        return new CtmsKernelGrantRecord(
            GrantId: stored.GrantId,
            StudyCode: stored.StudyCode,
            SiteCode: stored.SiteCode,
            GranteeId: stored.GranteeId,
            RoleCode: "SITE_COORDINATOR",
            Capabilities: stored.Capabilities,
            IssuedBy: stored.IssuedBy,
            InEffect: activePeriod is not null && !stored.RevokedAt.HasValue,
            ValidFrom: display.ValidFrom,
            ValidTo: display.ValidTo,
            Revoked: stored.RevokedAt.HasValue,
            Periods: stored.Periods.ToList());
    }

    private sealed record StoredGrant(
        Guid GrantId,
        string StudyCode,
        string SiteCode,
        string GranteeId,
        IReadOnlyList<string> Capabilities,
        string IssuedBy,
        IReadOnlyList<CtmsKernelGrantPeriod> Periods,
        DateTimeOffset? RevokedAt);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Queries <c>GET /api/v1/kernel/locks/in-force</c> and <c>POST /api/v1/kernel/locks/in-force/batch</c> on the
/// Clinical Kernel host with a gateway-signed identity and rejects standalone execution mutations when any covering
/// <c>Frozen</c> or <c>Locked</c> assertion is active.
/// </summary>
public sealed class HttpKernelLockGuard(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelLockGuard
{
    /// <summary>Canonical kernel error code raised when an active <c>Frozen</c> or <c>Locked</c> assertion denies writes.</summary>
    public const string ObservationLockedCode = "K.Observation.Locked";

    private const string CacheItemsPrefix = "Cadence.Execution.KernelLockGuard:";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<Result<bool>> EnsureUnlockedAsync(
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return true;
        }

        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            // Isolated non-tenant callers in unit tests have no kernel tenant claim and do not address a kernel tenant.
            return true;
        }

        string normalizedTenant = tenantId.Trim();
        string normalizedStudy = studyCode.Trim();
        string? normalizedSite = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();
        string? normalizedSubject = string.IsNullOrWhiteSpace(subjectCode) ? null : subjectCode.Trim();
        string cacheKey = $"{CacheItemsPrefix}{normalizedTenant}|{normalizedStudy}|{normalizedSite}|{normalizedSubject}";
        if (context.Items.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is Result<bool> cachedResult)
        {
            return cachedResult;
        }

        var auth = TryResolveCallerAuth(context, normalizedTenant);
        if (auth.Error is { } authError)
        {
            return authError;
        }

        var result = await QueryInForceAsync(
            auth.Identity!,
            auth.GatewaySecret!,
            normalizedStudy,
            normalizedSite,
            normalizedSubject,
            cancellationToken).ConfigureAwait(false);

        context.Items[cacheKey] = result;
        return result;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> EnsureUnlockedBatchAsync(
        string? studyCode,
        IReadOnlyCollection<string> siteCodes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return true;
        }

        if (siteCodes is null || siteCodes.Count == 0)
        {
            return await EnsureUnlockedAsync(studyCode, null, null, cancellationToken).ConfigureAwait(false);
        }

        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            return true;
        }

        string normalizedTenant = tenantId.Trim();
        string normalizedStudy = studyCode.Trim();
        string studyCacheKey = $"{CacheItemsPrefix}{normalizedTenant}|{normalizedStudy}||";
        if (context.Items.TryGetValue(studyCacheKey, out object? cachedStudyObj)
            && cachedStudyObj is Result<bool> { IsFailure: true } cachedStudyFailure)
        {
            return cachedStudyFailure;
        }

        var uncachedSites = new List<string>();
        var seenSites = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawSite in siteCodes)
        {
            if (string.IsNullOrWhiteSpace(rawSite))
            {
                continue;
            }

            string normalizedSite = rawSite.Trim();
            if (!seenSites.Add(normalizedSite))
            {
                continue;
            }

            string siteCacheKey = $"{CacheItemsPrefix}{normalizedTenant}|{normalizedStudy}|{normalizedSite}|";
            if (context.Items.TryGetValue(siteCacheKey, out object? cachedSiteObj) && cachedSiteObj is Result<bool> cachedSiteResult)
            {
                if (cachedSiteResult.IsFailure)
                {
                    return cachedSiteResult;
                }

                continue;
            }

            uncachedSites.Add(normalizedSite);
        }

        if (uncachedSites.Count == 0)
        {
            if (seenSites.Count == 0)
            {
                return await EnsureUnlockedAsync(normalizedStudy, null, null, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        string batchCacheKey = $"{CacheItemsPrefix}batch:{normalizedTenant}|{normalizedStudy}|{string.Join(',', uncachedSites.OrderBy(static s => s, StringComparer.Ordinal))}";
        if (context.Items.TryGetValue(batchCacheKey, out object? cachedBatchObj) && cachedBatchObj is Result<bool> cachedBatchResult)
        {
            return cachedBatchResult;
        }

        var auth = TryResolveCallerAuth(context, normalizedTenant);
        if (auth.Error is { } authError)
        {
            return authError;
        }

        var (result, scopeKind) = await QueryBatchInForceAsync(
            auth.Identity!,
            auth.GatewaySecret!,
            normalizedStudy,
            uncachedSites,
            cancellationToken).ConfigureAwait(false);

        context.Items[batchCacheKey] = result;
        if (result.IsSuccess)
        {
            context.Items[studyCacheKey] = result;
            foreach (string site in uncachedSites)
            {
                context.Items[$"{CacheItemsPrefix}{normalizedTenant}|{normalizedStudy}|{site}|"] = result;
            }
        }
        else if (string.Equals(scopeKind, "study", StringComparison.Ordinal))
        {
            context.Items[studyCacheKey] = result;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> EnsureUnlockedForServiceAsync(
        ServiceAccount serviceAccount,
        string? tenantCode,
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceAccount);

        if (string.IsNullOrWhiteSpace(studyCode) || string.IsNullOrWhiteSpace(tenantCode))
        {
            return true;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret
            ?? gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_MISCONFIGURED",
                "Gateway secret is required to verify Clinical Kernel lock state.");
        }

        var identity = new GatewayIdentity(
            UserId: serviceAccount.UserId,
            Roles: ["SYSTEM_SERVICE"],
            SiteIds: [],
            SponsorId: null,
            UnblindedAccess: false,
            TenantId: tenantCode.Trim(),
            ChangeReason: serviceAccount.ChangeReason,
            ProductFamilies: [nameof(ProductFamily.Capture), nameof(ProductFamily.Engage)]);

        return await QueryInForceAsync(
            identity,
            gatewaySecret,
            studyCode.Trim(),
            string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim(),
            string.IsNullOrWhiteSpace(subjectCode) ? null : subjectCode.Trim(),
            cancellationToken).ConfigureAwait(false);
    }

    private (GatewayIdentity? Identity, string? GatewaySecret, DomainError? Error) TryResolveCallerAuth(
        HttpContext context,
        string normalizedTenant)
    {
        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret
            ?? gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return (null, null, DomainError.Conflict(
                "KERNEL_LOCK_CHECK_MISCONFIGURED",
                "Gateway secret is required to verify Clinical Kernel lock state."));
        }

        var principal = context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (null, null, DomainError.Forbidden(
                "KERNEL_ACTOR_MISSING",
                "Authenticated caller identity is required to verify Clinical Kernel lock state."));
        }

        IReadOnlyList<string> forwardedFamilies = principal?.ProductFamilies is { Count: > 0 } explicitFamilies
            ? explicitFamilies
            : [nameof(ProductFamily.Capture)];

        IReadOnlyList<string> forwardedRoles = principal?.RawRoles ?? [];
        bool isVerifiedEtmfService = userId.StartsWith(ServiceAccount.IdPrefix, StringComparison.Ordinal)
            && userId.AsSpan(ServiceAccount.IdPrefix.Length).Trim().Length > 0
            && (forwardedRoles.Any(r => string.Equals(r, PermissionMatrix.EtmfServiceRole, StringComparison.OrdinalIgnoreCase))
                || context.User.IsInRole(PermissionMatrix.EtmfServiceRole));
        if (isVerifiedEtmfService && !forwardedRoles.Any(r => string.Equals(r, "SYSTEM_SERVICE", StringComparison.OrdinalIgnoreCase)))
        {
            forwardedRoles = [.. forwardedRoles, "SYSTEM_SERVICE"];
        }

        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: forwardedRoles,
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: normalizedTenant,
            ChangeReason: null,
            StudyIds: principal?.AssignedStudies,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: forwardedFamilies,
            TenantScoping: principal?.TenantScoping);

        return (identity, gatewaySecret, null);
    }

    private async Task<Result<bool>> QueryInForceAsync(
        GatewayIdentity identity,
        string gatewaySecret,
        string studyCode,
        string? siteCode,
        string? subjectCode,
        CancellationToken cancellationToken)
    {
        var pathBuilder = new StringBuilder("/api/v1/kernel/locks/in-force?study_code=")
            .Append(Uri.EscapeDataString(studyCode));
        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            pathBuilder.Append("&site_code=").Append(Uri.EscapeDataString(siteCode));
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            pathBuilder.Append("&subject_code=").Append(Uri.EscapeDataString(subjectCode));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, pathBuilder.ToString());
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        var (result, _) = await SendAndReadLockResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<(Result<bool> Result, string? ScopeKind)> QueryBatchInForceAsync(
        GatewayIdentity identity,
        string gatewaySecret,
        string studyCode,
        IReadOnlyList<string> siteCodes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/locks/in-force/batch")
        {
            Content = JsonContent.Create(
                new KernelBatchScopeWriteLockPayload(studyCode, siteCodes),
                options: WireJson),
        };
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        return await SendAndReadLockResponseAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Result<bool> Result, string? ScopeKind)> SendAndReadLockResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (DomainError.Conflict(
                "KERNEL_LOCK_CHECK_UNAVAILABLE",
                $"Could not reach the Clinical Kernel to verify lock state: {ex.Message}"), null);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var check = await response.Content
                    .ReadFromJsonAsync<KernelScopeWriteLockPayload>(WireJson, cancellationToken)
                    .ConfigureAwait(false);
                if (check is { Locked: true })
                {
                    string lockKind = string.IsNullOrWhiteSpace(check.LockKind) ? "Locked" : check.LockKind;
                    string scopeKind = string.IsNullOrWhiteSpace(check.ScopeKind) ? "study" : check.ScopeKind;
                    return (DomainError.Conflict(
                        ObservationLockedCode,
                        $"Scope is frozen or locked in the Clinical Kernel ({lockKind} on {scopeKind}); mutations are denied."), scopeKind);
                }

                return (true, null);
            }

            return (await ReadProblemErrorAsync(response, cancellationToken).ConfigureAwait(false), null);
        }
    }

    private static async Task<DomainError> ReadProblemErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await TryReadProblemPayloadAsync(response, cancellationToken).ConfigureAwait(false);
        string code = string.IsNullOrWhiteSpace(problem?.Code) ? "KERNEL_LOCK_CHECK_FAILED" : problem.Code;
        string detail;
        if (!string.IsNullOrWhiteSpace(problem?.Detail))
        {
            detail = problem.Detail;
        }
        else if (!string.IsNullOrWhiteSpace(problem?.Title))
        {
            detail = problem.Title;
        }
        else
        {
            detail = $"Clinical Kernel lock query failed with HTTP {(int)response.StatusCode}.";
        }
        return (int)response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => DomainError.Forbidden(code, detail),
            StatusCodes.Status404NotFound => DomainError.NotFound(code, detail),
            _ => DomainError.Conflict(code, detail),
        };
    }

    private static async Task<KernelProblemPayload?> TryReadProblemPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<KernelProblemPayload>(WireJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record KernelProblemPayload(string? Code, string? Detail, string? Title);

    private sealed record KernelBatchScopeWriteLockPayload(
        string StudyCode,
        IReadOnlyList<string> SiteCodes);

    private sealed record KernelScopeWriteLockPayload(
        bool StudyExists,
        bool Locked,
        string? LockKind,
        string? ScopeKind);
}

/// <summary>
/// No-op <see cref="IKernelLockGuard"/> registered only when <c>KERNEL_SERVICE_URL</c> is not configured (such as
/// isolated in-process unit tests without a Clinical Kernel host).
/// </summary>
public sealed class NoOpKernelLockGuard : IKernelLockGuard
{
    /// <summary>Shared stateless singleton instance.</summary>
    public static readonly NoOpKernelLockGuard Instance = new();

    /// <inheritdoc />
    public Task<Result<bool>> EnsureUnlockedAsync(
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));

    /// <inheritdoc />
    public Task<Result<bool>> EnsureUnlockedBatchAsync(
        string? studyCode,
        IReadOnlyCollection<string> siteCodes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));

    /// <inheritdoc />
    public Task<Result<bool>> EnsureUnlockedForServiceAsync(
        ServiceAccount serviceAccount,
        string? tenantCode,
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));
}

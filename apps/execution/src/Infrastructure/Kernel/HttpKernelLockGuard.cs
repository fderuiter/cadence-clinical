using System;
using System.Collections.Generic;
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
/// Queries <c>GET /api/v1/kernel/locks/in-force</c> on the Clinical Kernel host with a gateway-signed identity and
/// rejects standalone execution mutations when any covering <c>Frozen</c> or <c>Locked</c> assertion is active.
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

        string normalizedStudy = studyCode.Trim();
        string? normalizedSite = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();
        string? normalizedSubject = string.IsNullOrWhiteSpace(subjectCode) ? null : subjectCode.Trim();
        string cacheKey = $"{CacheItemsPrefix}{tenantId.Trim()}|{normalizedStudy}|{normalizedSite}|{normalizedSubject}";
        if (context.Items.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is Result<bool> cachedResult)
        {
            return cachedResult;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret
            ?? gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_MISCONFIGURED",
                "Gateway secret is required to verify Clinical Kernel lock state.");
        }

        var principal = context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden(
                "KERNEL_ACTOR_MISSING",
                "Authenticated caller identity is required to verify Clinical Kernel lock state.");
        }

        IReadOnlyList<string> forwardedFamilies = principal?.ProductFamilies is { Count: > 0 } explicitFamilies
            ? explicitFamilies
            : [nameof(ProductFamily.Capture)];

        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: principal?.RawRoles ?? [],
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: tenantId.Trim(),
            ChangeReason: null,
            StudyIds: principal?.AssignedStudies,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: forwardedFamilies,
            TenantScoping: principal?.TenantScoping);

        var result = await QueryInForceAsync(
            identity,
            gatewaySecret,
            normalizedStudy,
            normalizedSite,
            normalizedSubject,
            cancellationToken).ConfigureAwait(false);

        context.Items[cacheKey] = result;
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

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_UNAVAILABLE",
                $"Could not reach the Clinical Kernel to verify lock state: {ex.Message}");
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
                    return DomainError.Conflict(
                        ObservationLockedCode,
                        $"Scope is frozen or locked in the Clinical Kernel ({lockKind} on {scopeKind}); mutations are denied.");
                }

                return true;
            }

            return await ReadProblemErrorAsync(response, cancellationToken).ConfigureAwait(false);
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
    public Task<Result<bool>> EnsureUnlockedForServiceAsync(
        ServiceAccount serviceAccount,
        string? tenantCode,
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));
}

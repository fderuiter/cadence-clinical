using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Calls <c>GET /api/v1/kernel/locks/check</c> on the Clinical Kernel host with the ambient caller's
/// gateway-signed identity, failing closed when the kernel is unreachable, misconfigured, or returns an error.
/// </summary>
public sealed class HttpKernelLockGuard(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelLockGuard
{
    /// <inheritdoc />
    public async Task<DomainError?> CheckWriteAllowedAsync(
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return null;
        }

        var context = httpContextAccessor.HttpContext;
        var principal = context?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string? tenantId = principal?.TenantId
            ?? context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            // Requests without a kernel tenant claim (such as legacy non-tenant callers in isolated service tests)
            // cannot address a kernel tenant; tenant-scoped requests always enforce kernel lock checks.
            return null;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret
            ?? gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_MISCONFIGURED",
                "Gateway secret is required to verify Clinical Kernel lock posture.");
        }

        if (principal is { TenantReadOnly: true })
        {
            return DomainError.Forbidden(KernelErrors.SubscriptionExpired.Code, KernelErrors.SubscriptionExpired.Message);
        }

        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden(
                "KERNEL_ACTOR_MISSING",
                "Authenticated caller identity is required to verify Clinical Kernel lock posture.");
        }

        IReadOnlyList<string> forwardedFamilies;
        if (principal?.TenantId is not null)
        {
            forwardedFamilies = principal.ProductFamilies;
        }
        else if (principal?.ProductFamilies is { Count: > 0 } explicitFamilies)
        {
            forwardedFamilies = explicitFamilies;
        }
        else
        {
            forwardedFamilies = [nameof(ProductFamily.Capture)];
        }

        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: principal?.RawRoles ?? [],
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: tenantId,
            ChangeReason: null,
            StudyIds: principal?.AssignedStudies,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: forwardedFamilies,
            TenantScoping: principal?.TenantScoping);

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildCheckUri(studyCode, siteCode, subjectCode));
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
                $"Could not reach the Clinical Kernel to verify lock posture: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_UNAVAILABLE",
                $"Clinical Kernel HTTP client is misconfigured: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return DomainError.Conflict(
                "KERNEL_LOCK_CHECK_UNAVAILABLE",
                $"Timed out reaching the Clinical Kernel to verify lock posture: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            return await ReadProblemErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildCheckUri(string studyCode, string? siteCode, string? subjectCode)
    {
        var sb = new StringBuilder("/api/v1/kernel/locks/check?study_code=");
        sb.Append(Uri.EscapeDataString(studyCode.Trim()));

        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            sb.Append("&site_code=");
            sb.Append(Uri.EscapeDataString(siteCode.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            sb.Append("&subject_code=");
            sb.Append(Uri.EscapeDataString(subjectCode.Trim()));
        }

        return sb.ToString();
    }

    private static async Task<DomainError> ReadProblemErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? parsedCode = null;
        string detail = response.StatusCode == HttpStatusCode.Locked
            ? "Clinical scope is locked in the Clinical Kernel."
            : $"Clinical Kernel lock check failed ({(int)response.StatusCode}).";

        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("code", out var codeProp)
                    && codeProp.GetString() is { Length: > 0 } codeValue)
                {
                    parsedCode = codeValue;
                }

                if (doc.RootElement.TryGetProperty("detail", out var detailProp)
                    && detailProp.GetString() is { Length: > 0 } detailValue)
                {
                    detail = detailValue;
                }
                else if (doc.RootElement.TryGetProperty("title", out var titleProp)
                    && titleProp.GetString() is { Length: > 0 } titleValue)
                {
                    detail = titleValue;
                }
            }
            catch (JsonException)
            {
                // Keep default problem detail when upstream body is not JSON.
            }
        }

        if (response.StatusCode == HttpStatusCode.Locked)
        {
            string lockCode = parsedCode switch
            {
                "K.Observation.Locked" or "K.Assertion.Locked" or "KERNEL_SCOPE_LOCKED" => parsedCode,
                null => "K.Observation.Locked",
                _ => "KERNEL_SCOPE_LOCKED",
            };
            return DomainError.Conflict(lockCode, detail);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return DomainError.Forbidden(parsedCode ?? "KERNEL_LOCK_CHECK_FORBIDDEN", detail);
        }

        return DomainError.Conflict(parsedCode ?? "KERNEL_LOCK_CHECK_FAILED", detail);
    }
}

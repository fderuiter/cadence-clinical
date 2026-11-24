using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
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
/// Calls <c>POST /api/v1/kernel/enrollments/screen</c> on the Clinical Kernel host with the ambient caller's
/// gateway-signed identity, <c>X-Change-Reason</c>, and deterministic <c>Idempotency-Key</c>.
/// </summary>
public sealed class HttpKernelEnrollmentSynchronizer(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelEnrollmentSynchronizer
{
    /// <summary>Configuration key naming the Clinical Kernel base URL.</summary>
    public const string ConfigurationKey = "KERNEL_SERVICE_URL";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<Result<Guid>> SynchronizeScreeningEnrollmentAsync(
        Guid participantId,
        string studyCode,
        string siteCode,
        string subjectCode,
        DateTimeOffset screenedAt,
        string changeReason,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            // Requests without a kernel tenant claim (such as legacy non-tenant callers in isolated service tests) cannot
            // address a kernel tenant; only tenant-scoped callers synchronize into the kernel.
            return Guid.Empty;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret
            ?? gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel enrollments.");
        }

        var principal = context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        if (principal is { TenantReadOnly: true })
        {
            return DomainError.Forbidden(KernelErrors.SubscriptionExpired.Code, KernelErrors.SubscriptionExpired.Message);
        }

        if (principal?.TenantId is not null && !principal.HasProductFamily(ProductFamily.Capture))
        {
            return DomainError.Forbidden(
                CommercialErrors.ProductFamilyNotEntitled.Code,
                CommercialErrors.ProductFamilyNotEntitled.Message);
        }

        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden("KERNEL_ACTOR_MISSING", "Authenticated caller identity is required to synchronize kernel enrollments.");
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
            ChangeReason: changeReason,
            StudyIds: principal?.AssignedStudies,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: forwardedFamilies,
            TenantScoping: principal?.TenantScoping);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/enrollments/screen")
        {
            Content = JsonContent.Create(
                new KernelScreenRequestPayload(participantId, null, studyCode, siteCode, subjectCode, screenedAt),
                options: WireJson),
        };
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"execution-subject-screen:{studyCode}:{subjectCode}");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DomainError.Conflict("KERNEL_SYNC_UNAVAILABLE", $"Could not reach the Clinical Kernel to synchronize enrollment: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<KernelScreenResponsePayload>(WireJson, cancellationToken).ConfigureAwait(false);
                return created?.EnrollmentId ?? Guid.Empty;
            }

            return await ReadProblemErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<DomainError> ReadProblemErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string code = "KERNEL_ENROLLMENT_SYNC_FAILED";
        string detail = $"Clinical Kernel refused screening enrollment ({(int)response.StatusCode}).";

        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("code", out var codeProp) && codeProp.GetString() is { Length: > 0 } parsedCode)
                {
                    code = parsedCode;
                }

                if (doc.RootElement.TryGetProperty("detail", out var detailProp) && detailProp.GetString() is { Length: > 0 } parsedDetail)
                {
                    detail = parsedDetail;
                }
                else if (doc.RootElement.TryGetProperty("title", out var titleProp) && titleProp.GetString() is { Length: > 0 } parsedTitle)
                {
                    detail = parsedTitle;
                }
            }
            catch (JsonException)
            {
                // Keep default problem detail when upstream body is not JSON.
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => DomainError.Forbidden(code, detail),
            HttpStatusCode.NotFound => DomainError.NotFound(code, detail),
            HttpStatusCode.Locked => DomainError.Conflict(
                code is "K.Observation.Locked" or "K.Assertion.Locked" or "KERNEL_SCOPE_LOCKED"
                    ? code
                    : "K.Observation.Locked",
                detail),
            HttpStatusCode.Conflict => DomainError.Conflict(code, detail),
            _ => DomainError.Validation(code, detail),
        };
    }

    private sealed record KernelScreenRequestPayload(
        Guid ParticipantId,
        Guid? EnrollmentId,
        string StudyCode,
        string SiteCode,
        string SubjectCode,
        DateTimeOffset ScreenedAt);

    private sealed record KernelScreenResponsePayload(Guid EnrollmentId);
}

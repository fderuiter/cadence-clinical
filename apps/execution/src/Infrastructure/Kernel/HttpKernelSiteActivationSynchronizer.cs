using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Calls <c>POST /api/v1/kernel/study-sites/activate-by-code</c> on the Clinical Kernel host with the ambient
/// caller's gateway-signed identity, <c>X-Change-Reason</c>, and deterministic <c>Idempotency-Key</c>.
/// </summary>
public sealed class HttpKernelSiteActivationSynchronizer(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelSiteActivationSynchronizer
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<Result<bool>> SynchronizeSiteActivationAsync(
        string studyCode,
        string siteCode,
        string changeReason,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            // Requests without a kernel tenant claim (such as legacy non-tenant callers in isolated service tests)
            // cannot address a kernel tenant; only tenant-scoped callers synchronize into the kernel.
            return true;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel site activation.");
        }

        var principal = context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden("KERNEL_ACTOR_MISSING", "Authenticated caller identity is required to synchronize kernel site activation.");
        }

        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: principal?.RawRoles ?? [],
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: tenantId,
            ChangeReason: changeReason,
            StudyIds: principal?.AssignedStudies);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/study-sites/activate-by-code")
        {
            Content = JsonContent.Create(
                new KernelActivateStudySitePayload(studyCode, siteCode),
                options: WireJson),
        };
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"execution-site-activate:{studyCode}:{siteCode}");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DomainError.Conflict("KERNEL_SYNC_UNAVAILABLE", $"Could not reach the Clinical Kernel to synchronize site activation: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            return await HttpKernelEnrollmentSynchronizer.ReadProblemErrorAsync(
                response,
                "KERNEL_SITE_ACTIVATION_SYNC_FAILED",
                "site activation",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record KernelActivateStudySitePayload(string StudyCode, string SiteCode);
}

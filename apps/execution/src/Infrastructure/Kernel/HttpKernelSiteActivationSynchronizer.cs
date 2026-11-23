using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Calls <c>POST /api/v1/kernel/study-sites/activate</c> on the Clinical Kernel host with the ambient caller's
/// gateway-signed identity, <c>X-Change-Reason</c>, and deterministic <c>Idempotency-Key</c> (PRD-SSU-003).
/// </summary>
public sealed class HttpKernelSiteActivationSynchronizer(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelSiteActivationSynchronizer
{
    private static readonly ProductFamily[] RequiredFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
    ];

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
            return true;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel site activation.");
        }

        var identityResult = KernelSyncTransport.ResolveAmbientIdentity(
            context,
            tenantId,
            changeReason,
            "synchronize kernel site activation",
            RequiredFamilies,
            nameof(ProductFamily.Orbit));
        if (!identityResult.IsSuccess)
        {
            return identityResult.Error!;
        }

        return await KernelSyncTransport.PostSignedCommandAsync(
            httpClient,
            "/api/v1/kernel/study-sites/activate",
            new KernelActivateSitePayload(studyCode, siteCode),
            identityResult.Value!,
            clock.UtcNow,
            gatewaySecret,
            $"execution-site-activate:{studyCode}:{siteCode}",
            "KERNEL_SITE_ACTIVATION_SYNC_FAILED",
            "study site activation",
            "synchronize site activation",
            cancellationToken).ConfigureAwait(false);
    }

    private sealed record KernelActivateSitePayload(string StudyCode, string SiteCode);
}

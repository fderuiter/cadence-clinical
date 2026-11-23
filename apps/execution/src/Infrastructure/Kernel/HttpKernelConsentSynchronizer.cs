using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Calls <c>POST /api/v1/kernel/consents/sync</c> on the Clinical Kernel host with either the ambient HTTP caller's
/// gateway-signed identity or <see cref="ServiceAccounts.EConsentConsumer"/> for background eConsent completions
/// (PRD-SUB-007).
/// </summary>
public sealed class HttpKernelConsentSynchronizer(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelConsentSynchronizer
{
    private static readonly ProductFamily[] RequiredFamilies = [ProductFamily.Capture, ProductFamily.Engage];
    private static readonly string[] ConsumerProductFamilies = [nameof(ProductFamily.Capture), nameof(ProductFamily.Engage)];

    /// <inheritdoc />
    public async Task<Result<bool>> SynchronizeConsentAsync(
        string studyCode,
        string? siteCode,
        string subjectCode,
        int versionIndex,
        string? versionTag,
        DateTimeOffset signedAt,
        bool isPaperOverride,
        string changeReason,
        CancellationToken cancellationToken,
        string? explicitTenantCode = null)
    {
        var context = httpContextAccessor.HttpContext;
        string? tenantId = explicitTenantCode ?? context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return true;
        }

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel consent.");
        }

        GatewayIdentity identity;
        if (context is not null)
        {
            var identityResult = KernelSyncTransport.ResolveAmbientIdentity(
                context,
                tenantId,
                changeReason,
                "synchronize kernel consent",
                RequiredFamilies,
                nameof(ProductFamily.Capture));
            if (!identityResult.IsSuccess)
            {
                return identityResult.Error!;
            }

            identity = identityResult.Value!;
        }
        else
        {
            var account = ServiceAccounts.EConsentConsumer;
            identity = new GatewayIdentity(
                UserId: account.UserId,
                Roles: [],
                SiteIds: [],
                TenantId: tenantId,
                ChangeReason: changeReason,
                ProductFamilies: ConsumerProductFamilies);
        }

        return await KernelSyncTransport.PostSignedCommandAsync(
            httpClient,
            "/api/v1/kernel/consents/sync",
            new KernelSyncConsentPayload(studyCode, siteCode, subjectCode, null, versionIndex, versionTag, signedAt, isPaperOverride),
            identity,
            clock.UtcNow,
            gatewaySecret,
            $"execution-consent-sync:{studyCode}:{subjectCode}:v{versionIndex}:{signedAt.ToUnixTimeMilliseconds()}",
            "KERNEL_CONSENT_SYNC_FAILED",
            "consent synchronization",
            "synchronize consent",
            cancellationToken).ConfigureAwait(false);
    }

    private sealed record KernelSyncConsentPayload(
        string StudyCode,
        string? SiteCode,
        string SubjectCode,
        string? ProtocolVersionRef,
        int VersionIndex,
        string? VersionTag,
        DateTimeOffset SignedAt,
        bool IsPaperOverride);
}

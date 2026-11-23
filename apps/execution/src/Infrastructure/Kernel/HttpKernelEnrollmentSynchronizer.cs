using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

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

    private static readonly ProductFamily[] RequiredFamilies = [ProductFamily.Capture];

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

        string? gatewaySecret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel enrollments.");
        }

        var identityResult = KernelSyncTransport.ResolveAmbientIdentity(
            context,
            tenantId,
            changeReason,
            "synchronize kernel enrollments",
            RequiredFamilies,
            nameof(ProductFamily.Capture));
        if (!identityResult.IsSuccess)
        {
            return identityResult.Error!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/enrollments/screen")
        {
            Content = JsonContent.Create(
                new KernelScreenRequestPayload(participantId, null, studyCode, siteCode, subjectCode, screenedAt),
                options: KernelSyncTransport.WireJson),
        };
        foreach (var (header, value) in identityResult.Value!.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
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
                var created = await response.Content.ReadFromJsonAsync<KernelScreenResponsePayload>(KernelSyncTransport.WireJson, cancellationToken).ConfigureAwait(false);
                return created?.EnrollmentId ?? Guid.Empty;
            }

            return await KernelSyncTransport.ReadProblemErrorAsync(
                response,
                "KERNEL_ENROLLMENT_SYNC_FAILED",
                "screening enrollment",
                cancellationToken,
                defaultToValidation: true).ConfigureAwait(false);
        }
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

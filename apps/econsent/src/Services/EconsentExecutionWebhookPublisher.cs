using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Econsent.Services;

public sealed record EconsentSignedSyncMessage(
    string SubjectPseudonym,
    string StudyCode,
    string? SiteCode,
    string ProtocolVersion,
    int VersionIndex,
    DateTimeOffset SignedAt,
    string ConsentId,
    string? TenantCode,
    string ReasonForChange);

public interface IEconsentExecutionWebhookPublisher
{
    Task PublishConsentSignedAsync(EconsentSignedSyncMessage message, CancellationToken ct);
}

public sealed class HttpEconsentExecutionWebhookPublisher(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IConfiguration configuration,
    IClock clock,
    ILogger<HttpEconsentExecutionWebhookPublisher> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IEconsentExecutionWebhookPublisher
{
    private const string ServiceActorId = "svc:econsent";
    private const string ExecutionWebhookRoute = "/api/v1/execution/webhooks/econsent";
    private static readonly IReadOnlyList<string> DefaultEngageEntitlements = [nameof(ProductFamily.Engage)];

    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task PublishConsentSignedAsync(EconsentSignedSyncMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.SubjectPseudonym) || string.IsNullOrWhiteSpace(message.StudyCode))
        {
            return;
        }

        Principal? callerPrincipal = httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        if (callerPrincipal is { TenantReadOnly: true })
        {
            logger.LogWarning(
                "Skipping Execution consent sync for study {StudyCode}: tenant subscription is read-only.",
                message.StudyCode);
            return;
        }

        if (httpClient.BaseAddress is null)
        {
            logger.LogWarning(
                "Skipping Execution consent sync for study {StudyCode}: EXECUTION_SERVICE_URL is not configured.",
                message.StudyCode);
            return;
        }

        byte[]? signingKey = GetGatewaySecretBytes();
        if (signingKey is null || signingKey.Length == 0)
        {
            logger.LogWarning(
                "Skipping Execution consent sync for study {StudyCode}: GATEWAY_SECRET is not configured.",
                message.StudyCode);
            return;
        }

        string? resolvedTenantId = !string.IsNullOrWhiteSpace(message.TenantCode)
            ? message.TenantCode
            : callerPrincipal?.TenantId;

        IReadOnlyList<string>? families = null;
        if (callerPrincipal?.ProductFamilies is { Count: > 0 } existingFamilies)
        {
            families = existingFamilies;
        }
        else if (!string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            families = DefaultEngageEntitlements;
        }

        IReadOnlyList<string> siteScope = !string.IsNullOrWhiteSpace(message.SiteCode)
            ? [message.SiteCode]
            : [];

        var serviceIdentity = new GatewayIdentity(
            UserId: ServiceActorId,
            Roles: [PermissionMatrix.EconsentServiceRole],
            SiteIds: siteScope,
            UnblindedAccess: false,
            TenantId: resolvedTenantId,
            ChangeReason: string.IsNullOrWhiteSpace(message.ReasonForChange)
                ? "Automated eConsent signature synchronization"
                : message.ReasonForChange,
            StudyIds: [message.StudyCode],
            TenantReadOnly: callerPrincipal?.TenantReadOnly ?? false,
            ProductFamilies: families,
            TenantScoping: callerPrincipal?.TenantScoping);

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["subject_code"] = message.SubjectPseudonym,
            ["study_id"] = message.StudyCode,
            ["site_id"] = string.IsNullOrWhiteSpace(message.SiteCode) ? null : message.SiteCode,
            ["version_tag"] = string.IsNullOrWhiteSpace(message.ProtocolVersion)
                ? $"v{message.VersionIndex}.0"
                : message.ProtocolVersion,
            ["version_index"] = message.VersionIndex,
            ["signed_at"] = message.SignedAt,
            ["consent_id"] = message.ConsentId,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ExecutionWebhookRoute)
        {
            Content = JsonContent.Create(body, options: WireJsonOptions),
        };

        foreach (var (name, value) in serviceIdentity.ToSignedHeaders(clock.UtcNow, signingKey))
        {
            httpRequest.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Execution eConsent sync webhook answered HTTP {StatusCode} for study {StudyCode} version {VersionIndex}.",
                    (int)response.StatusCode,
                    message.StudyCode,
                    message.VersionIndex);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Could not reach Execution eConsent sync webhook for study {StudyCode} version {VersionIndex}.",
                message.StudyCode,
                message.VersionIndex);
        }
    }

    private byte[]? GetGatewaySecretBytes()
    {
        var configured = gatewayOptions.CurrentValue.GatewaySecretBytes;
        if (configured.Length > 0)
        {
            return configured.ToArray();
        }

        string? rawSecret = configuration["GATEWAY_SECRET"];
        return string.IsNullOrEmpty(rawSecret) ? null : GatewayIdentity.EncodeSecret(rawSecret);
    }
}

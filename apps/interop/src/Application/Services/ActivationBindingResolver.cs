using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Interop.Application.Services;

/// <summary>
/// Resolves a BYOD participant's bound <c>SubjectCode</c> from Execution's 30-day activation-code ledger
/// (<c>GET /api/v1/execution/subjects/activation-codes/me</c>) when an authenticated <c>Subject</c> token
/// does not yet carry a static <c>subject_code</c> OIDC claim.
/// </summary>
public interface IActivationBindingResolver
{
    Task<string?> ResolveSubjectCodeAsync(
        string callerUserId,
        IReadOnlyList<string> callerRoles,
        string? tenantCode,
        IReadOnlyList<string> studyCodes,
        CancellationToken ct = default);
}

public sealed partial class ActivationBindingResolver(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IClock clock,
    ILogger<ActivationBindingResolver> logger) : IActivationBindingResolver
{
    public const string HttpClientName = "ExecutionActivationBindings";

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Warning,
        Message = "Activation binding lookup failed transiently: {Reason}")]
    public static partial void LogActivationBindingLookupWarning(ILogger logger, string reason);

    public async Task<string?> ResolveSubjectCodeAsync(
        string callerUserId,
        IReadOnlyList<string> callerRoles,
        string? tenantCode,
        IReadOnlyList<string> studyCodes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerUserId))
        {
            return null;
        }

        string? executionBaseUrl = configuration["EXECUTION_SERVICE_URL"];
        string? gatewaySecret = configuration["GATEWAY_SECRET"];
        if (string.IsNullOrWhiteSpace(executionBaseUrl)
            || string.IsNullOrWhiteSpace(gatewaySecret)
            || gatewaySecret.Length < GatewayAuthenticationOptions.MinimumSecretLength)
        {
            return null;
        }

        string url = $"{executionBaseUrl.TrimEnd('/')}/api/v1/execution/subjects/activation-codes/me";
        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var identity = new GatewayIdentity(
                UserId: callerUserId,
                Roles: callerRoles.Count > 0 ? callerRoles : ["Subject"],
                SiteIds: [],
                TenantId: string.IsNullOrWhiteSpace(tenantCode) ? null : tenantCode,
                StudyIds: studyCodes);

            foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string rawJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("subject_id", out var subjectEl)
                && subjectEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(subjectEl.GetString()))
            {
                return subjectEl.GetString()!.Trim();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            LogActivationBindingLookupWarning(logger, ex.Message);
        }

        return null;
    }
}

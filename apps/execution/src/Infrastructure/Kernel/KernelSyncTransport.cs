using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Shared gateway-signed HTTP transport and RFC 9457 Problem Details mapping for Execution-to-Kernel synchronizers.
/// </summary>
internal static class KernelSyncTransport
{
    public static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static Result<GatewayIdentity> ResolveAmbientIdentity(
        HttpContext context,
        string tenantId,
        string changeReason,
        string operationDescription)
    {
        var principal = context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string userId = principal?.UserId
            ?? context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden(
                "KERNEL_ACTOR_MISSING",
                $"Authenticated caller identity is required to {operationDescription}.");
        }

        return new GatewayIdentity(
            UserId: userId,
            Roles: principal?.RawRoles ?? [],
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: tenantId,
            ChangeReason: changeReason,
            StudyIds: principal?.AssignedStudies);
    }

    public static async Task<Result<bool>> PostSignedCommandAsync<TPayload>(
        HttpClient httpClient,
        string relativePath,
        TPayload payload,
        GatewayIdentity identity,
        DateTimeOffset signedAtUtc,
        string gatewaySecret,
        string idempotencyKey,
        string defaultErrorCode,
        string refusalSummary,
        string unavailableOperation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(payload, options: WireJson),
        };
        foreach (var (header, value) in identity.ToSignedHeaders(signedAtUtc, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DomainError.Conflict(
                "KERNEL_SYNC_UNAVAILABLE",
                $"Could not reach the Clinical Kernel to {unavailableOperation}: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            return await ReadProblemErrorAsync(response, defaultErrorCode, refusalSummary, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<DomainError> ReadProblemErrorAsync(
        HttpResponseMessage response,
        string defaultErrorCode,
        string refusalSummary,
        CancellationToken cancellationToken,
        bool defaultToValidation = false)
    {
        string code = defaultErrorCode;
        string detail = $"Clinical Kernel refused {refusalSummary} ({(int)response.StatusCode}).";

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
            HttpStatusCode.Conflict => DomainError.Conflict(code, detail),
            HttpStatusCode.BadRequest => DomainError.Validation(code, detail),
            _ => defaultToValidation ? DomainError.Validation(code, detail) : DomainError.Conflict(code, detail),
        };
    }
}

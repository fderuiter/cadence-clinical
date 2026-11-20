using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Ctms.Infrastructure.Clients;

public sealed record KernelDelegationBridgeOutcome(
    bool Succeeded,
    Guid PersonId,
    IReadOnlyList<Guid> GrantIds,
    int ErrorStatusCode = 0,
    string? ProblemJson = null);

public interface IKernelDelegationBridge
{
    Task<KernelDelegationBridgeOutcome> DelegateByEmailAsync(
        HttpContext httpContext,
        string siteCode,
        string staffEmail,
        string? staffName,
        IReadOnlyList<string> duties,
        string startDate,
        string? endDate,
        string reasonForChange,
        CancellationToken cancellationToken = default);

    Task<KernelDelegationBridgeOutcome> RevokeGrantsAsync(
        HttpContext httpContext,
        IReadOnlyList<Guid> grantIds,
        string reasonForChange,
        CancellationToken cancellationToken = default);
}

public sealed class KernelDelegationBridge(HttpClient httpClient) : IKernelDelegationBridge
{
    private static readonly string[] ForwardedHeaders =
    [
        "X-User-Id",
        "X-User-Roles",
        "X-Site-Id",
        "X-Study-Ids",
        "X-Sponsor-Id",
        "X-Unblinded-Access",
        "X-Tenant-Id",
        "X-Change-Reason",
        "X-Sig-Token",
        "X-Timestamp",
        "X-Gateway-Signature",
        "Idempotency-Key",
    ];

    public async Task<KernelDelegationBridgeOutcome> DelegateByEmailAsync(
        HttpContext httpContext,
        string siteCode,
        string staffEmail,
        string? staffName,
        IReadOnlyList<string> duties,
        string startDate,
        string? endDate,
        string reasonForChange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        DateTimeOffset? validFrom = DateTimeOffset.TryParse(startDate, out var parsedStart) ? parsedStart : null;
        DateTimeOffset? validTo = !string.IsNullOrWhiteSpace(endDate) && DateTimeOffset.TryParse(endDate, out var parsedEnd)
            ? parsedEnd
            : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/org/delegations", UriKind.Relative))
        {
            Content = JsonContent.Create(new KernelDelegatePayload(
                Email: staffEmail,
                FullName: staffName,
                SiteCode: siteCode,
                Duties: duties,
                ValidFrom: validFrom,
                ValidTo: validTo)),
        };
        CopyGatewayHeaders(httpContext, request, reasonForChange, null);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new KernelDelegationBridgeOutcome(
                Succeeded: false,
                PersonId: Guid.Empty,
                GrantIds: [],
                ErrorStatusCode: (int)response.StatusCode,
                ProblemJson: string.IsNullOrWhiteSpace(body)
                    ? JsonSerializer.Serialize(new { title = "Delegation refused", status = (int)response.StatusCode })
                    : body);
        }

        var parsed = JsonSerializer.Deserialize<KernelDelegateResponse>(body);
        if (parsed is null || parsed.PersonId == Guid.Empty)
        {
            return new KernelDelegationBridgeOutcome(
                Succeeded: false,
                PersonId: Guid.Empty,
                GrantIds: [],
                ErrorStatusCode: StatusCodes.Status502BadGateway,
                ProblemJson: JsonSerializer.Serialize(new { title = "Invalid kernel delegation response", status = 502 }));
        }

        return new KernelDelegationBridgeOutcome(
            Succeeded: true,
            PersonId: parsed.PersonId,
            GrantIds: parsed.GrantIds ?? []);
    }

    public async Task<KernelDelegationBridgeOutcome> RevokeGrantsAsync(
        HttpContext httpContext,
        IReadOnlyList<Guid> grantIds,
        string reasonForChange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(grantIds);

        foreach (var grantId in grantIds.Where(g => g != Guid.Empty))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri($"/api/v1/org/grants/{grantId:D}/revoke", UriKind.Relative))
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            CopyGatewayHeaders(httpContext, request, reasonForChange, $"ctms-revoke:{grantId:D}");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != StatusCodes.Status404NotFound)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new KernelDelegationBridgeOutcome(
                    Succeeded: false,
                    PersonId: Guid.Empty,
                    GrantIds: grantIds,
                    ErrorStatusCode: (int)response.StatusCode,
                    ProblemJson: body);
            }
        }

        return new KernelDelegationBridgeOutcome(Succeeded: true, PersonId: Guid.Empty, GrantIds: grantIds);
    }

    private static void CopyGatewayHeaders(HttpContext source, HttpRequestMessage target, string reason, string? fallbackIdempotencyKey)
    {
        foreach (var header in ForwardedHeaders)
        {
            if (source.Request.Headers.TryGetValue(header, out var values) && values.Count > 0)
            {
                target.Headers.TryAddWithoutValidation(header, values.ToArray());
            }
        }

        if (!target.Headers.Contains("X-Change-Reason") && !string.IsNullOrWhiteSpace(reason))
        {
            target.Headers.TryAddWithoutValidation("X-Change-Reason", reason);
        }

        if (!target.Headers.Contains("Idempotency-Key"))
        {
            target.Headers.TryAddWithoutValidation("Idempotency-Key", fallbackIdempotencyKey ?? $"ctms-doa:{Guid.NewGuid():N}");
        }
    }

    private sealed record KernelDelegatePayload(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("full_name")] string? FullName,
        [property: JsonPropertyName("site_code")] string SiteCode,
        [property: JsonPropertyName("duties")] IReadOnlyList<string> Duties,
        [property: JsonPropertyName("valid_from")] DateTimeOffset? ValidFrom,
        [property: JsonPropertyName("valid_to")] DateTimeOffset? ValidTo);

    private sealed record KernelDelegateResponse(
        [property: JsonPropertyName("person_id")] Guid PersonId,
        [property: JsonPropertyName("grant_ids")] List<Guid>? GrantIds);
}

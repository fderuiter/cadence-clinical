using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Knowledge.Application;

public sealed record CtmsDelegationItemDto(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("study_id")] string? Study,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("task_codes")] List<string>? TaskCodes,
    [property: JsonPropertyName("duties")] string? Duties,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("signed_off")] bool SignedOff,
    [property: JsonPropertyName("pi_signature_hash")] string? PiSignatureHash,
    [property: JsonPropertyName("pi_approved_at")] DateTimeOffset? PiApprovedAt,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId,
    [property: JsonPropertyName("etmf_document_id")] string? EtmfDocumentId);

public sealed record CtmsEndorseBatchRequestDto(
    [property: JsonPropertyName("study_id")] string? Study,
    [property: JsonPropertyName("record_ids")] List<string> RecordIds,
    [property: JsonPropertyName("signer_user_id")] string SignerUserId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("signature_hash")] string SignatureHash,
    [property: JsonPropertyName("approved_at")] DateTimeOffset ApprovedAt);

public sealed record CtmsEndorseBatchResponseDto(
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("signature_hash")] string SignatureHash,
    [property: JsonPropertyName("approved_at")] DateTimeOffset ApprovedAt,
    [property: JsonPropertyName("approved_count")] int ApprovedCount,
    [property: JsonPropertyName("records")] List<CtmsDelegationItemDto> Records);

public sealed record CtmsEndorseOutcome(
    bool Succeeded,
    int StatusCode,
    string? ProblemDetail,
    CtmsEndorseBatchResponseDto? Payload);

/// <summary>
/// Signed HTTP client for querying and endorsing canonical Delegation of Authority (DoA) records in CTMS.
/// </summary>
public interface ICtmsDelegationClient
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<CtmsDelegationItemDto>> ListDelegationsAsync(
        HttpContext callerContext,
        string? studyCode,
        string? siteCode,
        string? status,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        CancellationToken ct);

    Task<CtmsEndorseOutcome> EndorseSignedBatchAsync(
        HttpContext callerContext,
        CtmsEndorseBatchRequestDto request,
        CancellationToken ct);
}

public sealed class HttpCtmsDelegationClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : ICtmsDelegationClient
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveBaseUrl());

    public async Task<IReadOnlyList<CtmsDelegationItemDto>> ListDelegationsAsync(
        HttpContext callerContext,
        string? studyCode,
        string? siteCode,
        string? status,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        CancellationToken ct)
    {
        string? baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            queryParts.Add($"study_id={Uri.EscapeDataString(studyCode.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            queryParts.Add($"site_id={Uri.EscapeDataString(siteCode.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParts.Add($"status={Uri.EscapeDataString(status.Trim())}");
        }

        if (validAt.HasValue)
        {
            queryParts.Add($"valid_at={Uri.EscapeDataString(validAt.Value.ToString("O"))}");
        }

        if (knownAt.HasValue)
        {
            queryParts.Add($"known_at={Uri.EscapeDataString(knownAt.Value.ToString("O"))}");
        }

        string path = "/api/v1/ctms/doa/delegations" + (queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : string.Empty);
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return [];
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        SignRequest(req, callerContext);

        HttpClient client = httpClientFactory.CreateClient(nameof(HttpCtmsDelegationClient));
        using HttpResponseMessage response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var rows = await response.Content.ReadFromJsonAsync<List<CtmsDelegationItemDto>>(cancellationToken: ct);
        return rows ?? [];
    }

    public async Task<CtmsEndorseOutcome> EndorseSignedBatchAsync(
        HttpContext callerContext,
        CtmsEndorseBatchRequestDto request,
        CancellationToken ct)
    {
        string? baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return new CtmsEndorseOutcome(false, StatusCodes.Status503ServiceUnavailable, "CTMS_SERVICE_URL is not configured.", null);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/v1/ctms/doa/endorse-signed-batch"))
        {
            Content = JsonContent.Create(request)
        };
        SignRequest(req, callerContext);

        HttpClient client = httpClientFactory.CreateClient(nameof(HttpCtmsDelegationClient));
        using HttpResponseMessage response = await client.SendAsync(req, ct);
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<CtmsEndorseBatchResponseDto>(cancellationToken: ct);
            return new CtmsEndorseOutcome(true, (int)response.StatusCode, null, payload);
        }

        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
            detail = problem?.Detail ?? problem?.Title;
        }
        catch (JsonException)
        {
            detail = await response.Content.ReadAsStringAsync(ct);
        }

        return new CtmsEndorseOutcome(false, (int)response.StatusCode, detail, null);
    }

    private string? ResolveBaseUrl() =>
        configuration["CTMS_SERVICE_URL"] ?? configuration["Services:Ctms"];

    private void SignRequest(HttpRequestMessage request, HttpContext callerContext)
    {
        string? secret = gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = configuration["GATEWAY_SECRET"];
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("GATEWAY_SECRET is required for signed Knowledge-to-CTMS calls.");
        }

        var principal = CallerScope.Principal(callerContext);
        string? tenantId = principal?.TenantId
            ?? callerContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        IReadOnlyList<string> studyScope = principal?.AssignedStudies is { Count: > 0 } s ? s.ToArray() : ["*"];
        IReadOnlyList<string> siteScope = principal?.AssignedSites is { Count: > 0 } sites ? sites.ToArray() : [];
        IReadOnlyList<string>? productFamilies = string.IsNullOrWhiteSpace(tenantId)
            ? null
            : (principal?.ProductFamilies ?? [])
                .Concat([Cadence.Kernel.Tenancy.ProductFamily.Orbit.ToString()])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var identity = new GatewayIdentity(
            UserId: "service:knowledge",
            Roles: [PermissionMatrix.KnowledgeServiceRole],
            SiteIds: siteScope,
            UnblindedAccess: false,
            TenantId: tenantId,
            ChangeReason: "Knowledge training service delegation sync",
            StudyIds: studyScope,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: productFamilies,
            TenantScoping: principal?.TenantScoping);

        byte[] secretBytes = GatewayIdentity.EncodeSecret(secret);
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, secretBytes))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }
    }
}

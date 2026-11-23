using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms.Infrastructure.Kernel;

/// <summary>
/// Bridges CTMS site Delegation of Authority (DoA) assignments and revocations to the Clinical Kernel's
/// <c>Cadence.Modules.Org</c> endpoints (<c>POST /api/v1/org/delegations</c> and <c>POST /api/v1/org/grants/{id}/revoke</c>)
/// so email-based delegations provision OIDC accounts, seal identities in <c>IdentityVault</c>, and issue or revoke
/// bounded kernel <c>Grant</c> records (ADR-2611).
/// </summary>
public interface IKernelDelegationBridge
{
    /// <summary>True when a remote kernel endpoint is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>Issues bounded kernel child grants for a site delegation by colleague email.</summary>
    Task<KernelBridgeOutcome<KernelDelegatedStaffResult>> DelegateDutiesAsync(
        HttpContext callerContext,
        KernelDelegateDutiesInput input,
        string reasonForChange,
        CancellationToken ct);

    /// <summary>Revokes each linked kernel grant ID when a CTMS DOA record is revoked.</summary>
    Task<KernelBridgeOutcome<bool>> RevokeGrantsAsync(
        HttpContext callerContext,
        IReadOnlyList<Guid> grantIds,
        string reasonForChange,
        CancellationToken ct);
}

/// <summary>Input for delegating site duties through the kernel Org module.</summary>
public sealed record KernelDelegateDutiesInput(
    string Email,
    string? FullName,
    string SiteCode,
    string? StudyCode,
    IReadOnlyList<string> Duties,
    string StartDate,
    string? EndDate);

/// <summary>Resolved pseudonymous person ID and kernel grant IDs returned by <c>POST /api/v1/org/delegations</c>.</summary>
public sealed record KernelDelegatedStaffResult(
    Guid PersonId,
    IReadOnlyList<Guid> GrantIds,
    bool ReusedExistingAccount,
    bool WelcomeEmailSent);

/// <summary>Outcome of calling the kernel Org module, preserving RFC 9457 problem payloads verbatim on refusal.</summary>
public sealed record KernelBridgeOutcome<T>(
    bool IsSuccess,
    T? Value,
    int StatusCode,
    string ContentType,
    string RawProblemJson)
{
    public static KernelBridgeOutcome<T> Success(T value) =>
        new(true, value, StatusCodes.Status200OK, "application/json", string.Empty);

    public static KernelBridgeOutcome<T> Failure(int statusCode, string? contentType, string rawBody) =>
        new(
            false,
            default,
            statusCode,
            string.IsNullOrWhiteSpace(contentType) ? "application/problem+json" : contentType,
            rawBody);
}

/// <summary>No-op stand-in used when CTMS runs in standalone unit tests without <c>KERNEL_SERVICE_URL</c>.</summary>
public sealed class NoOpKernelDelegationBridge : IKernelDelegationBridge
{
    public bool IsEnabled => false;

    public Task<KernelBridgeOutcome<KernelDelegatedStaffResult>> DelegateDutiesAsync(
        HttpContext callerContext,
        KernelDelegateDutiesInput input,
        string reasonForChange,
        CancellationToken ct)
    {
        var deterministicId = DerivePseudonymousStaffId(input.Email);
        return Task.FromResult(KernelBridgeOutcome<KernelDelegatedStaffResult>.Success(
            new KernelDelegatedStaffResult(deterministicId, [], ReusedExistingAccount: false, WelcomeEmailSent: false)));
    }

    public Task<KernelBridgeOutcome<bool>> RevokeGrantsAsync(
        HttpContext callerContext,
        IReadOnlyList<Guid> grantIds,
        string reasonForChange,
        CancellationToken ct) =>
        Task.FromResult(KernelBridgeOutcome<bool>.Success(true));

    internal static Guid DerivePseudonymousStaffId(string email)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ctms-doa-staff:{email.Trim().ToLowerInvariant()}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

/// <summary>HTTP client bridge forwarding signed gateway identity headers to <c>Cadence.Modules.Org</c>.</summary>
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
        "X-Tenant-Read-Only",
        "X-Tenant-Product-Families",
        "X-Tenant-Scoping",
        "X-Change-Reason",
        "X-Sig-Token",
        "X-Timestamp",
        "X-Gateway-Signature",
        "X-Cadence-Identity-Payload",
        "X-Cadence-Identity-Signature",
        "Authorization",
    ];

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsEnabled => true;

    public async Task<KernelBridgeOutcome<KernelDelegatedStaffResult>> DelegateDutiesAsync(
        HttpContext callerContext,
        KernelDelegateDutiesInput input,
        string reasonForChange,
        CancellationToken ct)
    {
        DateTimeOffset? validFrom = ParseDateStartUtc(input.StartDate);
        DateTimeOffset? validTo = ParseDateEndUtc(input.EndDate);

        var payload = new OrgDelegationWireRequest(
            Email: input.Email.Trim(),
            FullName: string.IsNullOrWhiteSpace(input.FullName) ? null : input.FullName.Trim(),
            Duties: input.Duties,
            StudyCode: string.IsNullOrWhiteSpace(input.StudyCode) ? null : input.StudyCode.Trim(),
            SiteCode: input.SiteCode.Trim(),
            ValidFrom: validFrom,
            ValidTo: validTo,
            ReasonForChange: reasonForChange);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/org/delegations")
        {
            Content = JsonContent.Create(payload, options: WireJson),
        };
        CopyCallerHeaders(callerContext, request, reasonForChange, $"ctms-doa:{input.SiteCode}:{input.Email}:{string.Join(',', input.Duties)}:{input.StartDate}");

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return KernelBridgeOutcome<KernelDelegatedStaffResult>.Failure(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                raw);
        }

        var parsed = JsonSerializer.Deserialize<OrgDelegationWireResponse>(raw, WireJson);
        if (parsed is null || parsed.PersonId == Guid.Empty)
        {
            return KernelBridgeOutcome<KernelDelegatedStaffResult>.Failure(
                StatusCodes.Status502BadGateway,
                "application/problem+json",
                """{"title":"Invalid kernel delegation response","status":502}""");
        }

        return KernelBridgeOutcome<KernelDelegatedStaffResult>.Success(
            new KernelDelegatedStaffResult(
                parsed.PersonId,
                parsed.GrantIds ?? [],
                parsed.ReusedExistingAccount,
                parsed.WelcomeEmailSent));
    }

    public async Task<KernelBridgeOutcome<bool>> RevokeGrantsAsync(
        HttpContext callerContext,
        IReadOnlyList<Guid> grantIds,
        string reasonForChange,
        CancellationToken ct)
    {
        foreach (var grantId in grantIds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/org/grants/{grantId:D}/revoke")
            {
                Content = JsonContent.Create(new OrgRevokeWireRequest(reasonForChange), options: WireJson),
            };
            CopyCallerHeaders(callerContext, request, reasonForChange, $"ctms-doa-revoke:{grantId:D}");

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return KernelBridgeOutcome<bool>.Failure(
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    raw);
            }
        }

        return KernelBridgeOutcome<bool>.Success(true);
    }

    private static void CopyCallerHeaders(
        HttpContext callerContext,
        HttpRequestMessage outbound,
        string reasonForChange,
        string fallbackSeed)
    {
        foreach (string header in ForwardedHeaders)
        {
            if (callerContext.Request.Headers.TryGetValue(header, out var values) && values.Count > 0)
            {
                outbound.Headers.TryAddWithoutValidation(header, values.ToArray());
            }
        }

        if (!outbound.Headers.Contains("X-Change-Reason") && !outbound.Headers.Contains("X-Gateway-Signature"))
        {
            outbound.Headers.TryAddWithoutValidation("X-Change-Reason", reasonForChange);
        }

        string idempotencyKey = callerContext.Request.Headers.TryGetValue("Idempotency-Key", out var idem) && !string.IsNullOrWhiteSpace(idem.ToString())
            ? $"{idem}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fallbackSeed)))[..12]}"
            : $"ctms-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fallbackSeed)))[..24]}";
        outbound.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
    }

    private static DateTimeOffset? ParseDateStartUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
        }

        return DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }

    private static DateTimeOffset? ParseDateEndUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
        }

        return DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }

    private sealed record OrgDelegationWireRequest(
        string Email,
        string? FullName,
        IReadOnlyList<string> Duties,
        string? StudyCode,
        string SiteCode,
        DateTimeOffset? ValidFrom,
        DateTimeOffset? ValidTo,
        string ReasonForChange);

    private sealed record OrgDelegationWireResponse(
        Guid PersonId,
        IReadOnlyList<Guid>? GrantIds,
        bool ReusedExistingAccount,
        bool WelcomeEmailSent);

    private sealed record OrgRevokeWireRequest(string ReasonForChange);
}

/// <summary>Service registration helper for <see cref="IKernelDelegationBridge"/>.</summary>
public static class KernelDelegationBridgeExtensions
{
    public static IServiceCollection AddKernelDelegationBridge(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? rawBaseUrl = configuration["KernelApi:BaseUrl"] ?? configuration["KERNEL_SERVICE_URL"];
        if (!string.IsNullOrWhiteSpace(rawBaseUrl) && Uri.TryCreate(rawBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            services.AddHttpClient<IKernelDelegationBridge, KernelDelegationBridge>(client =>
            {
                client.BaseAddress = baseUri;
            });
        }
        else
        {
            services.AddSingleton<IKernelDelegationBridge, NoOpKernelDelegationBridge>();
        }

        return services;
    }
}

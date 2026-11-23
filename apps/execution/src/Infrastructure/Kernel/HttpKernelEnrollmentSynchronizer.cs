using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Kernel;

/// <summary>
/// Calls the Clinical Kernel enrollment endpoints (<c>/screen</c>, <c>/status</c>, <c>/withdraw</c>) with the ambient
/// caller's gateway-signed identity, <c>X-Change-Reason</c>, optional <c>X-Sig-Token</c>, and deterministic
/// <c>Idempotency-Key</c>.
/// </summary>
public sealed class HttpKernelEnrollmentSynchronizer(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKernelEnrollmentSynchronizer
{
    /// <summary>Configuration key naming the Clinical Kernel base URL.</summary>
    public const string ConfigurationKey = "KERNEL_SERVICE_URL";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<Result<Guid>> SynchronizeScreeningEnrollmentAsync(
        Guid participantId,
        string studyCode,
        string siteCode,
        string subjectCode,
        DateTimeOffset screenedAt,
        string changeReason,
        CancellationToken cancellationToken,
        string? fallbackTenantId = null,
        string? fallbackActorId = null)
    {
        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value ?? fallbackTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Requests without a kernel tenant claim (such as legacy non-tenant callers in isolated service tests) cannot
            // address a kernel tenant; only tenant-scoped callers synchronize into the kernel.
            return Guid.Empty;
        }

        var identityResult = BuildIdentity(context, tenantId, changeReason, sigToken: null, fallbackActorId);
        if (identityResult.IsFailure)
        {
            return Result<Guid>.Failure(identityResult.Error!);
        }

        using var request = CreateSignedPostRequest(
            "/api/v1/kernel/enrollments/screen",
            new KernelScreenRequestPayload(participantId, null, studyCode, siteCode, subjectCode, screenedAt),
            identityResult.Value.Identity,
            identityResult.Value.GatewaySecret,
            $"execution-subject-screen:{studyCode}:{subjectCode}");

        var responseResult = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (responseResult.IsFailure)
        {
            return Result<Guid>.Failure(responseResult.Error!);
        }

        using var response = responseResult.Value!;
        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<KernelScreenResponsePayload>(WireJson, cancellationToken).ConfigureAwait(false);
            return created?.EnrollmentId ?? Guid.Empty;
        }

        return await ReadProblemErrorAsync(response, "screening enrollment", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<KernelEnrollmentSyncResult>> SynchronizeStatusChangeAsync(
        string studyCode,
        string subjectCode,
        string targetStatus,
        DateTimeOffset effectiveAt,
        string changeReason,
        CancellationToken cancellationToken,
        string? idempotencyToken = null)
    {
        var context = httpContextAccessor.HttpContext;
        string? tenantId = context?.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (context is null || string.IsNullOrWhiteSpace(tenantId))
        {
            return new KernelEnrollmentSyncResult(Guid.Empty, targetStatus, null);
        }

        var identityResult = BuildIdentity(context, tenantId, changeReason, sigToken: null, fallbackActorId: null);
        if (identityResult.IsFailure)
        {
            return Result<KernelEnrollmentSyncResult>.Failure(identityResult.Error!);
        }

        string token = string.IsNullOrWhiteSpace(idempotencyToken)
            ? effectiveAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : idempotencyToken;

        using var request = CreateSignedPostRequest(
            "/api/v1/kernel/enrollments/status",
            new KernelStatusChangeRequestPayload(studyCode, subjectCode, targetStatus, effectiveAt),
            identityResult.Value.Identity,
            identityResult.Value.GatewaySecret,
            $"execution-subject-status:{studyCode}:{subjectCode}:{targetStatus}:{token}");

        var responseResult = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (responseResult.IsFailure)
        {
            return Result<KernelEnrollmentSyncResult>.Failure(responseResult.Error!);
        }

        using var response = responseResult.Value!;
        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<KernelEnrollmentStatePayload>(WireJson, cancellationToken).ConfigureAwait(false);
            return updated is null
                ? new KernelEnrollmentSyncResult(Guid.Empty, targetStatus, null)
                : new KernelEnrollmentSyncResult(updated.EnrollmentId, updated.EnrollmentStatus, updated.EnrollmentNumber);
        }

        return await ReadProblemErrorAsync(response, "enrollment status transition", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<KernelWithdrawalSyncResult>> SynchronizeWithdrawalAsync(
        string studyCode,
        string subjectCode,
        string withdrawalKind,
        DateTimeOffset effectiveAt,
        string changeReason,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return new KernelWithdrawalSyncResult(Guid.Empty, Guid.Empty, "Withdrawn", withdrawalKind);
        }

        string? sigToken = context.Request.Headers[GatewayHeaders.SigToken].ToString();
        if (string.IsNullOrWhiteSpace(sigToken))
        {
            return DomainError.Unauthorized(SigTokenVerifier.ReauthenticationRequired, SigTokenVerifier.ReauthenticationRequired);
        }

        string? tenantId = context.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return new KernelWithdrawalSyncResult(Guid.Empty, Guid.Empty, "Withdrawn", withdrawalKind);
        }

        var identityResult = BuildIdentity(context, tenantId, changeReason, sigToken, fallbackActorId: null);
        if (identityResult.IsFailure)
        {
            return Result<KernelWithdrawalSyncResult>.Failure(identityResult.Error!);
        }

        using var request = CreateSignedPostRequest(
            IWithdrawalSignatureVerifier.Action,
            new KernelWithdrawRequestPayload(studyCode, subjectCode, withdrawalKind, effectiveAt),
            identityResult.Value.Identity,
            identityResult.Value.GatewaySecret,
            $"execution-subject-withdraw:{studyCode}:{subjectCode}");

        var responseResult = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (responseResult.IsFailure)
        {
            return Result<KernelWithdrawalSyncResult>.Failure(responseResult.Error!);
        }

        using var response = responseResult.Value!;
        if (response.IsSuccessStatusCode)
        {
            var withdrawn = await response.Content.ReadFromJsonAsync<KernelEnrollmentStatePayload>(WireJson, cancellationToken).ConfigureAwait(false);
            return withdrawn is null
                ? new KernelWithdrawalSyncResult(Guid.Empty, Guid.Empty, "Withdrawn", withdrawalKind)
                : new KernelWithdrawalSyncResult(withdrawn.EnrollmentId, Guid.Empty, withdrawn.EnrollmentStatus, withdrawalKind);
        }

        return await ReadProblemErrorAsync(response, "participant withdrawal", cancellationToken).ConfigureAwait(false);
    }

    private Result<(GatewayIdentity Identity, string GatewaySecret)> BuildIdentity(
        HttpContext? context,
        string tenantId,
        string changeReason,
        string? sigToken,
        string? fallbackActorId)
    {
        string? gatewaySecret = gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret
            ?? gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Conflict("KERNEL_SYNC_MISCONFIGURED", "Gateway secret is required to synchronize kernel enrollments.");
        }

        var principal = context?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        if (principal is { TenantReadOnly: true })
        {
            return DomainError.Forbidden(KernelErrors.SubscriptionExpired.Code, KernelErrors.SubscriptionExpired.Message);
        }

        if (principal?.TenantId is not null && !principal.HasProductFamily(ProductFamily.Capture))
        {
            return DomainError.Forbidden(
                CommercialErrors.ProductFamilyNotEntitled.Code,
                CommercialErrors.ProductFamilyNotEntitled.Message);
        }

        string userId = principal?.UserId
            ?? context?.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? fallbackActorId
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainError.Forbidden("KERNEL_ACTOR_MISSING", "Authenticated caller identity is required to synchronize kernel enrollments.");
        }

        IReadOnlyList<string> forwardedFamilies;
        if (principal?.TenantId is not null)
        {
            forwardedFamilies = principal.ProductFamilies;
        }
        else if (principal?.ProductFamilies is { Count: > 0 } explicitFamilies)
        {
            forwardedFamilies = explicitFamilies;
        }
        else
        {
            forwardedFamilies = [nameof(ProductFamily.Capture)];
        }

        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: principal?.RawRoles ?? [],
            SiteIds: principal?.AssignedSites ?? [],
            SponsorId: principal?.SponsorId,
            UnblindedAccess: principal?.UnblindedAccess ?? false,
            TenantId: tenantId,
            ChangeReason: changeReason,
            SigToken: sigToken,
            StudyIds: principal?.AssignedStudies,
            TenantReadOnly: principal?.TenantReadOnly ?? false,
            ProductFamilies: forwardedFamilies,
            TenantScoping: principal?.TenantScoping);

        return (identity, gatewaySecret);
    }

    private HttpRequestMessage CreateSignedPostRequest<TPayload>(
        string path,
        TPayload payload,
        GatewayIdentity identity,
        string gatewaySecret,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: WireJson),
        };
        foreach (var (header, value) in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private async Task<Result<HttpResponseMessage>> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DomainError.Conflict("KERNEL_SYNC_UNAVAILABLE", $"Could not reach the Clinical Kernel to synchronize enrollment: {ex.Message}");
        }
    }

    private static async Task<DomainError> ReadProblemErrorAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        string code = "KERNEL_ENROLLMENT_SYNC_FAILED";
        string detail = $"Clinical Kernel refused {operation} ({(int)response.StatusCode}).";

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
                else if (doc.RootElement.TryGetProperty("error", out var errorProp) && errorProp.GetString() is { Length: > 0 } parsedError)
                {
                    code = parsedError;
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
            HttpStatusCode.Unauthorized => DomainError.Unauthorized(code, detail),
            HttpStatusCode.Forbidden => DomainError.Forbidden(code, detail),
            HttpStatusCode.NotFound => DomainError.NotFound(code, detail),
            HttpStatusCode.Conflict => DomainError.Conflict(code, detail),
            _ => DomainError.Validation(code, detail),
        };
    }

    private sealed record KernelScreenRequestPayload(
        Guid ParticipantId,
        Guid? EnrollmentId,
        string StudyCode,
        string SiteCode,
        string SubjectCode,
        DateTimeOffset ScreenedAt);

    private sealed record KernelScreenResponsePayload(Guid EnrollmentId);

    private sealed record KernelStatusChangeRequestPayload(
        string StudyCode,
        string SubjectCode,
        string ToStatus,
        DateTimeOffset EffectiveAt);

    private sealed record KernelWithdrawRequestPayload(
        string StudyCode,
        string SubjectCode,
        string WithdrawalKind,
        DateTimeOffset EffectiveAt);

    private sealed record KernelEnrollmentStatePayload(
        Guid EnrollmentId,
        string EnrollmentStatus,
        int? EnrollmentNumber);
}

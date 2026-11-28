using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Knowledge.Infrastructure;

public sealed record KnowledgeStaffGrantRecord(
    string UserId,
    string UserName,
    KernelSiteCode SiteId,
    string Role,
    Guid GrantId);

public sealed record KnowledgeQualificationRegistration(
    string ArtifactVersionRef,
    string QualificationRef);

public interface IKnowledgeKernelTrustClient
{
    Task<IReadOnlyList<KnowledgeStaffGrantRecord>> ListStudyStaffGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        CancellationToken ct);

    Task<KnowledgeQualificationRegistration> RegisterTrainingQualificationAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        int protocolVersionNumber,
        string qualificationCode,
        string holderId,
        string title,
        string sha256Hex,
        long sizeBytes,
        string etmfDocumentId,
        DateTimeOffset completedAt,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct);
}

public interface IEtmfTrainingIngestClient
{
    Task<string> IngestTrainingCertificateAsync(
        string? tenantCode,
        string studyCode,
        string siteCode,
        string assignmentId,
        byte[] signedPdfBytes,
        string reasonForChange,
        CancellationToken ct);
}

public sealed class HttpKnowledgeKernelTrustClient(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IKnowledgeKernelTrustClient
{
    private static readonly JsonSerializerOptions SnakeJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly string[] DefaultProductFamilies =
    [
        nameof(ProductFamily.Blueprint),
        nameof(ProductFamily.Capture),
        nameof(ProductFamily.Orbit),
        nameof(ProductFamily.Assure),
    ];

    public async Task<IReadOnlyList<KnowledgeStaffGrantRecord>> ListStudyStaffGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        CancellationToken ct)
    {
        string effectiveTenant = ResolveTenantHeader(tenantId, tenantCode);
        if (string.IsNullOrWhiteSpace(effectiveTenant) || string.IsNullOrWhiteSpace(studyCode))
        {
            return [];
        }

        string url = $"/api/v1/kernel/grants?study_code={Uri.EscapeDataString(studyCode.Trim())}&include_inactive=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AttachOperatorHeaders(request, effectiveTenant, changeReason: null, idempotencyKey: null);

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<List<WireGrantResponse>>(SnakeJson, ct).ConfigureAwait(false) ?? [];
        return wire
            .Where(g => g.GrantId != Guid.Empty
                && g.InEffect
                && !g.Revoked
                && string.Equals(g.GranteeKind, "Person", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(g.GranteeId)
                && !string.IsNullOrWhiteSpace(g.SiteCode))
            .Select(g => new KnowledgeStaffGrantRecord(
                UserId: g.GranteeId!.Trim(),
                UserName: g.GranteeId!.Trim(),
                SiteId: g.SiteCode!.Trim(),
                Role: string.IsNullOrWhiteSpace(g.RoleCode) ? "SITE_CRC" : g.RoleCode!.Trim(),
                GrantId: g.GrantId))
            .ToList();
    }

    public async Task<KnowledgeQualificationRegistration> RegisterTrainingQualificationAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        int protocolVersionNumber,
        string qualificationCode,
        string holderId,
        string title,
        string sha256Hex,
        long sizeBytes,
        string etmfDocumentId,
        DateTimeOffset completedAt,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        string effectiveTenant = ResolveTenantHeader(tenantId, tenantCode);
        string trimmedStudy = studyCode.Trim();

        using var artifactReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{Uri.EscapeDataString(trimmedStudy)}/artifacts")
        {
            Content = JsonContent.Create(new
            {
                kind = "TrainingRecord",
                title,
                contentHash = $"sha256:{sha256Hex}",
                sizeBytes,
                mediaType = "application/pdf",
                storageRef = $"etmf://05.02.04/{etmfDocumentId}",
            }, options: SnakeJson),
        };
        AttachOperatorHeaders(artifactReq, effectiveTenant, reasonForChange, $"{idempotencyKey}:artifact");
        using var artifactRes = await httpClient.SendAsync(artifactReq, ct).ConfigureAwait(false);
        artifactRes.EnsureSuccessStatusCode();
        var artifactJson = await artifactRes.Content.ReadFromJsonAsync<JsonElement>(SnakeJson, ct).ConfigureAwait(false);
        string artifactVersionRef = TryReadJsonString(artifactJson, "latest_version_ref", "version_ref", "artifact_version_ref")
            ?? (TryReadJsonString(artifactJson, "latest_version_id", "version_id", "artifact_version_id") is { } vid ? $"artifact_version:{vid}" : string.Empty);
        if (string.IsNullOrWhiteSpace(artifactVersionRef))
        {
            throw new InvalidOperationException("Kernel artifact registration returned no artifact version reference.");
        }

        using var qualReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/qualifications")
        {
            Content = JsonContent.Create(new
            {
                holderId,
                holderKind = "Person",
                kind = "Training",
                code = string.IsNullOrWhiteSpace(qualificationCode) ? "protocol_training" : qualificationCode.Trim(),
                evidenceRef = artifactVersionRef,
                studyCode = trimmedStudy,
                protocolVersionNumber = protocolVersionNumber > 0 ? protocolVersionNumber : 1,
                validFrom = completedAt,
            }, options: SnakeJson),
        };
        AttachOperatorHeaders(qualReq, effectiveTenant, reasonForChange, $"{idempotencyKey}:qualification");
        using var qualRes = await httpClient.SendAsync(qualReq, ct).ConfigureAwait(false);
        qualRes.EnsureSuccessStatusCode();
        var qual = await qualRes.Content.ReadFromJsonAsync<WireQualificationResponse>(SnakeJson, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Kernel qualification recording returned an empty payload.");
        string qualificationRef = !string.IsNullOrWhiteSpace(qual.Ref)
            ? qual.Ref!
            : $"qualification:{qual.QualificationId:D}";

        return new KnowledgeQualificationRegistration(artifactVersionRef, qualificationRef);
    }

    private static string ResolveTenantHeader(TenantId tenantId, string? tenantCode)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            return tenantCode.Trim();
        }

        return !tenantId.IsEmpty ? tenantId.ToString() : string.Empty;
    }

    private void AttachOperatorHeaders(
        HttpRequestMessage request,
        string effectiveTenant,
        string? changeReason,
        string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        string? secret = gatewayOptions.CurrentValue.GatewaySecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!string.IsNullOrWhiteSpace(changeReason))
            {
                request.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(changeReason));
            }

            return;
        }

        var identity = new GatewayIdentity(
            UserId: TrustOperators.KnowledgeTrainingOperator.Id,
            Roles: ["service"],
            SiteIds: [],
            TenantId: effectiveTenant,
            ChangeReason: changeReason,
            StudyIds: ["*"],
            TenantReadOnly: false,
            ProductFamilies: DefaultProductFamilies);

        foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(secret)))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private sealed record WireGrantResponse(
        Guid GrantId,
        string? GranteeKind,
        string? GranteeId,
        string? RoleCode,
        string? StudyCode,
        string? SiteCode,
        bool InEffect,
        bool Revoked);

    private static string? TryReadJsonString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                string? raw = prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
        }

        return null;
    }

    private sealed record WireQualificationResponse(
        string? Ref,
        Guid QualificationId);
}

public sealed class InMemoryKnowledgeKernelTrustClient : IKnowledgeKernelTrustClient
{
    private readonly ConcurrentDictionary<(TenantId TenantId, string StudyCode, string SiteCode, string UserId), KnowledgeStaffGrantRecord> _grants = new();
    private readonly ConcurrentBag<(TenantId TenantId, string StudyCode, string HolderId, string QualificationCode, KnowledgeQualificationRegistration Registration)> _qualifications = [];

    public IReadOnlyCollection<(TenantId TenantId, string StudyCode, string HolderId, string QualificationCode, KnowledgeQualificationRegistration Registration)> RecordedQualifications =>
        _qualifications.ToArray();

    public void SeedGrant(
        TenantId tenantId,
        string studyCode,
        string siteCode,
        string userId,
        string? userName = null,
        string role = "SITE_CRC",
        Guid? grantId = null)
    {
        var record = new KnowledgeStaffGrantRecord(
            UserId: userId,
            UserName: string.IsNullOrWhiteSpace(userName) ? userId : userName!,
            SiteId: siteCode,
            Role: role,
            GrantId: grantId ?? DeriveGuid($"{tenantId}:{studyCode}:{siteCode}:{userId}:{role}"));
        _grants[(tenantId, studyCode, siteCode, userId)] = record;
    }

    public void Clear() => _grants.Clear();

    public Task<IReadOnlyList<KnowledgeStaffGrantRecord>> ListStudyStaffGrantsAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        CancellationToken ct)
    {
        var items = _grants
            .Where(kvp => (kvp.Key.TenantId == tenantId || kvp.Key.TenantId.IsEmpty || tenantId.IsEmpty)
                && string.Equals(kvp.Key.StudyCode, studyCode, StringComparison.Ordinal))
            .Select(kvp => kvp.Value)
            .OrderBy(r => r.SiteId.Raw, StringComparer.Ordinal)
            .ThenBy(r => r.UserId, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<KnowledgeStaffGrantRecord>>(items);
    }

    public Task<KnowledgeQualificationRegistration> RegisterTrainingQualificationAsync(
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        int protocolVersionNumber,
        string qualificationCode,
        string holderId,
        string title,
        string sha256Hex,
        long sizeBytes,
        string etmfDocumentId,
        DateTimeOffset completedAt,
        string reasonForChange,
        string idempotencyKey,
        CancellationToken ct)
    {
        Guid artifactVersionId = DeriveGuid($"artifact:{tenantId}:{idempotencyKey}");
        Guid qualificationId = DeriveGuid($"qual:{tenantId}:{idempotencyKey}");
        var registration = new KnowledgeQualificationRegistration(
            $"artifact_version:{artifactVersionId:D}",
            $"qualification:{qualificationId:D}");
        _qualifications.Add((tenantId, studyCode, holderId, qualificationCode, registration));
        return Task.FromResult(registration);
    }

    private static Guid DeriveGuid(string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

public sealed class HttpEtmfTrainingIngestClient(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IClock clock) : IEtmfTrainingIngestClient
{
    private static readonly string[] DefaultProductFamilies =
    [
        nameof(ProductFamily.Blueprint),
        nameof(ProductFamily.Capture),
        nameof(ProductFamily.Orbit),
        nameof(ProductFamily.Assure),
    ];

    public async Task<string> IngestTrainingCertificateAsync(
        string? tenantCode,
        string studyCode,
        string siteCode,
        string assignmentId,
        byte[] signedPdfBytes,
        string reasonForChange,
        CancellationToken ct)
    {
        string normalizedReason = reasonForChange.Trim().Length >= 10
            ? reasonForChange.Trim()
            : $"21 CFR Part 11 training certificate filing: {reasonForChange.Trim()}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/etmf/ingest")
        {
            Content = JsonContent.Create(new
            {
                filename = $"Training_Certificate_{assignmentId}.pdf",
                content = (string?)null,
                studyId = studyCode,
                siteId = siteCode,
                mimeType = "application/pdf",
                zone = 5,
                section = "05.02",
                artifactCode = "05.02.04",
                artifactType = "Site Staff Qualification and Training Record",
                taxonomyVersion = "v3.2.0-complete",
                reasonForChange = normalizedReason,
                contentBase64 = Convert.ToBase64String(signedPdfBytes),
            }),
        };

        string? secret = gatewayOptions.CurrentValue.GatewaySecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var identity = new GatewayIdentity(
                UserId: TrustOperators.KnowledgeTrainingOperator.Id,
                Roles: [RoleNormalization.ROLE_SYSADMIN],
                SiteIds: [siteCode],
                TenantId: tenantCode ?? string.Empty,
                ChangeReason: normalizedReason,
                StudyIds: [studyCode],
                TenantReadOnly: false,
                ProductFamilies: DefaultProductFamilies);

            foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(secret)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        else
        {
            request.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(normalizedReason));
        }

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<WireEtmfDocument>(cancellationToken: ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(doc?.Id)
            ? doc.Id!
            : $"DOC-05-02-04-{assignmentId}";
    }

    private sealed record WireEtmfDocument(string? Id);
}

public sealed class InMemoryEtmfTrainingIngestClient : IEtmfTrainingIngestClient
{
    private readonly ConcurrentBag<(string StudyCode, string SiteCode, string AssignmentId, string DocumentId, int ByteLength)> _ingested = [];

    public IReadOnlyCollection<(string StudyCode, string SiteCode, string AssignmentId, string DocumentId, int ByteLength)> IngestedDocuments =>
        _ingested.ToArray();

    public Task<string> IngestTrainingCertificateAsync(
        string? tenantCode,
        string studyCode,
        string siteCode,
        string assignmentId,
        byte[] signedPdfBytes,
        string reasonForChange,
        CancellationToken ct)
    {
        string shortHash = Convert.ToHexString(SHA256.HashData(signedPdfBytes))[..8];
        string docId = $"DOC-050204-{shortHash}";
        _ingested.Add((studyCode, siteCode, assignmentId, docId, signedPdfBytes.Length));
        return Task.FromResult(docId);
    }
}

public static class KnowledgeTrainingServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeTrainingClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? kernelUrl = configuration["KERNEL_SERVICE_URL"] ?? configuration["Services:KernelUrl"];
        if (!string.IsNullOrWhiteSpace(kernelUrl))
        {
            services.AddHttpClient<IKnowledgeKernelTrustClient, HttpKnowledgeKernelTrustClient>(client =>
            {
                client.BaseAddress = new Uri(kernelUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<InMemoryKnowledgeKernelTrustClient>();
            services.AddSingleton<IKnowledgeKernelTrustClient>(sp => sp.GetRequiredService<InMemoryKnowledgeKernelTrustClient>());
        }

        string? etmfUrl = configuration["ETMF_SERVICE_URL"] ?? configuration["Services:EtmfUrl"];
        if (!string.IsNullOrWhiteSpace(etmfUrl))
        {
            services.AddHttpClient<IEtmfTrainingIngestClient, HttpEtmfTrainingIngestClient>(client =>
            {
                client.BaseAddress = new Uri(etmfUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<InMemoryEtmfTrainingIngestClient>();
            services.AddSingleton<IEtmfTrainingIngestClient>(sp => sp.GetRequiredService<InMemoryEtmfTrainingIngestClient>());
        }

        return services;
    }
}

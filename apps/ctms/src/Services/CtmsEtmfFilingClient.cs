using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Cadence.Apps.Ctms.Services;

public sealed record CtmsEtmfFilingRequest(
    string StudyCode,
    string SiteCode,
    string Filename,
    string ArtifactCode,
    byte[] PdfBytes,
    string Signer,
    DateTimeOffset SigningTimestamp,
    string SignatureManifestation,
    string CorrelationKey,
    string ReasonForChange,
    string? MetadataJson = null);

public interface ICtmsEtmfFilingClient
{
    Task<string?> FileSignedArtifactAsync(CtmsEtmfFilingRequest filing, CancellationToken ct);

    Task<string?> FileSignedDoaLogAsync(
        string studyCode,
        string siteCode,
        string correlationKey,
        byte[] signedPdfBytes,
        string sha256Hash,
        string signerUserId,
        DateTimeOffset signingTimestamp,
        string signatureManifestationJson,
        string reasonForChange,
        CancellationToken ct) =>
        FileSignedArtifactAsync(
            new CtmsEtmfFilingRequest(
                StudyCode: studyCode,
                SiteCode: siteCode,
                Filename: $"DOA_Log_{siteCode}_{signingTimestamp:yyyyMMddHHmmss}.pdf",
                ArtifactCode: "05.02.04",
                PdfBytes: signedPdfBytes,
                Signer: signerUserId,
                SigningTimestamp: signingTimestamp,
                SignatureManifestation: signatureManifestationJson,
                CorrelationKey: correlationKey,
                ReasonForChange: reasonForChange,
                MetadataJson: JsonSerializer.Serialize(new { sha256_hash = sha256Hash, artifact_code = "05.02.04" })),
            ct);

    Task<string?> FileVisitLetterAsync(
        string studyCode,
        string siteCode,
        string letterId,
        string templateType,
        byte[] signedPdfBytes,
        string sha256Hash,
        string signerUserId,
        DateTimeOffset signingTimestamp,
        string? signatureManifestationJson,
        string reasonForChange,
        CancellationToken ct) =>
        FileSignedArtifactAsync(
            new CtmsEtmfFilingRequest(
                StudyCode: studyCode,
                SiteCode: siteCode,
                Filename: $"Visit_Letter_{siteCode}_{letterId}.pdf",
                ArtifactCode: "05.02.06",
                PdfBytes: signedPdfBytes,
                Signer: signerUserId,
                SigningTimestamp: signingTimestamp,
                SignatureManifestation: signatureManifestationJson ?? string.Empty,
                CorrelationKey: $"ctms-letter:{letterId}",
                ReasonForChange: reasonForChange,
                MetadataJson: JsonSerializer.Serialize(new { letter_id = letterId, template_type = templateType, sha256_hash = sha256Hash, artifact_code = "05.02.06" })),
            ct);
}

/// <summary>
/// Files Part 11 signed CTMS artifacts (05.02.04 DoA logs and 05.02.06 monitoring follow-up letters)
/// into eTMF Zone 5 via gateway-signed HTTP calls modeled on <c>HttpEtmfCompletenessWebhookPublisher</c>.
/// </summary>
public sealed class HttpCtmsEtmfFilingClient(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IConfiguration configuration,
    IClock clock,
    ILogger<HttpCtmsEtmfFilingClient> logger,
    IHttpContextAccessor? httpContextAccessor = null) : ICtmsEtmfFilingClient
{
    private const string DefaultServiceUserId = "svc:ctms";
    private const string IngestPath = "/api/v1/etmf/ingest";
    private static readonly IReadOnlyList<string> DefaultOrbitFamilies = [nameof(ProductFamily.Orbit)];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<string?> FileSignedArtifactAsync(CtmsEtmfFilingRequest filing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filing);

        if (string.IsNullOrWhiteSpace(filing.StudyCode)
            || string.IsNullOrWhiteSpace(filing.SiteCode)
            || filing.PdfBytes is not { Length: > 0 })
        {
            return null;
        }

        Principal? ambientPrincipal = httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        if (ambientPrincipal is { TenantReadOnly: true })
        {
            logger.LogWarning(
                "Skipping eTMF filing for study {StudyCode} site {SiteCode}: workspace subscription is read-only.",
                filing.StudyCode,
                filing.SiteCode);
            return null;
        }

        Uri? baseAddress = ResolveBaseAddress();
        if (baseAddress is null)
        {
            logger.LogInformation(
                "ETMF_SERVICE_URL is not configured; skipping HTTP eTMF filing for {ArtifactCode} ({CorrelationKey}).",
                filing.ArtifactCode,
                filing.CorrelationKey);
            return null;
        }

        byte[]? secretBytes = ResolveGatewaySecretBytes();
        if (secretBytes is null || secretBytes.Length == 0)
        {
            logger.LogWarning(
                "Skipping eTMF filing for study {StudyCode} site {SiteCode}: GATEWAY_SECRET is not configured.",
                filing.StudyCode,
                filing.SiteCode);
            return null;
        }

        string? tenantId = ambientPrincipal?.TenantId;
        IReadOnlyList<string>? productFamilies = ResolveProductFamilies(ambientPrincipal, tenantId);

        string actorUserId = !string.IsNullOrWhiteSpace(ambientPrincipal?.UserId)
            ? ambientPrincipal!.UserId
            : DefaultServiceUserId;

        string effectiveReason = !string.IsNullOrWhiteSpace(filing.ReasonForChange) && filing.ReasonForChange.Trim().Length >= 10
            ? filing.ReasonForChange.Trim()
            : $"CTMS automated signed GxP filing ({filing.ArtifactCode})";

        var identity = new GatewayIdentity(
            UserId: actorUserId,
            Roles: [PermissionMatrix.CtmsServiceRole],
            SiteIds: [filing.SiteCode],
            UnblindedAccess: false,
            TenantId: tenantId,
            ChangeReason: effectiveReason,
            StudyIds: [filing.StudyCode],
            TenantReadOnly: ambientPrincipal?.TenantReadOnly ?? false,
            ProductFamilies: productFamilies,
            TenantScoping: ambientPrincipal?.TenantScoping);

        string artifactType = filing.ArtifactCode switch
        {
            "05.02.04" => "Delegation of Authority Log",
            "05.02.06" => "Monitoring Visit Follow-up Letter",
            _ => "Site Management Artifact",
        };

        var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = filing.StudyCode,
            ["site_id"] = filing.SiteCode,
            ["filename"] = filing.Filename,
            ["zone"] = 5,
            ["section"] = "05.02",
            ["artifact_code"] = filing.ArtifactCode,
            ["artifact_type"] = artifactType,
            ["mime_type"] = "application/pdf",
            ["reason_for_change"] = effectiveReason,
            ["content_base64"] = Convert.ToBase64String(filing.PdfBytes),
            ["auto_approve_signed"] = true,
            ["signer"] = filing.Signer,
            ["signing_timestamp"] = filing.SigningTimestamp,
            ["signature_manifestation"] = filing.SignatureManifestation,
            ["source_system"] = "CTMS",
            ["correlation_key"] = filing.CorrelationKey,
            ["metadata_json"] = filing.MetadataJson,
        };

        var targetUri = httpClient.BaseAddress is not null
            ? new Uri(IngestPath, UriKind.Relative)
            : new Uri(baseAddress, IngestPath);

        using var request = new HttpRequestMessage(HttpMethod.Post, targetUri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };

        foreach (var (headerName, headerValue) in signedHeaders)
        {
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "eTMF ingest returned HTTP {StatusCode} for artifact {ArtifactCode} study {StudyCode} site {SiteCode}.",
                    (int)response.StatusCode,
                    filing.ArtifactCode,
                    filing.StudyCode,
                    filing.SiteCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    return idProp.GetString();
                }

                if (json.TryGetProperty("document_id", out var docIdProp) && docIdProp.ValueKind == JsonValueKind.String)
                {
                    return docIdProp.GetString();
                }
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to deliver signed CTMS artifact {ArtifactCode} to eTMF for study {StudyCode} site {SiteCode}.",
                filing.ArtifactCode,
                filing.StudyCode,
                filing.SiteCode);
            return null;
        }
    }

    private static IReadOnlyList<string>? ResolveProductFamilies(Principal? ambientPrincipal, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        return [.. (ambientPrincipal?.ProductFamilies ?? [])
            .Concat(DefaultOrbitFamilies)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private Uri? ResolveBaseAddress()
    {
        if (httpClient.BaseAddress is not null)
        {
            return httpClient.BaseAddress;
        }

        string? rawUrl = configuration["ETMF_SERVICE_URL"];
        return !string.IsNullOrWhiteSpace(rawUrl) && Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private byte[]? ResolveGatewaySecretBytes()
    {
        var configuredBytes = gatewayOptions.CurrentValue.GatewaySecretBytes;
        if (configuredBytes.Length > 0)
        {
            return configuredBytes.ToArray();
        }

        string? secret = configuration["GATEWAY_SECRET"];
        return string.IsNullOrEmpty(secret) ? null : GatewayIdentity.EncodeSecret(secret);
    }
}

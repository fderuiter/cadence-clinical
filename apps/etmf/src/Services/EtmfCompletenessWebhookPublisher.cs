using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Etmf.Services;

public interface IEtmfCompletenessWebhookPublisher
{
    Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        CancellationToken ct);
}

public sealed class HttpEtmfCompletenessWebhookPublisher(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IConfiguration configuration,
    IClock clock,
    ILogger<HttpEtmfCompletenessWebhookPublisher> logger) : IEtmfCompletenessWebhookPublisher
{
    private const string SiteActivationMilestone = "SITE_ACTIVATION";
    private const string ServiceUserId = "svc:etmf";
    private const string WebhookPath = "/api/v1/execution/webhooks/etmf";

    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return;
        }

        var targetSites = await ResolveTargetSitesAsync(dbContext, studyId, siteId, ct);
        if (targetSites.Count == 0)
        {
            return;
        }

        byte[]? secretBytes = ResolveGatewaySecretBytes();
        if (secretBytes is null || secretBytes.Length == 0)
        {
            logger.LogWarning(
                "Skipping eTMF completeness webhook for study {StudyId}: GATEWAY_SECRET is not configured.",
                studyId);
            return;
        }

        if (httpClient.BaseAddress is null)
        {
            logger.LogWarning(
                "Skipping eTMF completeness webhook for study {StudyId}: EXECUTION_SERVICE_URL is not configured.",
                studyId);
            return;
        }

        foreach (string targetSiteId in targetSites)
        {
            var (isComplete, missingArtifacts) = await EvaluateSiteActivationCompletenessAsync(
                dbContext, studyId, targetSiteId, ct);

            await PostWebhookAsync(
                studyId: studyId,
                siteId: targetSiteId,
                tenantId: tenantId,
                reasonForChange: reasonForChange,
                isComplete: isComplete,
                missingArtifacts: missingArtifacts,
                secretBytes: secretBytes,
                ct: ct);
        }
    }

    public static async Task<(bool IsComplete, IReadOnlyList<string> MissingArtifacts)> EvaluateSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string siteId,
        CancellationToken ct)
    {
        var expectedDocs = await dbContext.TmfExpectedDocuments
            .AsNoTracking()
            .Where(e => e.StudyId == studyId
                        && e.Milestone == SiteActivationMilestone
                        && (e.SiteId == siteId || e.SiteId == null || e.SiteId == string.Empty))
            .ToListAsync(ct);

        var studyAndSiteDocs = await dbContext.TmfDocuments
            .AsNoTracking()
            .Where(d => d.StudyId == studyId
                        && (d.SiteId == siteId || d.SiteId == null || d.SiteId == string.Empty))
            .ToListAsync(ct);

        var siteDocs = studyAndSiteDocs
            .Where(d => string.Equals(d.SiteId, siteId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var missing = new List<string>();
        var seenMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddMissing(string label)
        {
            if (!string.IsNullOrWhiteSpace(label) && seenMissing.Add(label))
            {
                missing.Add(label);
            }
        }

        if (expectedDocs.Count > 0)
        {
            foreach (var expected in expectedDocs)
            {
                bool expectSiteSpecific = !string.IsNullOrWhiteSpace(expected.SiteId);
                var candidatePool = expectSiteSpecific ? siteDocs : studyAndSiteDocs;

                bool satisfied = candidatePool.Any(doc =>
                    IsApprovedOrFiled(doc.Status)
                    && MatchesExpectedArtifact(doc, expected.ArtifactType));

                if (!satisfied)
                {
                    AddMissing(expected.ArtifactType);
                }
            }

            foreach (var doc in siteDocs.Where(d => !IsApprovedOrFiled(d.Status)))
            {
                string label = ArtifactLabel(doc);
                bool hasApprovedCounterpart = siteDocs.Any(other =>
                    IsApprovedOrFiled(other.Status)
                    && MatchesArtifactLabel(other, label));

                if (!hasApprovedCounterpart)
                {
                    AddMissing(label);
                }
            }

            return (missing.Count == 0, missing);
        }

        var approvedLabels = new HashSet<string>(
            siteDocs.Where(d => IsApprovedOrFiled(d.Status)).Select(ArtifactLabel),
            StringComparer.OrdinalIgnoreCase);

        foreach (var doc in siteDocs.Where(d => !IsApprovedOrFiled(d.Status)))
        {
            string label = ArtifactLabel(doc);
            if (!approvedLabels.Contains(label))
            {
                AddMissing(label);
            }
        }

        bool isComplete = approvedLabels.Count > 0 && missing.Count == 0;
        return (isComplete, missing);
    }

    private static async Task<IReadOnlyList<string>> ResolveTargetSitesAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            return [siteId];
        }

        var expectedSiteIds = await dbContext.TmfExpectedDocuments
            .AsNoTracking()
            .Where(e => e.StudyId == studyId
                        && e.Milestone == SiteActivationMilestone
                        && e.SiteId != null
                        && e.SiteId != string.Empty)
            .Select(e => e.SiteId!)
            .Distinct()
            .ToListAsync(ct);

        var docSiteIds = await dbContext.TmfDocuments
            .AsNoTracking()
            .Where(d => d.StudyId == studyId
                        && d.SiteId != null
                        && d.SiteId != string.Empty)
            .Select(d => d.SiteId!)
            .Distinct()
            .ToListAsync(ct);

        return expectedSiteIds
            .Concat(docSiteIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task PostWebhookAsync(
        string studyId,
        string siteId,
        string? tenantId,
        string reasonForChange,
        bool isComplete,
        IReadOnlyList<string> missingArtifacts,
        byte[] secretBytes,
        CancellationToken ct)
    {
        var identity = new GatewayIdentity(
            UserId: ServiceUserId,
            Roles: [PermissionMatrix.EtmfServiceRole],
            SiteIds: [siteId],
            UnblindedAccess: false,
            TenantId: tenantId,
            ChangeReason: string.IsNullOrWhiteSpace(reasonForChange)
                ? "eTMF completeness evaluation sync"
                : reasonForChange,
            StudyIds: [studyId]);

        var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = studyId,
            ["site_id"] = siteId,
            ["milestone"] = SiteActivationMilestone,
            ["is_complete"] = isComplete,
            ["missing_artifacts"] = missingArtifacts,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = JsonContent.Create(payload, options: WebhookJsonOptions),
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
                    "Execution completeness webhook returned HTTP {StatusCode} for study {StudyId} site {SiteId}.",
                    (int)response.StatusCode,
                    studyId,
                    siteId);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to deliver eTMF completeness webhook to Execution for study {StudyId} site {SiteId}.",
                studyId,
                siteId);
        }
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

    private static bool IsApprovedOrFiled(string? status) =>
        string.Equals(status, TmfDocumentLifecycleState.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, TmfDocumentLifecycleState.Filed, StringComparison.OrdinalIgnoreCase);

    private static string ArtifactLabel(TmfDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.ArtifactType))
        {
            return doc.ArtifactType;
        }

        if (!string.IsNullOrWhiteSpace(doc.ArtifactCode))
        {
            return doc.ArtifactCode;
        }

        return doc.Filename;
    }

    private static bool MatchesExpectedArtifact(TmfDocument doc, string expectedArtifact) =>
        string.Equals(doc.ArtifactType, expectedArtifact, StringComparison.OrdinalIgnoreCase)
        || string.Equals(doc.ArtifactCode, expectedArtifact, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesArtifactLabel(TmfDocument doc, string label) =>
        string.Equals(ArtifactLabel(doc), label, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(doc.ArtifactCode)
            && string.Equals(doc.ArtifactCode, label, StringComparison.OrdinalIgnoreCase));
}

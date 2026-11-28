using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Etmf.Services;

public readonly record struct EtmfArtifactTaxonomy(
    string? ArtifactType,
    string? ArtifactCode);

public sealed record EtmfDocumentReviewContext(
    EtmfArtifactTaxonomy Current,
    EtmfArtifactTaxonomy? Previous = null);

public interface IEtmfCompletenessWebhookPublisher
{
    Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        CancellationToken ct);

    Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        EtmfDocumentReviewContext? reviewContext,
        CancellationToken ct) =>
        PublishSiteActivationCompletenessAsync(
            dbContext,
            studyId,
            siteId,
            tenantId,
            reasonForChange,
            ct);
}

public sealed class HttpEtmfCompletenessWebhookPublisher(
    HttpClient httpClient,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IConfiguration configuration,
    IClock clock,
    ILogger<HttpEtmfCompletenessWebhookPublisher> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IEtmfCompletenessWebhookPublisher
{
    private const string SiteActivationMilestone = "SITE_ACTIVATION";
    private const string ServiceUserId = "svc:etmf";
    private const string WebhookPath = "/api/v1/execution/webhooks/etmf";
    private const string BatchWebhookPath = "/api/v1/execution/webhooks/etmf/batch";
    private static readonly IReadOnlyList<string> DefaultOrbitFamilies = [nameof(ProductFamily.Orbit)];

    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        CancellationToken ct) =>
        PublishSiteActivationCompletenessAsync(
            dbContext,
            studyId,
            siteId,
            tenantId,
            reasonForChange,
            reviewContext: null,
            ct);

    public async Task PublishSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string? siteId,
        string? tenantId,
        string reasonForChange,
        EtmfDocumentReviewContext? reviewContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return;
        }

        Principal? ambientPrincipal = httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        if (ambientPrincipal is { TenantReadOnly: true })
        {
            logger.LogWarning(
                "Skipping eTMF completeness webhook for study {StudyId}: workspace subscription is expired (read-only).",
                studyId);
            return;
        }

        if (ambientPrincipal?.TenantId is not null && !ambientPrincipal.HasProductFamily(ProductFamily.Orbit))
        {
            logger.LogWarning(
                "Skipping eTMF completeness webhook for study {StudyId}: workspace is not entitled to Orbit.",
                studyId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            if (!TryEnsureWebhookConfigured(studyId, out var secretBytes))
            {
                return;
            }

            var (isComplete, missingArtifacts) = await EvaluateSiteActivationCompletenessAsync(
                dbContext, studyId, siteId, ct);

            await PostWebhookAsync(
                studyId: studyId,
                siteId: siteId,
                tenantId: tenantId,
                reasonForChange: reasonForChange,
                isComplete: isComplete,
                missingArtifacts: missingArtifacts,
                secretBytes: secretBytes,
                ct: ct);
            return;
        }

        if (!TryEnsureWebhookConfigured(studyId, out var batchSecretBytes))
        {
            return;
        }

        List<ProjectedExpectedDoc> allExpectedDocs;
        List<ProjectedDoc> allStudyDocs;

        bool openedConnection = await OpenConnectionIfNeededAsync(dbContext, ct);
        try
        {
            allExpectedDocs = await dbContext.TmfExpectedDocuments
                .AsNoTracking()
                .Where(e => e.StudyId == studyId && e.Milestone == SiteActivationMilestone)
                .OrderBy(e => e.SiteId == null || e.SiteId == string.Empty ? 0 : 1)
                .ThenBy(e => e.Zone ?? int.MaxValue)
                .ThenBy(e => e.Section)
                .ThenBy(e => e.ArtifactType)
                .ThenBy(e => e.Id)
                .Select(e => new ProjectedExpectedDoc(e.SiteId, e.ArtifactType))
                .ToListAsync(ct);

            if (reviewContext is not null)
            {
                bool affectsStartupReadiness = allExpectedDocs.Any(e =>
                    string.IsNullOrWhiteSpace(e.SiteCode)
                    && (MatchesTaxonomy(reviewContext.Current, e.ArtifactType)
                        || (reviewContext.Previous is { } previous
                            && MatchesTaxonomy(previous, e.ArtifactType))));

                if (!affectsStartupReadiness)
                {
                    return;
                }
            }

            allStudyDocs = await dbContext.TmfDocuments
                .AsNoTracking()
                .Where(d => d.StudyId == studyId)
                .OrderBy(d => d.Id)
                .Select(d => new ProjectedDoc(
                    d.SiteId,
                    d.Status,
                    d.ArtifactType,
                    d.ArtifactCode,
                    d.Filename))
                .ToListAsync(ct);
        }
        finally
        {
            if (openedConnection)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }

        var targetSites = allExpectedDocs
            .Where(e => !string.IsNullOrWhiteSpace(e.SiteCode))
            .Select(e => e.SiteCode!)
            .Concat(allStudyDocs
                .Where(d => !string.IsNullOrWhiteSpace(d.SiteCode))
                .Select(d => d.SiteCode!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetSites.Count == 0)
        {
            return;
        }

        var batchItems = EvaluateStudySitesInMemory(targetSites, allExpectedDocs, allStudyDocs);
        await PostBatchWebhookAsync(
            studyId: studyId,
            tenantId: tenantId,
            reasonForChange: reasonForChange,
            targetSites: targetSites,
            sites: batchItems,
            secretBytes: batchSecretBytes,
            ct: ct);
    }

    public static async Task<(bool IsComplete, IReadOnlyList<string> MissingArtifacts)> EvaluateSiteActivationCompletenessAsync(
        EtmfDbContext dbContext,
        string studyId,
        string siteId,
        CancellationToken ct)
    {
        List<ProjectedExpectedDoc> expectedDocs;
        List<ProjectedDoc> studyAndSiteDocs;

        bool openedConnection = await OpenConnectionIfNeededAsync(dbContext, ct);
        try
        {
            expectedDocs = await dbContext.TmfExpectedDocuments
                .AsNoTracking()
                .Where(e => e.StudyId == studyId
                            && e.Milestone == SiteActivationMilestone
                            && (e.SiteId == siteId || e.SiteId == null || e.SiteId == string.Empty))
                .OrderBy(e => e.SiteId == null || e.SiteId == string.Empty ? 0 : 1)
                .ThenBy(e => e.Zone ?? int.MaxValue)
                .ThenBy(e => e.Section)
                .ThenBy(e => e.ArtifactType)
                .ThenBy(e => e.Id)
                .Select(e => new ProjectedExpectedDoc(e.SiteId, e.ArtifactType))
                .ToListAsync(ct);

            studyAndSiteDocs = await dbContext.TmfDocuments
                .AsNoTracking()
                .Where(d => d.StudyId == studyId
                            && (d.SiteId == siteId || d.SiteId == null || d.SiteId == string.Empty))
                .OrderBy(d => d.Id)
                .Select(d => new ProjectedDoc(
                    d.SiteId,
                    d.Status,
                    d.ArtifactType,
                    d.ArtifactCode,
                    d.Filename))
                .ToListAsync(ct);
        }
        finally
        {
            if (openedConnection)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }

        var centralDocs = new List<ProjectedDoc>();
        var siteDocs = new List<ProjectedDoc>();
        foreach (var doc in studyAndSiteDocs)
        {
            if (string.Equals(doc.SiteCode, siteId, StringComparison.OrdinalIgnoreCase))
            {
                siteDocs.Add(doc);
            }
            else if (string.IsNullOrWhiteSpace(doc.SiteCode))
            {
                centralDocs.Add(doc);
            }
        }

        return ComputeSiteActivationCompleteness(expectedDocs, centralDocs, siteDocs);
    }

    private static IReadOnlyList<BatchSiteSnapshot> EvaluateStudySitesInMemory(
        IReadOnlyList<string> targetSites,
        IReadOnlyList<ProjectedExpectedDoc> allExpectedDocs,
        IReadOnlyList<ProjectedDoc> allStudyDocs)
    {
        var centralExpected = new List<(int Index, ProjectedExpectedDoc Doc)>();
        var expectedBySite = new Dictionary<string, List<(int Index, ProjectedExpectedDoc Doc)>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allExpectedDocs.Count; i++)
        {
            var expected = allExpectedDocs[i];
            if (string.IsNullOrWhiteSpace(expected.SiteCode))
            {
                centralExpected.Add((i, expected));
                continue;
            }

            if (!expectedBySite.TryGetValue(expected.SiteCode, out var list))
            {
                list = [];
                expectedBySite[expected.SiteCode] = list;
            }

            list.Add((i, expected));
        }

        var centralDocs = new List<ProjectedDoc>();
        var docsBySite = new Dictionary<string, List<ProjectedDoc>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in allStudyDocs)
        {
            if (string.IsNullOrWhiteSpace(doc.SiteCode))
            {
                centralDocs.Add(doc);
                continue;
            }

            if (!docsBySite.TryGetValue(doc.SiteCode, out var list))
            {
                list = [];
                docsBySite[doc.SiteCode] = list;
            }

            list.Add(doc);
        }

        var results = new List<BatchSiteSnapshot>(targetSites.Count);
        foreach (string targetSiteId in targetSites)
        {
            var siteExpected = MergeExpectedByIndex(
                centralExpected,
                expectedBySite.GetValueOrDefault(targetSiteId));
            var siteDocs = docsBySite.GetValueOrDefault(targetSiteId) ?? (IReadOnlyList<ProjectedDoc>)[];

            var (isComplete, missingArtifacts) = ComputeSiteActivationCompleteness(
                siteExpected,
                centralDocs,
                siteDocs);
            results.Add(new BatchSiteSnapshot(targetSiteId, isComplete, missingArtifacts));
        }

        return results;
    }

    private static IReadOnlyList<ProjectedExpectedDoc> MergeExpectedByIndex(
        List<(int Index, ProjectedExpectedDoc Doc)> centralExpected,
        List<(int Index, ProjectedExpectedDoc Doc)>? siteExpected)
    {
        if (siteExpected is null || siteExpected.Count == 0)
        {
            if (centralExpected.Count == 0)
            {
                return [];
            }

            var onlyCentral = new ProjectedExpectedDoc[centralExpected.Count];
            for (int i = 0; i < centralExpected.Count; i++)
            {
                onlyCentral[i] = centralExpected[i].Doc;
            }

            return onlyCentral;
        }

        if (centralExpected.Count == 0)
        {
            var onlySite = new ProjectedExpectedDoc[siteExpected.Count];
            for (int i = 0; i < siteExpected.Count; i++)
            {
                onlySite[i] = siteExpected[i].Doc;
            }

            return onlySite;
        }

        var merged = new ProjectedExpectedDoc[centralExpected.Count + siteExpected.Count];
        int c = 0;
        int s = 0;
        int m = 0;
        while (c < centralExpected.Count && s < siteExpected.Count)
        {
            if (centralExpected[c].Index <= siteExpected[s].Index)
            {
                merged[m++] = centralExpected[c++].Doc;
            }
            else
            {
                merged[m++] = siteExpected[s++].Doc;
            }
        }

        while (c < centralExpected.Count)
        {
            merged[m++] = centralExpected[c++].Doc;
        }

        while (s < siteExpected.Count)
        {
            merged[m++] = siteExpected[s++].Doc;
        }

        return merged;
    }

    private static (bool IsComplete, IReadOnlyList<string> MissingArtifacts) ComputeSiteActivationCompleteness(
        IReadOnlyList<ProjectedExpectedDoc> expectedDocs,
        IReadOnlyList<ProjectedDoc> centralDocs,
        IReadOnlyList<ProjectedDoc> siteDocs)
    {
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
                bool expectSiteSpecific = !string.IsNullOrWhiteSpace(expected.SiteCode);
                bool satisfied = expectSiteSpecific
                    ? AnyApprovedMatching(siteDocs, expected.ArtifactType)
                    : AnyApprovedMatching(centralDocs, expected.ArtifactType)
                      || AnyApprovedMatching(siteDocs, expected.ArtifactType);

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

    private static async Task<bool> OpenConnectionIfNeededAsync(
        EtmfDbContext dbContext,
        CancellationToken ct)
    {
        if (!dbContext.Database.IsRelational())
        {
            return false;
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State == ConnectionState.Open)
        {
            return false;
        }

        await dbContext.Database.OpenConnectionAsync(ct);
        return true;
    }

    private bool TryEnsureWebhookConfigured(string studyId, out byte[] secretBytes)
    {
        byte[]? resolved = ResolveGatewaySecretBytes();
        if (resolved is null || resolved.Length == 0)
        {
            logger.LogWarning(
                "Skipping eTMF completeness webhook for study {StudyId}: GATEWAY_SECRET is not configured.",
                studyId);
            secretBytes = [];
            return false;
        }

        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                $"Cannot publish eTMF completeness webhook for study '{studyId}': EXECUTION_SERVICE_URL is not configured on HttpClient.BaseAddress.");
        }

        secretBytes = resolved;
        return true;
    }

    private GatewayIdentity BuildServiceIdentity(
        string studyId,
        IReadOnlyList<string> siteIds,
        string? tenantId,
        string reasonForChange)
    {
        Principal? ambientPrincipal = httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        IReadOnlyList<string>? productFamilies;
        if (ambientPrincipal?.TenantId is not null)
        {
            productFamilies = ambientPrincipal.ProductFamilies;
        }
        else
        {
            productFamilies = string.IsNullOrWhiteSpace(tenantId) ? null : DefaultOrbitFamilies;
        }

        return new GatewayIdentity(
            UserId: ServiceUserId,
            Roles: [PermissionMatrix.EtmfServiceRole],
            SiteIds: siteIds,
            UnblindedAccess: false,
            TenantId: tenantId,
            ChangeReason: string.IsNullOrWhiteSpace(reasonForChange)
                ? "eTMF completeness evaluation sync"
                : reasonForChange,
            StudyIds: [studyId],
            TenantReadOnly: ambientPrincipal?.TenantReadOnly ?? false,
            ProductFamilies: productFamilies,
            TenantScoping: ambientPrincipal?.TenantScoping);
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
        var identity = BuildServiceIdentity(studyId, [siteId], tenantId, reasonForChange);
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

    private async Task PostBatchWebhookAsync(
        string studyId,
        string? tenantId,
        string reasonForChange,
        IReadOnlyList<string> targetSites,
        IReadOnlyList<BatchSiteSnapshot> sites,
        byte[] secretBytes,
        CancellationToken ct)
    {
        var identity = BuildServiceIdentity(studyId, targetSites, tenantId, reasonForChange);
        var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
        var wireSites = new List<Dictionary<string, object?>>(sites.Count);
        foreach (var site in sites)
        {
            wireSites.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["site_id"] = site.SiteCode,
                ["is_complete"] = site.IsComplete,
                ["missing_artifacts"] = site.MissingArtifacts,
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = studyId,
            ["milestone"] = SiteActivationMilestone,
            ["sites"] = wireSites,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BatchWebhookPath)
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
                    "Execution batch completeness webhook returned HTTP {StatusCode} for study {StudyId} ({SiteCount} sites).",
                    (int)response.StatusCode,
                    studyId,
                    sites.Count);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to deliver eTMF batch completeness webhook to Execution for study {StudyId} ({SiteCount} sites).",
                studyId,
                sites.Count);
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

    private static bool AnyApprovedMatching(IReadOnlyList<ProjectedDoc> docs, string expectedArtifact)
    {
        for (int i = 0; i < docs.Count; i++)
        {
            var doc = docs[i];
            if (IsApprovedOrFiled(doc.Status) && MatchesExpectedArtifact(doc, expectedArtifact))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsApprovedOrFiled(string? status) =>
        string.Equals(status, TmfDocumentLifecycleState.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, TmfDocumentLifecycleState.Filed, StringComparison.OrdinalIgnoreCase);

    private static string ArtifactLabel(ProjectedDoc doc)
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

    private static bool MatchesTaxonomy(EtmfArtifactTaxonomy taxonomy, string expectedArtifact) =>
        (!string.IsNullOrWhiteSpace(taxonomy.ArtifactType)
            && string.Equals(taxonomy.ArtifactType, expectedArtifact, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(taxonomy.ArtifactCode)
            && string.Equals(taxonomy.ArtifactCode, expectedArtifact, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesExpectedArtifact(ProjectedDoc doc, string expectedArtifact) =>
        string.Equals(doc.ArtifactType, expectedArtifact, StringComparison.OrdinalIgnoreCase)
        || string.Equals(doc.ArtifactCode, expectedArtifact, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesArtifactLabel(ProjectedDoc doc, string label) =>
        string.Equals(ArtifactLabel(doc), label, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(doc.ArtifactCode)
            && string.Equals(doc.ArtifactCode, label, StringComparison.OrdinalIgnoreCase));

    private sealed record ProjectedExpectedDoc(
        string? SiteCode,
        string ArtifactType);

    private sealed record ProjectedDoc(
        string? SiteCode,
        string Status,
        string? ArtifactType,
        string? ArtifactCode,
        string Filename);

    private sealed record BatchSiteSnapshot(
        string SiteCode,
        bool IsComplete,
        IReadOnlyList<string> MissingArtifacts);
}

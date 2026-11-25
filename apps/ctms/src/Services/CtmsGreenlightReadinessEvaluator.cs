using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Ctms.Services;

public sealed record GreenlightLiveEvaluation(
    bool FacetRegulatoryIrb,
    bool FacetCtaBudget,
    bool FacetEisfBaseline,
    bool FacetDoaLog,
    bool FacetStaffTraining,
    bool FacetIpRelease,
    IReadOnlyList<GreenlightFacetItemDto> Facets,
    IReadOnlyList<string> BlockingReasons,
    int ReadinessPercentage,
    bool IsCompliant,
    string OverallStatus);

public interface ICtmsGreenlightReadinessEvaluator
{
    Task<GreenlightLiveEvaluation> EvaluateSiteAsync(
        CtmsDbContext db,
        CtmsSiteGreenlightGate? existingGate,
        string studyCode,
        string siteCode,
        CancellationToken ct);
}

/// <summary>
/// Evaluates the six site-startup greenlight facets from live CTMS milestones/visits/delegations and
/// gateway-signed service checks against eTMF, Knowledge training, and Execution site compliance.
/// </summary>
public sealed class CtmsGreenlightReadinessEvaluator(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions,
    IConfiguration configuration,
    IClock clock,
    ILogger<CtmsGreenlightReadinessEvaluator> logger,
    IHttpContextAccessor? httpContextAccessor = null) : ICtmsGreenlightReadinessEvaluator
{
    private const string ServiceUserId = "svc:ctms";
    private static readonly IReadOnlyList<string> ReadinessServiceFamilies =
    [
        nameof(ProductFamily.Allocate),
        nameof(ProductFamily.Assure),
        nameof(ProductFamily.Orbit),
    ];

    public async Task<GreenlightLiveEvaluation> EvaluateSiteAsync(
        CtmsDbContext db,
        CtmsSiteGreenlightGate? existingGate,
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var milestones = await db.CtmsSiteMilestones
            .AsNoTracking()
            .Where(m => m.StudyId == studyCode && m.SiteId == siteCode)
            .ToListAsync(ct);

        var delegations = await db.CtmsDelegations
            .AsNoTracking()
            .Where(d => d.StudyId == studyCode && d.SiteId == siteCode && d.IsActive)
            .ToListAsync(ct);

        var visits = await db.CtmsMonitoringVisits
            .AsNoTracking()
            .Where(v => v.StudyId == studyCode && v.SiteId == siteCode)
            .ToListAsync(ct);

        var etmfDocs = await TryFetchEtmfDocumentsAsync(studyCode, siteCode, ct);
        var trainingAssignments = await TryFetchTrainingAssignmentsAsync(studyCode, siteCode, ct);
        var executionCompliance = await TryFetchExecutionComplianceAsync(studyCode, siteCode, ct);

        // 1. Regulatory & IRB Approval
        bool hasRegMilestone = milestones.Any(m =>
            IsCompletedMilestone(m)
            && MatchesAny(m.MilestoneType, "IRB", "REGULATORY", "ETHICS", "EC_APPROVAL"));
        bool hasRegEtmf = etmfDocs.Any(d =>
            d.IsApproved
            && (d.ArtifactCode.StartsWith("04.", StringComparison.OrdinalIgnoreCase)
                || d.ArtifactCode.StartsWith("05.01.", StringComparison.OrdinalIgnoreCase)
                || MatchesAny(d.ArtifactType, "IRB", "REGULATORY", "ETHICS")));
        bool facetRegulatory = (existingGate?.FacetRegulatoryIrb ?? false) || hasRegMilestone || hasRegEtmf;
        string regSummary = DescribeRegulatoryFacet(facetRegulatory, hasRegEtmf, hasRegMilestone);

        // 2. Clinical Trial Agreement & Budget
        bool hasCtaMilestone = milestones.Any(m =>
            IsCompletedMilestone(m)
            && MatchesAny(m.MilestoneType, "CTA", "CONTRACT", "BUDGET", "SITE_AGREEMENT"));
        bool hasCtaEtmf = etmfDocs.Any(d =>
            d.IsApproved
            && (d.ArtifactCode is "05.02.02" or "05.02.03" or "05.03.01"
                || MatchesAny(d.ArtifactType, "CTA", "CONTRACT", "FINANCIAL", "BUDGET")));
        bool facetCta = (existingGate?.FacetCtaBudget ?? false) || hasCtaMilestone || hasCtaEtmf;
        string ctaSummary = DescribeCtaFacet(facetCta, hasCtaEtmf, hasCtaMilestone);

        // 3. eISF Baseline Package
        bool hasEisfMilestone = milestones.Any(m =>
            IsCompletedMilestone(m)
            && MatchesAny(m.MilestoneType, "EISF", "ISF", "SIV_READY", "BASELINE_DOCS"));
        bool hasCompletedSiv = visits.Any(v =>
            string.Equals(v.VisitType, "SIV", StringComparison.OrdinalIgnoreCase)
            && v.Status is CtmsMonitoringVisitStatus.Completed or CtmsMonitoringVisitStatus.Signed_Off);
        bool hasApprovedZone5Baseline = etmfDocs.Any(d =>
            d.IsApproved && d.Zone == 5 && d.ArtifactCode != "05.02.06");
        bool facetEisf = (existingGate?.FacetEisfBaseline ?? false)
            || hasEisfMilestone
            || hasCompletedSiv
            || (executionCompliance?.IsCompliant ?? false)
            || hasApprovedZone5Baseline;
        string eisfSummary = DescribeEisfFacet(
            facetEisf,
            executionCompliance is { IsCompliant: true },
            hasApprovedZone5Baseline,
            hasCompletedSiv);

        // 4. Delegation of Authority Log (CTMS canonical owner)
        bool facetDoa;
        string doaSummary;
        if (delegations.Count > 0)
        {
            int signedCount = delegations.Count(d => d.SignedOff);
            int pendingCount = delegations.Count - signedCount;
            facetDoa = pendingCount == 0;
            doaSummary = facetDoa
                ? $"{signedCount} active delegation(s) signed by Principal Investigator"
                : $"{pendingCount} of {delegations.Count} active delegation(s) pending PI signature";
        }
        else if (existingGate is { FacetDoaLog: true })
        {
            facetDoa = true;
            doaSummary = "Principal Investigator DoA log verified";
        }
        else
        {
            facetDoa = false;
            doaSummary = "No Principal Investigator-signed delegation entries recorded";
        }

        // 5. Protocol Staff Training (Knowledge service)
        bool facetTraining;
        string trainingSummary;
        if (trainingAssignments is { Count: > 0 })
        {
            int completedCount = trainingAssignments.Count(a =>
                string.Equals(a.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));
            int pendingCount = trainingAssignments.Count - completedCount;
            facetTraining = completedCount > 0 && pendingCount == 0;
            trainingSummary = facetTraining
                ? $"{completedCount} staff protocol training assignment(s) completed"
                : $"{pendingCount} of {trainingAssignments.Count} assigned staff training item(s) pending completion";
        }
        else if (milestones.Any(m => IsCompletedMilestone(m) && MatchesAny(m.MilestoneType, "TRAINING", "QUALIFICATION")))
        {
            facetTraining = true;
            trainingSummary = "Site protocol training milestone completed";
        }
        else if (existingGate is { FacetStaffTraining: true })
        {
            facetTraining = true;
            trainingSummary = "Protocol staff training verified";
        }
        else
        {
            facetTraining = false;
            trainingSummary = "No completed protocol training assignments recorded for site staff";
        }

        // 6. Investigational Product Depot Release
        bool hasIpMilestone = milestones.Any(m =>
            IsCompletedMilestone(m)
            && MatchesAny(m.MilestoneType, "IP_RELEASE", "DRUG_RELEASE", "SUPPLY_READY", "DEPOT_RELEASE", "IP_SHIPPED"));
        bool hasIpKitsReady = await TryHasSiteIpSupplyAsync(siteCode, studyCode, ct);
        bool facetIp = (existingGate?.FacetIpRelease ?? false)
            || hasIpMilestone
            || hasIpKitsReady
            || (executionCompliance?.IsSiteActive ?? false);
        string ipSummary = DescribeIpFacet(facetIp, hasIpMilestone, hasIpKitsReady, executionCompliance is { IsSiteActive: true });

        var facets = new List<GreenlightFacetItemDto>(6)
        {
            new("REGULATORY_IRB", "Regulatory & IRB Approval", facetRegulatory, regSummary),
            new("CTA_BUDGET", "Clinical Trial Agreement & Budget", facetCta, ctaSummary),
            new("EISF_BASELINE", "eISF Baseline Package", facetEisf, eisfSummary),
            new("DOA_LOG", "Delegation of Authority Log", facetDoa, doaSummary),
            new("STAFF_TRAINING", "Protocol Staff Training", facetTraining, trainingSummary),
            new("IP_RELEASE", "Investigational Product Release", facetIp, ipSummary),
        };

        bool isOverridden = existingGate?.IsOverridden ?? false;
        var blockingReasons = new List<string>();
        if (!isOverridden)
        {
            if (!facetRegulatory) blockingReasons.Add("Pending IRB/Regulatory Approval");
            if (!facetCta) blockingReasons.Add("Missing Executed CTA & Budget");
            if (!facetEisf) blockingReasons.Add("Incomplete eISF Baseline");
            if (!facetDoa) blockingReasons.Add("Pending Delegation of Authority Log");
            if (!facetTraining) blockingReasons.Add("Incomplete Staff Protocol Training");
            if (!facetIp) blockingReasons.Add("Pending Investigational Product Depot Release");
        }

        int fulfilledCount = facets.Count(f => f.IsSatisfied);
        int percentage = isOverridden ? 100 : (int)Math.Round(fulfilledCount / 6.0 * 100);
        bool isCompliant = blockingReasons.Count == 0 || isOverridden;

        bool alreadyCertified = existingGate?.GreenlightCertifiedAt is not null
            || existingGate?.OverallStatus is CtmsGreenlightStatus.Approved or CtmsGreenlightStatus.Overridden;

        string overallStatus;
        if (alreadyCertified)
        {
            overallStatus = existingGate!.OverallStatus.ToString().ToUpperInvariant();
        }
        else
        {
            overallStatus = isCompliant ? "READY" : "PENDING";
        }

        return new GreenlightLiveEvaluation(
            facetRegulatory,
            facetCta,
            facetEisf,
            facetDoa,
            facetTraining,
            facetIp,
            facets,
            blockingReasons,
            percentage,
            isCompliant,
            overallStatus);
    }

    private static string DescribeRegulatoryFacet(bool satisfied, bool hasEtmf, bool hasMilestone)
    {
        if (!satisfied) return "Pending IRB/EC and national regulatory approval";
        if (hasEtmf) return "Approved regulatory/IRB filing verified in eTMF";
        return hasMilestone ? "Regulatory/IRB milestone completed in CTMS" : "Regulatory & IRB approval verified";
    }

    private static string DescribeCtaFacet(bool satisfied, bool hasEtmf, bool hasMilestone)
    {
        if (!satisfied) return "Pending Clinical Trial Agreement & Budget execution";
        if (hasEtmf) return "Executed agreement filed in eTMF";
        return hasMilestone ? "CTA execution milestone completed in CTMS" : "Executed CTA & site agreement verified";
    }

    private static string DescribeEisfFacet(bool satisfied, bool execCompliant, bool zone5Baseline, bool completedSiv)
    {
        if (!satisfied) return "Pending eISF baseline document package";
        if (execCompliant) return "Site activation baseline verified in Execution & eTMF";
        if (zone5Baseline) return "Approved Zone 5 site baseline artifact filed in eTMF";
        return completedSiv ? "Site Initiation Visit completed" : "eISF baseline package verified";
    }

    private static string DescribeIpFacet(bool satisfied, bool hasMilestone, bool hasKits, bool siteActive)
    {
        if (!satisfied) return "Pending Investigational Product depot release";
        if (hasMilestone) return "IP depot release milestone completed in CTMS";
        if (hasKits) return "Available Investigational Product kit inventory verified at site";
        return siteActive ? "Site supply activation verified in Execution" : "Investigational product depot release verified";
    }

    private async Task<bool> TryHasSiteIpSupplyAsync(string siteCode, string studyCode, CancellationToken ct)
    {
        string? baseUrl = configuration["EXECUTION_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        string path = $"/api/v1/execution/rtsm/kits/inventory/{Uri.EscapeDataString(studyCode)}/{Uri.EscapeDataString(siteCode)}";
        var json = await SendSignedGetAsync(baseUrl, path, studyCode, siteCode, ct);
        if (json is not { ValueKind: JsonValueKind.Array })
        {
            return false;
        }

        foreach (var el in json.Value.EnumerateArray())
        {
            string status = el.TryGetProperty("status", out var sp) ? sp.GetString() ?? string.Empty : string.Empty;
            if (string.Equals(status, "ACTIVE_AT_SITE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "AVAILABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<(int Zone, string ArtifactCode, string ArtifactType, bool IsApproved)>> TryFetchEtmfDocumentsAsync(
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        string? baseUrl = configuration["ETMF_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        string path = $"/api/v1/etmf/documents?study_id={Uri.EscapeDataString(studyCode)}&site_id={Uri.EscapeDataString(siteCode)}";
        var json = await SendSignedGetAsync(baseUrl, path, studyCode, siteCode, ct);
        if (json is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var items = new List<(int, string, string, bool)>();
        foreach (var el in json.Value.EnumerateArray())
        {
            int zone = el.TryGetProperty("zone", out var zp) && zp.TryGetInt32(out int zv) ? zv : 0;
            string code = el.TryGetProperty("artifact_code", out var cp) ? cp.GetString() ?? string.Empty : string.Empty;
            string type = el.TryGetProperty("artifact_type", out var tp) ? tp.GetString() ?? string.Empty : string.Empty;
            string status = el.TryGetProperty("status", out var sp) ? sp.GetString() ?? string.Empty : string.Empty;
            bool approved = string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "FILED", StringComparison.OrdinalIgnoreCase);
            items.Add((zone, code, type, approved));
        }

        return items;
    }

    private async Task<List<(string Id, string Status)>?> TryFetchTrainingAssignmentsAsync(
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        string? baseUrl = configuration["KNOWLEDGE_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        string path = $"/api/v1/training/assignments?study_id={Uri.EscapeDataString(studyCode)}&site_id={Uri.EscapeDataString(siteCode)}";
        var json = await SendSignedGetAsync(baseUrl, path, studyCode, siteCode, ct);
        if (json is not { ValueKind: JsonValueKind.Array })
        {
            return null;
        }

        var items = new List<(string, string)>();
        foreach (var el in json.Value.EnumerateArray())
        {
            string id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            string status = el.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? string.Empty : string.Empty;
            items.Add((id, status));
        }

        return items;
    }

    private async Task<(bool IsCompliant, bool IsSiteActive)?> TryFetchExecutionComplianceAsync(
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        string? baseUrl = configuration["EXECUTION_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        string path = $"/api/v1/execution/sites/{Uri.EscapeDataString(siteCode)}/compliance-status?study_id={Uri.EscapeDataString(studyCode)}";
        var json = await SendSignedGetAsync(baseUrl, path, studyCode, siteCode, ct);
        if (json is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        bool compliant = (json.Value.TryGetProperty("is_complete", out var completeProp) && completeProp.ValueKind == JsonValueKind.True)
            || (json.Value.TryGetProperty("is_compliant", out var cp) && cp.ValueKind == JsonValueKind.True);
        bool active = (json.Value.TryGetProperty("is_activated", out var activatedProp) && activatedProp.ValueKind == JsonValueKind.True)
            || (json.Value.TryGetProperty("site_status", out var sp)
                && string.Equals(sp.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
        return (compliant, active);
    }

    private async Task<JsonElement?> SendSignedGetAsync(
        string rawBaseUrl,
        string relativePath,
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(rawBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        byte[]? secretBytes = ResolveGatewaySecretBytes();
        if (secretBytes is null || secretBytes.Length == 0)
        {
            return null;
        }

        Principal? ambient = httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string? tenantId = ambient?.TenantId;
        IReadOnlyList<string>? productFamilies = ResolveProductFamilies(ambient, tenantId);

        var identity = new GatewayIdentity(
            UserId: ServiceUserId,
            Roles: [PermissionMatrix.CtmsServiceRole],
            SiteIds: [siteCode],
            UnblindedAccess: false,
            TenantId: tenantId,
            ChangeReason: "CTMS startup greenlight live readiness evaluation",
            StudyIds: [studyCode],
            TenantReadOnly: ambient?.TenantReadOnly ?? false,
            ProductFamilies: productFamilies,
            TenantScoping: ambient?.TenantScoping);

        var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        foreach (var (k, v) in signedHeaders)
        {
            request.Headers.TryAddWithoutValidation(k, v);
        }

        try
        {
            var client = httpClientFactory.CreateClient("ctms-readiness");
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Live readiness check against {BaseUrl} failed; falling back to local CTMS state.", rawBaseUrl);
            return null;
        }
    }

    private static IReadOnlyList<string>? ResolveProductFamilies(Principal? ambient, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        return [.. (ambient?.ProductFamilies ?? [])
            .Concat(ReadinessServiceFamilies)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
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

    private static bool IsCompletedMilestone(CtmsSiteMilestone milestone) =>
        milestone.Status is CtmsSiteMilestoneStatus.Completed or CtmsSiteMilestoneStatus.Achieved
        || milestone.ActualDate.HasValue;

    private static bool MatchesAny(string? source, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(source)
        && tokens.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
}

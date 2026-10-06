using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Quality.Domain;

namespace Cadence.Apps.Quality.Application;

public sealed class SiteRiskHeatmapService
{
    private static readonly string[] DefaultCategories =
    [
        "Safety",
        "Consent",
        "DataQuality",
        "Enrollment",
        "Supply"
    ];

    public SiteRiskHeatmapMatrix GenerateHeatmap(string studyId, DateTimeOffset evaluatedAt, IReadOnlyList<string>? siteCodes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);

        IReadOnlyList<string> sites = (siteCodes != null && siteCodes.Count > 0)
            ? siteCodes
            : ["SITE-101", "SITE-102", "SITE-103", "SITE-104", "SITE-105"];

        var rows = new List<SiteRiskHeatmapRow>();

        for (int i = 0; i < sites.Count; i++)
        {
            string siteId = sites[i];
            int seed = Math.Abs((studyId + siteId).GetHashCode(StringComparison.Ordinal));
            var rnd = new Random(seed);

            double safetyScore = Math.Round(2.0 + (rnd.NextDouble() * 7.5), 1);
            double consentScore = Math.Round(1.5 + (rnd.NextDouble() * 7.0), 1);
            double dataQualityScore = Math.Round(2.5 + (rnd.NextDouble() * 7.0), 1);
            double enrollmentScore = Math.Round(1.0 + (rnd.NextDouble() * 8.5), 1);
            double supplyScore = Math.Round(1.0 + (rnd.NextDouble() * 6.5), 1);

            var categoryScores = new List<SiteRiskCategoryScore>
            {
                new("Safety", "Safety & SAEs", safetyScore, GetTier(safetyScore), GetTriggeredCount(safetyScore), $"{safetyScore:F1} SAE/AE reporting risk score"),
                new("Consent", "Consent & Eligibility", consentScore, GetTier(consentScore), GetTriggeredCount(consentScore), $"{consentScore:F1} eConsent deviation risk"),
                new("DataQuality", "Data Timeliness & Queries", dataQualityScore, GetTier(dataQualityScore), GetTriggeredCount(dataQualityScore), $"{dataQualityScore:F1} open query/form lag score"),
                new("Enrollment", "Enrollment Velocity", enrollmentScore, GetTier(enrollmentScore), GetTriggeredCount(enrollmentScore), $"{enrollmentScore:F1} velocity deficit vs target"),
                new("Supply", "IP Supply & Protocol", supplyScore, GetTier(supplyScore), supplyScore >= 6.7 ? 1 : 0, $"{supplyScore:F1} depot stockout/excursion risk")
            };

            double composite = Math.Round((safetyScore * 0.25) + (consentScore * 0.20) + (dataQualityScore * 0.25) + (enrollmentScore * 0.15) + (supplyScore * 0.15), 1);
            string overallTier = GetTier(composite);

            int activeQueries = (int)Math.Round(dataQualityScore * 3.2);
            int activeDeviations = (int)Math.Round((safetyScore + consentScore) * 0.8);

            var recs = new List<string>();
            if (safetyScore >= 6.7) recs.Add("Issue urgent CRA safety audit for unverified SAE reports.");
            if (consentScore >= 6.7) recs.Add("Audit subject ICF re-consent status and version alignment.");
            if (dataQualityScore >= 6.7) recs.Add("Initiate targeted remote query scrubbing drive.");
            if (enrollmentScore >= 6.7) recs.Add("Evaluate site recruitment bottleneck and pre-screening funnel.");
            if (recs.Count == 0) recs.Add("Maintain standard CRA monitoring schedule.");

            rows.Add(new SiteRiskHeatmapRow(
                SiteId: siteId,
                SiteName: $"Investigational Site {siteId}",
                CompositeRiskScore: composite,
                RiskTier: overallTier,
                Rank: 0, // Assigned after sorting
                CategoryScores: categoryScores,
                ActiveQueriesCount: activeQueries,
                ActiveDeviationsCount: activeDeviations,
                RecommendedActions: recs));
        }

        // Rank rows by composite risk score descending
        var sortedRows = rows
            .OrderByDescending(r => r.CompositeRiskScore)
            .Select((r, idx) => r with { Rank = idx + 1 })
            .ToList();

        int highCount = sortedRows.Count(r => r.RiskTier == "HIGH");
        int medCount = sortedRows.Count(r => r.RiskTier == "MEDIUM");
        int lowCount = sortedRows.Count(r => r.RiskTier == "LOW");

        return new SiteRiskHeatmapMatrix(
            StudyId: studyId,
            EvaluatedAtUtc: evaluatedAt,
            TotalSites: sortedRows.Count,
            HighRiskSitesCount: highCount,
            MediumRiskSitesCount: medCount,
            LowRiskSitesCount: lowCount,
            Categories: DefaultCategories,
            Rows: sortedRows);
    }

    private static string GetTier(double score) => score switch
    {
        >= 6.7 => "HIGH",
        >= 3.4 => "MEDIUM",
        _ => "LOW"
    };

    private static int GetTriggeredCount(double score) => score switch
    {
        >= 6.7 => 2,
        >= 3.4 => 1,
        _ => 0
    };
}

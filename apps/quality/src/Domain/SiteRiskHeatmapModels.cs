using System;
using System.Collections.Generic;

namespace Cadence.Apps.Quality.Domain;

public record SiteRiskCategoryScore(
    string CategoryKey,
    string CategoryName,
    double RiskScore,
    string RiskTier,
    int TriggeredMetricsCount,
    string Details);

public record SiteRiskHeatmapRow(
    string SiteId,
    string SiteName,
    double CompositeRiskScore,
    string RiskTier,
    int Rank,
    IReadOnlyList<SiteRiskCategoryScore> CategoryScores,
    int ActiveQueriesCount,
    int ActiveDeviationsCount,
    IReadOnlyList<string> RecommendedActions);

public record SiteRiskHeatmapMatrix(
    string StudyId,
    DateTimeOffset EvaluatedAtUtc,
    int TotalSites,
    int HighRiskSitesCount,
    int MediumRiskSitesCount,
    int LowRiskSitesCount,
    IReadOnlyList<string> Categories,
    IReadOnlyList<SiteRiskHeatmapRow> Rows);

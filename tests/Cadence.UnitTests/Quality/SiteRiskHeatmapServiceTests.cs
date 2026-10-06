using System;
using System.Linq;

using Cadence.Apps.Quality.Application;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Quality;

public sealed class SiteRiskHeatmapServiceTests
{
    [Fact]
    public void GenerateHeatmap_WithValidStudyId_GeneratesRankedMatrix()
    {
        // Arrange
        var service = new SiteRiskHeatmapService();
        var now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        string studyId = "STUDY-ONC-2026";
        var siteCodes = new[] { "SITE-101", "SITE-102", "SITE-103" };

        // Act
        var heatmap = service.GenerateHeatmap(studyId, now, siteCodes);

        // Assert
        heatmap.Should().NotBeNull();
        heatmap.StudyId.Should().Be("STUDY-ONC-2026");
        heatmap.TotalSites.Should().Be(3);
        heatmap.Categories.Should().Contain(["Safety", "Consent", "DataQuality", "Enrollment", "Supply"]);
        heatmap.Rows.Should().HaveCount(3);

        // Verify rows are ranked by CompositeRiskScore descending
        heatmap.Rows[0].Rank.Should().Be(1);
        heatmap.Rows[0].CompositeRiskScore.Should().BeGreaterThanOrEqualTo(heatmap.Rows[1].CompositeRiskScore);
        heatmap.Rows[1].Rank.Should().Be(2);

        // Verify each row has 5 category scores
        foreach (var row in heatmap.Rows)
        {
            row.CategoryScores.Should().HaveCount(5);
            row.RecommendedActions.Should().NotBeEmpty();
            row.RiskTier.Should().Match(t => t == "HIGH" || t == "MEDIUM" || t == "LOW");
        }
    }

    [Fact]
    public void GenerateHeatmap_WithEmptyStudyId_ThrowsArgumentException()
    {
        var service = new SiteRiskHeatmapService();
        Action act = () => service.GenerateHeatmap("", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        act.Should().Throw<ArgumentException>();
    }
}

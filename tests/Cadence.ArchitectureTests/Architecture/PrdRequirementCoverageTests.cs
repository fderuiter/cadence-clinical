using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces a shrink-only ratchet baseline over canonical <c>PRD-*</c> requirements that do not yet have an automated
/// backend or frontend test tag across <see cref="RtmGeneratorService.TestSearchDirs"/>, and rejects unknown or
/// misapplied requirement tags across C#, Vitest, and Playwright suites.
/// </summary>
public sealed class PrdRequirementCoverageTests
{
    public const string BaselinePath = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/prd-requirement-coverage.baseline.txt";

    private static readonly string[] HeaderLines =
    [
        "# Canonical PRD-* requirements with no automated backend or frontend test tag yet.",
        "# One requirement ID per line, sorted ordinally. This baseline may only shrink:",
        "# tag a test that proves a listed requirement ([GxPRequirement('PRD-...')] or @req:PRD-...) and run",
        "# CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter FullyQualifiedName~PrdRequirementCoverageTests.",
    ];

    [Fact(DisplayName = "EveryCanonicalPrdRequirement_HasATaggedTest_OrIsInTheShrinkOnlyBaseline")]
    [Trait("Category", "Architecture")]
    public void EveryCanonicalPrdRequirementHasATaggedTestOrIsInTheShrinkOnlyBaseline()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        var requirements = RtmGeneratorService.LoadRequirements(repoRoot);
        var scan = RequirementTagScanner.ScanDirectoriesWithViolations(repoRoot, RtmGeneratorService.TestSearchDirs);

        scan.Violations.Should().BeEmpty(
            "misapplied file-level, suite-level, class-level, or restricted requirement tags must never fabricate coverage");

        var knownIds = requirements.Keys.ToHashSet(StringComparer.Ordinal);
        var unknownTags = scan.Tests
            .SelectMany(t => t.RequirementIds.Where(id => !knownIds.Contains(id)).Select(id => $"{t.File}:{t.Line} ({id})"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        unknownTags.Should().BeEmpty("every backend and frontend requirement tag must reference a defined requirement ID");

        var coveredIds = scan.Tests
            .SelectMany(t => t.RequirementIds)
            .ToHashSet(StringComparer.Ordinal);

        var unmappedPrdIds = requirements.Values
            .Where(r => !r.IsLegacyAlias && r.Id.StartsWith("PRD-", StringComparison.Ordinal) && !coveredIds.Contains(r.Id))
            .Select(r => r.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var comparison = ShrinkOnlyBaseline.Verify(BaselinePath, HeaderLines, unmappedPrdIds);
        comparison.IsClean.Should().BeTrue(
            comparison.Describe(
                "PrdRequirementCoverage",
                nameof(PrdRequirementCoverageTests),
                "Tag a test whose assertions prove the new PRD-* requirement using [GxPRequirement] or @req:PRD-... (see docs/TESTING.md)."));

        var recorded = ShrinkOnlyBaseline.Read(BaselinePath);
        recorded.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "ShrinkOnlyPrdBaseline_RejectsNewUncoveredRequirements_AndFlagsNewlyCoveredStaleEntries")]
    [Trait("Category", "Architecture")]
    public void ShrinkOnlyPrdBaselineRejectsNewUncoveredRequirementsAndFlagsNewlyCoveredStaleEntries()
    {
        IReadOnlyList<string> baseline = ["PRD-DEMO-002", "PRD-DEMO-003"];
        IReadOnlyList<string> actual = ["PRD-DEMO-003", "PRD-DEMO-004"];

        var comparison = ShrinkOnlyBaseline.Compare(BaselinePath, actual, baseline);
        comparison.IsClean.Should().BeFalse();
        comparison.NewViolations.Should().Equal("PRD-DEMO-004");
        comparison.StaleEntries.Should().Equal("PRD-DEMO-002");
    }
}

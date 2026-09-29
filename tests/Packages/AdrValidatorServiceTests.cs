using System;
using System.IO;

using Cadence.Testing;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// ADR integrity checks run by <c>cadence check adrs</c>: unique numbers, <c># ADR-NNNN:</c> headings,
/// and an index that lists every ADR exactly once with no broken links.
/// </summary>
public sealed class AdrValidatorServiceTests : IDisposable
{
    private readonly string _adrDir = Path.Combine(Path.GetTempPath(), $"adr-validator-{Guid.NewGuid():N}");

    public AdrValidatorServiceTests()
    {
        Directory.CreateDirectory(_adrDir);
        File.WriteAllText(Path.Combine(_adrDir, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_adrDir))
        {
            Directory.Delete(_adrDir, recursive: true);
        }
    }

    private void WriteAdr(string fileName, string heading) =>
        File.WriteAllText(Path.Combine(_adrDir, fileName), $"{heading}\n\n- **Status:** Accepted\n");

    private void WriteIndex(params string[] lines) =>
        File.WriteAllText(Path.Combine(_adrDir, "index.md"), string.Join("\n", lines) + "\n");

    /// <summary>
    /// A directory whose ADRs are uniquely numbered and fully indexed has no integrity errors.
    /// @req:PRD-SYS-049
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    public void FindIntegrityErrors_ConsistentDirectory_ReturnsNoErrors()
    {
        WriteAdr("2026-01-01-first.md", "# ADR-049: First");
        WriteAdr("2026-01-02-second.md", "# ADR-2250: Second");
        WriteIndex(
            "# Index",
            "",
            "- [ADR Template](TEMPLATE.md)",
            "",
            "### 1. Domain",
            "",
            "- [ADR-049: First](2026-01-01-first.md) (2026-01-01)",
            "- [ADR-2250: Second](2026-01-02-second.md) (2026-01-02)");

        AdrValidatorService.FindIntegrityErrors(_adrDir).Should().BeEmpty();
    }

    /// <summary>
    /// Two ADRs carrying the same number (including zero-padded variants) are reported as a duplicate.
    /// @req:PRD-SYS-049
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    public void FindIntegrityErrors_DuplicateNumber_ReportsBothFiles()
    {
        WriteAdr("2026-01-01-first.md", "# ADR-085: First");
        WriteAdr("2026-01-02-second.md", "# ADR-0085: Second");
        WriteIndex(
            "- [ADR-085: First](2026-01-01-first.md)",
            "- [ADR-0085: Second](2026-01-02-second.md)");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().ContainSingle()
            .Which.Should().Contain("ADR-085").And.Contain("2026-01-01-first.md").And.Contain("2026-01-02-second.md");
    }

    /// <summary>
    /// An ADR whose first line is not a numbered "# ADR-NNNN: Title" heading is rejected.
    /// @req:PRD-SYS-049
    /// </summary>
    [Theory]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    [InlineData("# ADR 2026-07-22: Date-Based Title")]
    [InlineData("# ADR-[NUMBER]: Unnumbered Placeholder")]
    [InlineData("# ADR-2026-07-24-slug: Date Used As Number")]
    [InlineData("# ADR-12: Too Few Digits")]
    [InlineData("Plain first line")]
    public void FindIntegrityErrors_MissingAdrHeading_IsReported(string heading)
    {
        WriteAdr("2026-01-01-first.md", heading);
        WriteIndex("- [First](2026-01-01-first.md)");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().ContainSingle()
            .Which.Should().Contain("2026-01-01-first.md").And.Contain("heading");
    }

    /// <summary>
    /// Index links to files that do not exist are reported with their line number.
    /// @req:PRD-SYS-049
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    public void FindIntegrityErrors_BrokenIndexLink_IsReported()
    {
        WriteAdr("2026-01-01-first.md", "# ADR-100: First");
        WriteIndex(
            "See the [spec](../kernel/MISSING_SPEC.md).",
            "- [ADR-100: First](2026-01-01-first.md)",
            "- [ADR-101: Gone](2026-01-02-gone.md)");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("line 1") && e.Contains("../kernel/MISSING_SPEC.md"));
        errors.Should().Contain(e => e.Contains("line 3") && e.Contains("2026-01-02-gone.md"));
    }

    /// <summary>
    /// An ADR missing from the index, or listed more than once, is reported.
    /// @req:PRD-SYS-049
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    public void FindIntegrityErrors_UnlistedAndDoublyListedAdrs_AreReported()
    {
        WriteAdr("2026-01-01-first.md", "# ADR-100: First");
        WriteAdr("2026-01-02-second.md", "# ADR-101: Second");
        WriteIndex(
            "### A",
            "- [ADR-100: First](2026-01-01-first.md)",
            "### B",
            "- [ADR-100: First](2026-01-01-first.md)");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("2026-01-01-first.md") && e.Contains("listed 2 times"));
        errors.Should().Contain(e => e.Contains("2026-01-02-second.md") && e.Contains("missing from index"));
    }

    /// <summary>
    /// Heading parsing accepts three-or-more digit numbers and rejects everything else.
    /// @req:PRD-SYS-049
    /// </summary>
    [Theory]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    [InlineData("# ADR-049: Title", 49)]
    [InlineData("# ADR-2250: Title\r", 2250)]
    [InlineData("# ADR-20260929: Title", 20260929)]
    [InlineData("# ADR-49: Title", null)]
    [InlineData("## ADR-2250: Title", null)]
    [InlineData("# ADR-2250 Title", null)]
    public void ParseAdrNumber_ParsesOnlyNumberedHeadings(string line, int? expected)
    {
        AdrValidatorService.ParseAdrNumber(line).Should().Be(expected);
    }

    /// <summary>
    /// The repository's own docs/adr directory satisfies every integrity rule.
    /// @req:PRD-SYS-049
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    [GxPRequirement("PRD-SYS-049")]
    public void FindIntegrityErrors_RepositoryAdrDirectory_IsConsistent()
    {
        string repoAdrDir = Path.Combine(AdrValidatorService.FindRepoRoot(), "docs", "adr");

        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().BeEmpty();
    }
}

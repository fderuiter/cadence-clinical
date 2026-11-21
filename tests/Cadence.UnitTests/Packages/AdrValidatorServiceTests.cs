using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        if (!Directory.Exists(_adrDir))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_adrDir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_adrDir, recursive: true);
    }

    private void WriteAdr(string fileName, string heading) =>
        File.WriteAllText(Path.Combine(_adrDir, fileName), $"{heading}\n\n- **Status:** Accepted\n");

    private void WriteIndex(params string[] lines) =>
        File.WriteAllText(Path.Combine(_adrDir, "index.md"), string.Join("\n", lines) + "\n");

    /// <summary>
    /// A directory whose ADRs are uniquely numbered and fully indexed has no integrity errors.
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Theory]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Theory]
    [Trait("Category", "Tooling")]
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
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    public void FindIntegrityErrors_RepositoryAdrDirectory_IsConsistent()
    {
        string repoAdrDir = Path.Combine(AdrValidatorService.FindRepoRoot(), "docs", "adr");

        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void ValidateAdrs_RejectsCommittedGatewayAndPackageJsonDependencyChangesWithoutAdr_AndAdmitsPreCommitWhenBranchAddedAdr()
    {
        string repo = Path.Combine(_adrDir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "docs", "adr"));
        Directory.CreateDirectory(Path.Combine(repo, "docs", "SDLC"));
        Directory.CreateDirectory(Path.Combine(repo, "apps", "gateway", "src"));

        File.WriteAllText(Path.Combine(repo, "docs", "SDLC", "prd.md"), "#### PRD-SYS-001: Gateway routing\n");
        File.WriteAllText(Path.Combine(repo, "docs", "adr", "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "2025-01-01-initial.md"),
            "# ADR-0100: Initial\n\n## Status\nAccepted\n\n## Context\nBaseline.\n\n## Decision\nKeep.\n\n## Alternatives Considered\nNone.\n\n## Trade-offs\nNone.\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "index.md"),
            "# ADR Index\n\n- [ADR-0100: Initial](2025-01-01-initial.md)\n");
        File.WriteAllText(Path.Combine(repo, "apps", "gateway", "src", "Program.cs"), "class Program {}\n");
        File.WriteAllText(
            Path.Combine(repo, "package.json"),
            "{\n  \"name\": \"cadence\",\n  \"scripts\": { \"test\": \"vitest\" },\n  \"devDependencies\": { \"vitest\": \"3.0.0\" }\n}\n");

        RunGit(repo, "init", "-q", "-b", "main");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "baseline");
        RunGit(repo, "update-ref", "refs/remotes/origin/main", "HEAD");
        RunGit(repo, "checkout", "-q", "-b", "feature");

        // Committed script-only change in package.json needs no ADR.
        File.WriteAllText(
            Path.Combine(repo, "package.json"),
            "{\n  \"name\": \"cadence\",\n  \"scripts\": { \"test\": \"vitest run\" },\n  \"devDependencies\": { \"vitest\": \"3.0.0\" }\n}\n");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "chore: update test script");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeTrue();

        // Committed dependency change in package.json against compareBase fails without an ADR.
        File.WriteAllText(
            Path.Combine(repo, "package.json"),
            "{\n  \"name\": \"cadence\",\n  \"scripts\": { \"test\": \"vitest run\" },\n  \"devDependencies\": { \"vitest\": \"4.0.0\" }\n}\n");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "build: bump vitest major");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeFalse();

        // Adding an ADR on the branch satisfies both the branch check and subsequent --pre-commit architectural edits!
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "2026-11-20-gateway-and-toolchain.md"),
            "# ADR-0101: Gateway and toolchain\n\n## Status\nAccepted\n\n## Context\nImplements PRD-SYS-001.\n\n## Decision\nUpgrade gateway and toolchain.\n\n## Alternatives Considered\nManual scripting.\n\n## Trade-offs\nValidated in CI.\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "index.md"),
            "# ADR Index\n\n- [ADR-0100: Initial](2025-01-01-initial.md)\n- [ADR-0101: Gateway and toolchain](2026-11-20-gateway-and-toolchain.md)\n");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "docs: add ADR-0101");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeTrue();

        // Follow-up staged gateway edit on the same branch passes --pre-commit because ADR-0101 was already added in mergeBase..HEAD.
        File.WriteAllText(Path.Combine(repo, "apps", "gateway", "src", "Program.cs"), "class Program { static void Main() {} }\n");
        RunGit(repo, "add", "apps/gateway/src/Program.cs");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo, preCommit: true).Should().BeTrue();
    }

    private static void RunGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new List<string> { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, err);
    }
}

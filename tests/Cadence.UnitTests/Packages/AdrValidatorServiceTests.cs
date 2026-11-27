using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Commands;
using Cadence.Testing.Time;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// ADR integrity and domain-sharded scaffolding checks run by <c>cadence check adrs</c> and
/// <c>cadence scaffold adr --domain &lt;slug&gt;</c>.
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
        errors.Should().Contain(e => e.Contains("2026-01-02-second.md") && e.Contains("missing from"));
    }

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

    [Fact]
    [Trait("Category", "Tooling")]
    public void FindIntegrityErrors_RepositoryAdrDirectory_IsConsistent()
    {
        string repoAdrDir = Path.Combine(AdrValidatorService.FindRepoRoot(), "docs", "adr");

        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void ScaffoldAdr_WritesDomainShardedAdrUsingInjectedClock_SortsShardAlphabetically_AndKeepsHubStatic()
    {
        string repo = Path.Combine(_adrDir, "scaffold-repo");
        string adrDir = Path.Combine(repo, "docs", "adr");
        Directory.CreateDirectory(adrDir);

        string hubContent = "# Architectural Decision Records (Index)\n\nStatic hub.\n";
        string legacyContent = "# Frozen Legacy ADRs\n\n### 2. API Gateway, Security & Identity\n\n- [ADR-0100: Initial](2025-01-01-initial.md)\n";
        File.WriteAllText(Path.Combine(adrDir, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(Path.Combine(adrDir, "index.md"), hubContent);
        File.WriteAllText(Path.Combine(adrDir, "legacy-index.md"), legacyContent);
        File.WriteAllText(Path.Combine(adrDir, "2025-01-01-initial.md"), "# ADR-0100: Initial\n\n- **Status:** Accepted\n");

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 27, 15, 30, 0, TimeSpan.Zero));

        // Unknown domain fails with exit code 1.
        ScaffoldCommands.ScaffoldAdr("Some Decision", "unknown-domain", repoRoot: repo, clock: clock)
            .Should().Be(1);

        // Scaffold two security ADRs in reverse alphabetical order ("Zeta" then "Alpha") and one frontend ADR.
        ScaffoldCommands.ScaffoldAdr("Zeta Token Rotation", "security", repoRoot: repo, clock: clock)
            .Should().Be(0);
        ScaffoldCommands.ScaffoldAdr("Alpha mTLS Binding", "security", repoRoot: repo, clock: clock)
            .Should().Be(0);
        ScaffoldCommands.ScaffoldAdr("Clinical Table Virtualization", "frontend", repoRoot: repo, clock: clock)
            .Should().Be(0);

        // Duplicate filename fails closed without overwriting.
        ScaffoldCommands.ScaffoldAdr("Alpha mTLS Binding", "security", repoRoot: repo, clock: clock)
            .Should().Be(1);

        string alphaFile = Path.Combine(adrDir, "security", "2026-11-27-alpha-mtls-binding.md");
        string zetaFile = Path.Combine(adrDir, "security", "2026-11-27-zeta-token-rotation.md");
        string frontendFile = Path.Combine(adrDir, "frontend", "2026-11-27-clinical-table-virtualization.md");

        File.ReadAllText(alphaFile).Should().StartWith("# ADR-SECURITY-20261127-alpha-mtls-binding: Alpha mTLS Binding\n");
        File.ReadAllText(alphaFile).Should().Contain("- **Date:** 2026-11-27");
        File.ReadAllText(zetaFile).Should().StartWith("# ADR-SECURITY-20261127-zeta-token-rotation: Zeta Token Rotation\n");
        File.ReadAllText(frontendFile).Should().StartWith("# ADR-FRONTEND-20261127-clinical-table-virtualization: Clinical Table Virtualization\n");

        // Security shard index must have Alpha before Zeta even though Zeta was scaffolded first.
        string securityIndex = File.ReadAllText(Path.Combine(adrDir, "security", "index.md"));
        int alphaIdx = securityIndex.IndexOf("2026-11-27-alpha-mtls-binding.md", StringComparison.Ordinal);
        int zetaIdx = securityIndex.IndexOf("2026-11-27-zeta-token-rotation.md", StringComparison.Ordinal);
        alphaIdx.Should().BeGreaterThanOrEqualTo(0);
        zetaIdx.Should().BeGreaterThan(alphaIdx);

        // Hub index.md and legacy-index.md must remain 100% untouched.
        File.ReadAllText(Path.Combine(adrDir, "index.md")).Should().Be(hubContent);
        File.ReadAllText(Path.Combine(adrDir, "legacy-index.md")).Should().Be(legacyContent);

        AdrValidatorService.FindIntegrityErrors(adrDir).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void FindIntegrityErrors_DomainShards_EnforcesHeaderDomainDateSlugUniqueIdsAndAlphabeticalOrder()
    {
        string repoAdr = Path.Combine(_adrDir, "domain-errors");
        string secDir = Path.Combine(repoAdr, "security");
        Directory.CreateDirectory(secDir);

        File.WriteAllText(Path.Combine(repoAdr, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(Path.Combine(repoAdr, "index.md"), "# ADR Hub\n");
        File.WriteAllText(Path.Combine(repoAdr, "legacy-index.md"), "# Legacy\n");

        // Out-of-order entries in security/index.md + mismatched domain header on one file.
        File.WriteAllText(
            Path.Combine(secDir, "2026-11-27-zeta-policy.md"),
            "# ADR-SECURITY-20261127-zeta-policy: Zeta Policy\n\n- **Status:** Accepted\n");
        File.WriteAllText(
            Path.Combine(secDir, "2026-11-27-alpha-policy.md"),
            "# ADR-FRONTEND-20261127-alpha-policy: Wrong Domain Prefix\n\n- **Status:** Accepted\n");
        File.WriteAllText(
            Path.Combine(secDir, "index.md"),
            string.Join(
                '\n',
                "# Security ADRs",
                "",
                "## Decisions",
                "",
                "- [Zeta](2026-11-27-zeta-policy.md)",
                "- [Alpha](2026-11-27-alpha-policy.md)",
                ""));

        var errors = AdrValidatorService.FindIntegrityErrors(repoAdr);
        errors.Should().Contain(e => e.Contains("security/2026-11-27-alpha-policy.md") && e.Contains("does not match directory domain 'SECURITY'"));
        errors.Should().Contain(e => e.Contains("security/index.md") && e.Contains("not sorted alphabetically by filename"));
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void ValidateAdrs_RejectsCommittedGatewayAndPackageJsonDependencyChangesWithoutAdr_AndAdmitsDomainShardedAdr()
    {
        string repo = Path.Combine(_adrDir, "branch-repo");
        Directory.CreateDirectory(Path.Combine(repo, "docs", "adr"));
        Directory.CreateDirectory(Path.Combine(repo, "docs", "SDLC"));
        Directory.CreateDirectory(Path.Combine(repo, "apps", "gateway", "src"));

        File.WriteAllText(Path.Combine(repo, "docs", "SDLC", "prd.md"), "#### PRD-SYS-001: Gateway routing\n");
        File.WriteAllText(Path.Combine(repo, "docs", "adr", "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "2025-01-01-initial.md"),
            "# ADR-0100: Initial\n\n## Status\nAccepted\n\n## Context\nBaseline.\n\n## Decision\nKeep.\n\n## Alternatives Considered\nNone.\n\n## Trade-offs\nNone.\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "legacy-index.md"),
            "# Legacy ADRs\n\n- [ADR-0100: Initial](2025-01-01-initial.md)\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "index.md"),
            "# ADR Hub\n\n- [Legacy](legacy-index.md)\n");
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

        // Scaffolding a domain-sharded ADR satisfies both branch validation and subsequent --pre-commit edits.
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        ScaffoldCommands.ScaffoldAdr("Gateway and toolchain", "tooling", repoRoot: repo, clock: clock).Should().Be(0);
        string adrPath = Path.Combine(repo, "docs", "adr", "tooling", "2026-11-20-gateway-and-toolchain.md");
        string scaffolded = File.ReadAllText(adrPath).Replace("PRD-SYS-001", "PRD-SYS-001", StringComparison.Ordinal) + "\nImplements PRD-SYS-001.\n";
        File.WriteAllText(adrPath, scaffolded);
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "index.md"),
            "# ADR Hub\n\n- [Legacy](legacy-index.md)\n- [Tooling](tooling/index.md)\n");

        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "docs: add domain ADR");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeTrue();

        File.WriteAllText(Path.Combine(repo, "apps", "gateway", "src", "Program.cs"), "class Program { static void Main() {} }\n");
        RunGit(repo, "add", "apps/gateway/src/Program.cs");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo, preCommit: true).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "Tooling")]
    [InlineData("apps/web/src/components/DesignerSchemaPanel.vue", false)]
    [InlineData("apps/web/src/models/StudySchemaModel.ts", false)]
    [InlineData("apps/subject-portal/src/views/DatabaseConsentView.vue", false)]
    [InlineData("apps/edc/src/ui/FormModel.tsx", false)]
    [InlineData("packages/kernel/README.md", false)]
    [InlineData("apps/gateway/src/Program.cs", true)]
    [InlineData("packages/security/src/TokenValidator.cs", true)]
    [InlineData("packages/ui/src/components/Button.vue", true)]
    [InlineData("apps/ctms/src/Domain/StudySchema.cs", true)]
    [InlineData("apps/edc/src/Infrastructure/DatabaseMigration.cs", true)]
    public void IsArchitecturalFile_ExcludesFrontendAppsAndExtensions_WhileFlaggingBackendAndUiPackage(string relativePath, bool expected)
    {
        AdrValidatorService.IsArchitecturalFile(relativePath, _adrDir).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void SyncAdrIndex_InsertsUnlistedDomainAdrsSortedAlphabetically_WithoutTouchingHubOrLegacyIndex()
    {
        string repoRoot = Path.Combine(_adrDir, "sync-repo");
        string repoAdrDir = Path.Combine(repoRoot, "docs", "adr");
        string secDir = Path.Combine(repoAdrDir, "security");
        Directory.CreateDirectory(secDir);

        string hubContent = "# Hub\n\n- [ADR Template](TEMPLATE.md)\n- [Legacy](legacy-index.md)\n- [Security](security/index.md)\n";
        string legacyContent = "# Legacy\n\n- [ADR-100: First](2026-01-01-first.md) (2026-01-01)\n";

        File.WriteAllText(Path.Combine(repoAdrDir, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(Path.Combine(repoAdrDir, "2026-01-01-first.md"), "# ADR-100: First\n\n## Status\nAccepted\n");
        File.WriteAllText(Path.Combine(repoAdrDir, "index.md"), hubContent);
        File.WriteAllText(Path.Combine(repoAdrDir, "legacy-index.md"), legacyContent);

        File.WriteAllText(
            Path.Combine(secDir, "2026-11-20-zeta.md"),
            "# ADR-SECURITY-20261120-zeta: Zeta\n\n- **Status:** Accepted\n");
        File.WriteAllText(
            Path.Combine(secDir, "2026-11-19-alpha.md"),
            "# ADR-SECURITY-20261119-alpha: Alpha\n\n- **Status:** Accepted\n");
        File.WriteAllText(
            Path.Combine(secDir, "index.md"),
            "# Security ADRs\n\n## Decisions\n\n- [ADR-SECURITY-20261120-zeta: Zeta](2026-11-20-zeta.md) (2026-11-20)\n");

        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().ContainSingle();
        AdrValidatorService.SyncAdrIndex(repoRoot).Should().Be(0);
        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().BeEmpty();

        string syncedShard = File.ReadAllText(Path.Combine(secDir, "index.md"));
        syncedShard.IndexOf("2026-11-19-alpha.md", StringComparison.Ordinal)
            .Should().BeLessThan(syncedShard.IndexOf("2026-11-20-zeta.md", StringComparison.Ordinal));

        File.ReadAllText(Path.Combine(repoAdrDir, "index.md")).Should().Be(hubContent);
        File.ReadAllText(Path.Combine(repoAdrDir, "legacy-index.md")).Should().Be(legacyContent);

        AdrValidatorService.SyncAdrIndex(repoRoot).Should().Be(0);
        File.ReadAllText(Path.Combine(secDir, "index.md")).Should().Be(syncedShard);
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void ValidateAdrs_InPreCommitMode_InspectsStagedIndexOnly_IgnoringUnstagedArchitecturalEdits()
    {
        string repoRoot = Path.Combine(_adrDir, "repo");
        string repoAdrDir = Path.Combine(repoRoot, "docs", "adr");
        Directory.CreateDirectory(repoAdrDir);
        File.WriteAllText(Path.Combine(repoAdrDir, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(
            Path.Combine(repoAdrDir, "2025-12-01-init.md"),
            "# ADR-100: Init\n\n## Status\nAccepted\n\n## Context\nInit.\n\n## Decision\nInit.\n\n## Alternatives Considered\nNone.\n\n## Trade-offs\nNone.\n");
        File.WriteAllText(
            Path.Combine(repoAdrDir, "index.md"),
            "# Index\n\n- [ADR Template](TEMPLATE.md)\n- [ADR-100: Init](2025-12-01-init.md) (2025-12-01)\n");
        File.WriteAllText(
            Path.Combine(repoRoot, "package.json"),
            "{\n  \"name\": \"fixture\",\n  \"dependencies\": { \"vue\": \"^3.5.0\" }\n}\n");
        Directory.CreateDirectory(Path.Combine(repoRoot, "apps", "gateway", "src"));
        File.WriteAllText(Path.Combine(repoRoot, "apps", "gateway", "src", "Program.cs"), "class Program { }\n");

        RunGit(repoRoot, "init", "-q", "-b", "main");
        RunGit(repoRoot, "add", "-A");
        RunGit(repoRoot, "commit", "-q", "-m", "init");
        RunGit(repoRoot, "update-ref", "refs/remotes/origin/main", "HEAD");
        RunGit(repoRoot, "checkout", "-q", "-b", "feature");

        File.WriteAllText(Path.Combine(repoRoot, "docs", "note.md"), "# Note\n");
        RunGit(repoRoot, "add", "docs/note.md");

        File.WriteAllText(Path.Combine(repoRoot, "apps", "gateway", "src", "Program.cs"), "class Program { static void Main() { } }\n");
        File.WriteAllText(
            Path.Combine(repoRoot, "package.json"),
            "{\n  \"name\": \"fixture\",\n  \"dependencies\": { \"vue\": \"^3.5.0\", \"axios\": \"^1.7.0\" }\n}\n");

        AdrValidatorService.ValidateAdrs(repoRoot: repoRoot, preCommit: true).Should().BeTrue(
            "pre-commit mode must inspect staged index changes only and ignore unstaged working-tree edits");
        AdrValidatorService.ValidateAdrs(repoRoot: repoRoot, preCommit: false).Should().BeFalse(
            "full validation still flags unstaged architectural changes in the working tree");
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
        _ = process.StandardOutput.ReadToEnd();
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, err);
    }
}

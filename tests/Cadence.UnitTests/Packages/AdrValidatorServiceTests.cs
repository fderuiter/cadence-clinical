using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Commands;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// ADR integrity checks run by <c>cadence check adrs</c>: unique numbers, <c># ADR-NNNN:</c> headings,
/// domain metadata, and unbroken relative links across <c>docs/adr/*.md</c>.
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

    private void WriteAdr(string fileName, string heading, string extraMetadata = "") =>
        File.WriteAllText(Path.Combine(_adrDir, fileName), $"{heading}\n\n- **Status:** Accepted\n{extraMetadata}");

    private void WriteIndex(params string[] lines) =>
        File.WriteAllText(Path.Combine(_adrDir, "index.md"), string.Join("\n", lines) + "\n");

    /// <summary>
    /// A directory whose ADRs are uniquely numbered with valid links has no integrity errors, even without per-ADR index entries.
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
            "<AdrIndex />");

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
        WriteIndex("<AdrIndex />");

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
        WriteIndex("<AdrIndex />");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().ContainSingle()
            .Which.Should().Contain("2026-01-01-first.md").And.Contain("heading");
    }

    /// <summary>
    /// Broken relative links in index.md and individual ADR files are reported with their line numbers, while fenced code blocks are ignored.
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    public void FindIntegrityErrors_BrokenRelativeLinksInIndexAndAdrFiles_AreReported()
    {
        File.WriteAllText(
            Path.Combine(_adrDir, "2026-01-01-first.md"),
            "# ADR-100: First\n\n- **Status:** Accepted\n\nSee [Broken Peer](2026-01-02-gone.md) and `[Example](ignored-inline.md)`.\n\n```markdown\nSee [Code Example](ignored-fenced.md)\n```\n");
        WriteIndex(
            "See the [spec](../kernel/MISSING_SPEC.md).",
            "<AdrIndex />");

        var errors = AdrValidatorService.FindIntegrityErrors(_adrDir);

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("index.md line 1") && e.Contains("../kernel/MISSING_SPEC.md"));
        errors.Should().Contain(e => e.Contains("2026-01-01-first.md line 5") && e.Contains("2026-01-02-gone.md"));
    }

    /// <summary>
    /// Unlisted ADRs pass integrity checks without modifying index.md, and domain metadata is validated on deterministic-range ADRs.
    /// </summary>
    [Fact]
    [Trait("Category", "Tooling")]
    public void FindIntegrityErrors_ValidatesDomainMetadata_AndSyncAdrIndexLeavesLandingPageUntouched()
    {
        string repoRoot = Path.Combine(_adrDir, "domain-repo");
        string repoAdrDir = Path.Combine(repoRoot, "docs", "adr");
        Directory.CreateDirectory(repoAdrDir);
        File.WriteAllText(Path.Combine(repoAdrDir, "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n");
        File.WriteAllText(
            Path.Combine(repoAdrDir, "index.md"),
            "# Architecture Decision Records\n\n- [ADR Template](TEMPLATE.md)\n\n<AdrIndex />\n");
        string initialIndex = File.ReadAllText(Path.Combine(repoAdrDir, "index.md"));

        // Valid deterministic-range ADR with canonical domain passes without appearing in index.md.
        File.WriteAllText(
            Path.Combine(repoAdrDir, "2026-11-23-valid.md"),
            "# ADR-42105: Valid Deterministic ADR\n\n- **Status:** Accepted\n- **Domain:** 7. DevOps, Tooling & CI/CD\n");

        AdrValidatorService.FindIntegrityErrors(repoAdrDir).Should().BeEmpty();
        AdrValidatorService.SyncAdrIndex(repoRoot).Should().Be(0);
        File.ReadAllText(Path.Combine(repoAdrDir, "index.md")).Should().Be(initialIndex);

        // Missing Domain on deterministic-range ADR (>= 3000) and invalid Domain value are reported.
        File.WriteAllText(
            Path.Combine(repoAdrDir, "2026-11-23-missing-domain.md"),
            "# ADR-42106: Missing Domain\n\n- **Status:** Accepted\n");
        File.WriteAllText(
            Path.Combine(repoAdrDir, "2026-11-23-invalid-domain.md"),
            "# ADR-42107: Invalid Domain\n\n- **Status:** Accepted\n- **Domain:** Unknown Bucket\n");

        var errors = AdrValidatorService.FindIntegrityErrors(repoAdrDir);
        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("2026-11-23-missing-domain.md") && e.Contains("- **Domain:**"));
        errors.Should().Contain(e => e.Contains("2026-11-23-invalid-domain.md") && e.Contains("invalid '- **Domain:**'"));
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
            Path.Combine(repo, "docs", "adr", "index.md"),
            "# ADR Index\n\n<AdrIndex />\n");
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

        // Adding a standalone ADR file on the branch (without touching docs/adr/index.md) satisfies both branch and pre-commit checks.
        File.WriteAllText(
            Path.Combine(repo, "docs", "adr", "2026-11-20-gateway-and-toolchain.md"),
            "# ADR-0101: Gateway and toolchain\n\n## Status\nAccepted\n\n## Context\nImplements @req:PRD-SYS-001.\n\n## Decision\nUpgrade gateway and toolchain.\n\n## Domain Impact\nGateway routing.\n\n## Alternatives Considered\nManual scripting.\n\n## Trade-offs\nValidated in CI.\n");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "docs: add ADR-0101");
        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeTrue();

        // Follow-up staged gateway edit on the same branch passes --pre-commit because ADR-0101 was already added in mergeBase..HEAD.
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
    [InlineData("packages/ui/src/components/Button.vue", false)]
    [InlineData("packages/ui/tokens.css", false)]
    [InlineData("apps/ctms/src/Domain/StudySchema.cs", false)]
    [InlineData("apps/intelligence/src/Infrastructure/Models/IntelligenceOptions.cs", false)]
    [InlineData("apps/execution/src/Endpoints/CallerIdentity.cs", false)]
    [InlineData("packages/tooling_core/src/Services/MigrationToolingService.cs", false)]
    [InlineData("packages/ui/package.json", true)]
    [InlineData("packages/ui/index.js", true)]
    [InlineData("packages/ui/index.d.ts", true)]
    [InlineData("apps/gateway/src/Program.cs", true)]
    [InlineData("packages/security/src/TokenValidator.cs", true)]
    [InlineData("packages/database/src/CadenceAuditSaveChangesInterceptor.cs", true)]
    [InlineData("apps/ctms/src/Infrastructure/Persistence/CtmsDbContext.cs", true)]
    [InlineData("apps/ctms/src/Infrastructure/Persistence/Migrations/20260924144609_InitialCreate.cs", true)]
    [InlineData("packages/kernel/src/Cadence.Kernel.Persistence/Configurations/ObservationConfigurations.cs", true)]
    [InlineData("packages/kernel/src/Cadence.Kernel.Persistence/Standards/StandardsConfigurations.cs", true)]
    [InlineData("apps/ctms/src/Domain/CtmsEntities.cs", true)]
    [InlineData("apps/econsent/src/Domain/ConsentEntity.cs", true)]
    [InlineData("apps/ctms/src/Domain/Entities/Study.cs", true)]
    [InlineData("apps/edc/src/Infrastructure/DatabaseMigration.cs", true)]
    public void IsArchitecturalFile_UsesAnchoredPersistenceAndUiPublicSurfaceRules(string relativePath, bool expected)
    {
        AdrValidatorService.IsArchitecturalFile(relativePath, _adrDir).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void CreateAdrService_GeneratesDeterministicHashIdsOutsideLegacyRange_ProbesCollisions_AndEmitsRequiredSections()
    {
        string repo = Path.Combine(_adrDir, "scaffold-repo");
        string adrDir = Path.Combine(repo, "docs", "adr");
        Directory.CreateDirectory(adrDir);
        var date = new DateOnly(2026, 11, 23);

        int id1 = CreateAdrService.ComputeDeterministicAdrId(date, "adopt-event-outbox", new HashSet<int>());
        int id2 = CreateAdrService.ComputeDeterministicAdrId(date, "adopt-event-outbox", new HashSet<int>());
        id1.Should().Be(id2);
        id1.Should().BeInRange(10000, 99999);

        // Collision probing resolves deterministically when initial ID is already occupied.
        int collidedId = CreateAdrService.ComputeDeterministicAdrId(date, "adopt-event-outbox", new HashSet<int> { id1 });
        collidedId.Should().NotBe(id1);
        collidedId.Should().BeInRange(10000, 99999);

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        ScaffoldCommands.ScaffoldAdr(
            title: "Adopt Event Outbox",
            repoRoot: repo,
            clock: clock,
            domain: "unknown-domain").Should().Be(1);

        ScaffoldCommands.ScaffoldAdr(
            title: "Adopt Event Outbox",
            repoRoot: repo,
            clock: clock,
            domain: "kernel",
            requirements: ["PRD-SYS-001", "@req:PRD-SYS-002"],
            author: "@fderuiter").Should().Be(0);

        string createdFile = Path.Combine(adrDir, "2026-11-23-adopt-event-outbox.md");
        File.Exists(createdFile).Should().BeTrue();
        string content = File.ReadAllText(createdFile);
        content.Should().StartWith($"# ADR-{id1}: Adopt Event Outbox\n");
        content.Should().Contain("- **Domain:** 0. Clinical Kernel (K001–K037)");
        content.Should().Contain("@req:PRD-SYS-001");
        content.Should().Contain("@req:PRD-SYS-002");
        foreach (string section in new[]
                 {
                     "## 1. Context & Problem Statement",
                     "## 2. Decision Drivers & Constraints",
                     "## 3. Options Considered",
                     "## 4. Decision Outcome",
                     "## 5. Consequences & Trade-offs",
                     "## 6. Implementation & Verification"
                 })
        {
            content.Should().Contain(section);
        }

        File.Exists(Path.Combine(adrDir, "index.md")).Should().BeFalse("scaffolding an ADR must not create or touch docs/adr/index.md");
    }

    [Fact]
    [Trait("Category", "Tooling")]
    public void ScaffoldAdr_TwoParallelBranchesMergeCleanlyWithoutRegistryConflicts_AndPassAllValidators()
    {
        string repo = Path.Combine(_adrDir, "parallel-merge-repo");
        Directory.CreateDirectory(Path.Combine(repo, "docs", "adr"));
        Directory.CreateDirectory(Path.Combine(repo, "docs", "SDLC"));

        const string docIndexContent = """
            # Documentation Index

            ## Requirements, validation and GxP

            | Document | Status | What it is |
            | --- | --- | --- |
            | [PRD](SDLC/prd.md) | Normative | System requirements |

            ## Architecture decision records

            | Document | Status | What it is |
            | --- | --- | --- |
            | [Architecture Decision Records (ADRs)](adr/index.md) | Current | Catalog |
            | [ADR Template](adr/TEMPLATE.md) | Current | Template |
            """;
        const string adrIndexContent = """
            # Architecture Decision Records (ADRs)

            - [ADR Template](TEMPLATE.md)

            <AdrIndex />
            """;

        File.WriteAllText(Path.Combine(repo, "docs", "DOCUMENTATION_INDEX.md"), docIndexContent + "\n");
        File.WriteAllText(Path.Combine(repo, "docs", "adr", "index.md"), adrIndexContent + "\n");
        File.WriteAllText(Path.Combine(repo, "docs", "adr", "TEMPLATE.md"), "# ADR-[NUMBER]: Title\n\nTemplate body.\n");
        File.WriteAllText(
            Path.Combine(repo, "docs", "SDLC", "prd.md"),
            "#### PRD-SYS-001: Kernel event contract\n#### PRD-SYS-002: Gateway auth boundary\n");

        RunGit(repo, "init", "-q", "-b", "main");
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "chore: baseline docs");
        RunGit(repo, "update-ref", "refs/remotes/origin/main", "HEAD");

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero));

        // Branch A scaffolds an ADR
        RunGit(repo, "checkout", "-q", "-b", "branch-a", "main");
        ScaffoldCommands.ScaffoldAdr(
            title: "Introduce Kernel Observation Stream",
            repoRoot: repo,
            clock: clock,
            domain: "kernel",
            requirements: ["PRD-SYS-001"],
            author: "@alice").Should().Be(0);
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "docs(adr): add kernel observation stream ADR");

        // Branch B independently scaffolds a different ADR from the exact same main base
        RunGit(repo, "checkout", "-q", "-b", "branch-b", "main");
        ScaffoldCommands.ScaffoldAdr(
            title: "Harden Gateway Session Revocation",
            repoRoot: repo,
            clock: clock,
            domain: "security",
            requirements: ["PRD-SYS-002"],
            author: "@bob").Should().Be(0);
        RunGit(repo, "add", "-A");
        RunGit(repo, "commit", "-q", "-m", "docs(adr): add gateway session revocation ADR");

        // Merge branch-b into branch-a: zero git conflicts, zero central registry diffs!
        RunGit(repo, "checkout", "-q", "branch-a");
        RunGit(repo, "merge", "--no-edit", "branch-b");

        File.ReadAllText(Path.Combine(repo, "docs", "DOCUMENTATION_INDEX.md")).Should().Be(docIndexContent + "\n");
        File.ReadAllText(Path.Combine(repo, "docs", "adr", "index.md")).Should().Be(adrIndexContent + "\n");

        string adrA = Path.Combine(repo, "docs", "adr", "2026-11-23-introduce-kernel-observation-stream.md");
        string adrB = Path.Combine(repo, "docs", "adr", "2026-11-23-harden-gateway-session-revocation.md");
        int? numA = AdrValidatorService.ParseAdrNumber(File.ReadLines(adrA).First());
        int? numB = AdrValidatorService.ParseAdrNumber(File.ReadLines(adrB).First());
        numA.Should().NotBeNull();
        numB.Should().NotBeNull();
        numA.Should().NotBe(numB);

        AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: repo).Should().BeTrue();
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: repo).Should().Be(0);
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
            "# Index\n\n- [ADR Template](TEMPLATE.md)\n\n<AdrIndex />\n");
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

        // Stage a non-architectural doc change.
        File.WriteAllText(Path.Combine(repoRoot, "docs", "note.md"), "# Note\n");
        RunGit(repoRoot, "add", "docs/note.md");

        // Leave unstaged architectural edits in gateway and package.json dependencies.
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

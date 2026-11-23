using System;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages.Gates;

/// <summary>
/// Behavioral tests for quality gate execution modes (`--pre-commit`, `--exclude`, `--base`, `--push-before`) and
/// orchestrator invariants.
/// </summary>
public sealed class QualityGateBehaviorTests : IDisposable
{
    private readonly QualityGateFixtureWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void EveryFileScopedGate_TreatsExplicitEmptyTargetFilesAsNoOp()
    {
        string reqPath = Path.Combine(_ws.Root, "docs", "requirements.md");
        const string originalReqDoc = "# Requirements\n\nCovers @req:prd-sys-001   \n";
        _ws.Write("docs/requirements.md", originalReqDoc);
        _ws.Write("scratch/notes.txt", "forbidden directory\n");
        _ws.Write(".github/workflows/broken.yml", "name: \"unclosed\n");
        _ws.Write("Dockerfile", "FROM ubuntu:latest\nWORKDIR relative/path\n");
        _ws.Write("config/service.pem", $"{QualityGateFixtureWorkspace.PrivateKeyHeader}\nMIIB\n");
        _ws.Write("apps/alpha/src/Seed.cs", $"class Seed {{ string Ssn = \"000-12-3456\"; bool Check(bool f) => f {QualityGateFixtureWorkspace.BooleanComparison}; }}\n");
        Directory.CreateDirectory(Path.Combine(_ws.Root, "docs", "openapi"));
        _ws.Write("docs/api/retired.md", "# Retired service\n");

        var emptyPreCommitContext = _ws.Context(files: [], preCommit: true);
        emptyPreCommitContext.Files.Should().NotBeNull().And.BeEmpty();

        foreach (var gate in QualityGates.All.Where(g => g.TakesFiles))
        {
            gate.Run(emptyPreCommitContext).Should().BeTrue(
                $"file-scoped gate '{gate.Name}' must treat an explicit empty TargetFiles list ([]) as a no-op");
        }

        File.ReadAllText(reqPath).Should().Be(
            originalReqDoc,
            "unstaged Markdown files must never be rewritten on an empty-file pre-commit run");
    }

    [Fact]
    public void RunQualityGates_SkipsExcludedGatesByNameOrAlias_AndFailsClosedOnInvalidExcludeUsage()
    {
        _ws.Write("scratch/notes.txt", "left behind\n");
        string[] unrelatedGates = QualityGates.All
            .Select(g => g.Name)
            .Where(n => n is not "dependencies" and not "path-patterns")
            .ToArray();

        QualityGateOrchestrator.RunQualityGates(excludedGates: ["no-such-gate"], repoRoot: _ws.Root)
            .Should().Be(1, "unknown excluded gate names must fail closed");
        QualityGateOrchestrator.RunQualityGates(gateName: "dependencies", excludedGates: ["path-patterns"], repoRoot: _ws.Root)
            .Should().Be(1, "--exclude applies only when running all gates");
        QualityGateOrchestrator.RunQualityGates(excludedGates: unrelatedGates, repoRoot: _ws.Root)
            .Should().Be(1, "path-patterns still runs and fails when not excluded");
        QualityGateOrchestrator.RunQualityGates(excludedGates: [.. unrelatedGates, "validate-path-patterns"], repoRoot: _ws.Root)
            .Should().Be(0, "excluding path-patterns by alias skips it while running the remaining gate");
    }

    [Fact]
    public void RunQualityGates_SkipsPublicBundleOnlyWhenAllThreeBundleDirectoriesAreAbsent_AndFailsClosedOnPartialDistOrDirectCall()
    {
        string[] allOtherGates = QualityGates.All
            .Select(g => g.Name)
            .Where(n => n != "public-bundle")
            .ToArray();

        QualityGateOrchestrator.RunQualityGates(excludedGates: allOtherGates, repoRoot: _ws.Root)
            .Should().Be(0, "unscoped cadence check must skip post-build public-bundle when no dist/ directory exists");

        QualityGateOrchestrator.RunQualityGates(gateName: "public-bundle", repoRoot: _ws.Root)
            .Should().Be(1, "explicit cadence check public-bundle must fail closed when dist/ directories are absent");

        _ws.Write("apps/web/dist/index.html", "<!doctype html><html><head></head><body>partial</body></html>\n");
        QualityGateOrchestrator.RunQualityGates(excludedGates: allOtherGates, repoRoot: _ws.Root)
            .Should().Be(1, "unscoped cadence check must fail closed when partial dist/ output exists");
    }

    [Fact]
    public void SchemaDocGate_InPreCommitMode_SkipsWhenNoModelFilesStaged()
    {
        var gate = QualityGates.Find("schema-doc")!;
        var context = _ws.Context(files: ["apps/web/src/App.vue", "docs/guide.md"], preCommit: true);

        gate.Run(context).Should().BeTrue("pre-commit hook skips schema documentation check when staged files do not affect EF Core models");
    }

    [Fact]
    public void SchemaDocGate_InPreCommitMode_RunsWhenModelFilesStaged()
    {
        var gate = QualityGates.Find("schema-doc")!;
        var context = _ws.Context(files: ["apps/econsent/src/Domain/EconsentEntities.cs"], preCommit: true);

        gate.Run(context).Should().BeFalse("pre-commit hook executes schema documentation drift check when EF Core entity files are staged");
    }

    [Fact]
    public void DuplicationGate_InPreCommitMode_IgnoresNonCSharpDuplicates()
    {
        string cssAliases = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"  --alias-{i}: var(--semantic-token-{i});"));
        _ws.Write("apps/web/src/style.css", $":root {{\n{cssAliases}\n}}\n");
        _ws.Write("apps/subject-portal/src/style.css", $":root {{\n{cssAliases}\n}}\n");

        var gate = QualityGates.Find("duplication")!;
        var context = _ws.Context(files: ["apps/web/src/style.css", "apps/subject-portal/src/style.css"], preCommit: true);

        gate.Run(context).Should().BeTrue("duplication gate fails only when a duplicate involves a C# or JavaScript file, even when pre-commit passes staged files");

        string jsLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const val{i} = input + {i};"));
        _ws.Write("apps/web/src/dup.js", $"export function compute(input) {{\n{jsLines}\n  return val18;\n}}\n");
        _ws.Write("apps/subject-portal/src/dup.js", $"export function compute(input) {{\n{jsLines}\n  return val18;\n}}\n");

        var jsContext = _ws.Context(files: ["apps/web/src/dup.js", "apps/subject-portal/src/dup.js"], preCommit: true);
        gate.Run(jsContext).Should().BeFalse("duplication gate rejects staged JavaScript duplicates over 15 normalized lines");
    }

    [Fact]
    public void DemoManifestGate_InPreCommitMode_SkipsUnrelatedFiles_AndRunsWhenSeedOrFixtureFilesStaged()
    {
        var gate = QualityGates.Find("demo-manifest")!;
        gate.TakesFiles.Should().BeTrue();
        gate.PreCommit.Should().Be(PreCommitMode.Quick);

        gate.Run(_ws.Context(files: ["apps/web/src/App.vue", "docs/guide.md"], preCommit: true))
            .Should().BeTrue("pre-commit hook skips demo-manifest when staged files do not touch demo seed or fixture inputs");

        gate.Run(_ws.Context(files: ["packages/demo-fixtures/demo-seed.json"], preCommit: true))
            .Should().BeFalse("pre-commit hook runs demo-manifest when demo-seed.json is staged and fixture inputs are missing or drifted");
    }

    [Fact]
    public void BranchFreshnessAndTestSuiteGates_SkipPreCommit_AndBranchFreshnessPassesWhenOriginMainIsAncestor()
    {
        QualityGates.Find("branch-freshness")!.PreCommit.Should().Be(PreCommitMode.Skipped);
        QualityGates.Find("architecture-tests")!.PreCommit.Should().Be(PreCommitMode.Skipped);
        QualityGates.Find("unit-tests")!.PreCommit.Should().Be(PreCommitMode.Skipped);

        _ws.Write("README.md", "# Cadence\n");
        _ws.CommitOnMainThenBranch();
        _ws.Write("README.md", "# Cadence feature work\n");
        _ws.Git("commit", "-q", "-am", "feature commit");

        QualityGates.Find("branch-freshness")!.Run(_ws.Context())
            .Should().BeTrue("feature branch descending from origin/main satisfies branch-freshness");

        _ws.Git("checkout", "-q", "main");
        _ws.Write("README.md", "# Cadence advanced on main\n");
        _ws.Git("commit", "-q", "-am", "advance main");
        _ws.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        _ws.Git("checkout", "-q", "feature");

        QualityGates.Find("branch-freshness")!.Run(_ws.Context(pushBefore: "0000000000000000000000000000000000000000"))
            .Should().BeTrue("push-scoped CI runs (--push-before) skip branch-freshness so historical tag and release pushes succeed");
        QualityGates.Find("branch-freshness")!.Run(_ws.Context(baseRef: "refs/remotes/origin/release/1.0"))
            .Should().BeFalse("missing custom --base ref fails closed");
    }

    [Fact]
    public void CleanSecretsGate_FailsOnUnnormalizedBaseline_WithoutMutatingBaselineOnDisk()
    {
        const string unnormalized =
            "{\n" +
            "  \"version\": \"1.5.0\",\n" +
            "  \"generated_at\": \"2026-01-01T00:00:00Z\",\n" +
            "  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n" +
            "  \"filters_used\": [],\n" +
            "  \"results\": {\n" +
            "    \"src/Sample.cs\": [{ \"type\": \"Hex High Entropy String\", \"hashed_secret\": \"abc\", \"line_number\": 12 }]\n" +
            "  }\n" +
            "}\n";
        _ws.Write(".secrets.baseline", unnormalized);

        var gate = QualityGates.Find("clean-secrets")!;
        gate.Run(_ws.Context()).Should().BeFalse("unnormalized .secrets.baseline must fail clean-secrets check");
        File.ReadAllText(Path.Combine(_ws.Root, ".secrets.baseline")).Should().Be(
            unnormalized,
            "cadence check clean-secrets must never mutate .secrets.baseline on disk");

        Cadence.ToolingCore.Services.SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: _ws.Root).Should().Be(0);
        gate.Run(_ws.Context()).Should().BeTrue("normalized .secrets.baseline passes clean-secrets check");
    }

    [Fact]
    public void TrailingWhitespaceGate_EnforcesLegacyMarkdownSpecsInUnscopedAndPreCommitModes()
    {
        _ws.Write("docs/SRS.md", "# Software Requirements Specification   \n");
        var gate = QualityGates.Find("trailing-whitespace")!;

        gate.Run(_ws.Context()).Should().BeFalse("unscoped trailing-whitespace check must not exempt docs/SRS.md");
        gate.Run(_ws.Context(files: ["docs/SRS.md"], preCommit: true)).Should().BeFalse("pre-commit trailing-whitespace check also rejects trailing spaces");
    }

    [Fact]
    public void DeleteOnlyPreCommit_PreservesEmptyTargetFiles_AndSkipsFullRepoScans()
    {
        Directory.CreateDirectory(Path.Combine(_ws.Root, "docs", "openapi"));
        Cadence.ToolingCore.Services.ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot: _ws.Root).Should().Be(0);
        _ws.Write("obsolete.md", "# Obsolete\n");
        _ws.Write("docs/guide.md", "# Guide   \n\nSee [missing](nope.md).\n");
        _ws.Write("scratch/notes.txt", "untracked-pattern-violation\n");
        _ws.CommitOnMainThenBranch();

        _ws.Git("rm", "-q", "obsolete.md");
        Cadence.ToolingCore.Services.PreCommitOpenApiService.GetStagedFiles(_ws.Root).Should().BeEmpty();

        string[] nonFileGates = QualityGates.All
            .Where(g => !g.TakesFiles)
            .Select(g => g.Name)
            .ToArray();
        QualityGateOrchestrator.RunQualityGates(excludedGates: nonFileGates, preCommit: true, repoRoot: _ws.Root)
            .Should().Be(0, "a delete-only commit has zero surviving staged files and must complete all pre-commit file gates without scanning the full repository");
    }

    [Fact]
    public void MergeReadinessGate_FailsWhenBaseRefMissingOrConflicted_AndPassesWhenCleanOrPushScoped()
    {
        var gate = QualityGates.Find("merge-readiness")!;
        QualityGates.Find("check-merge-readiness").Should().BeSameAs(gate);
        gate.PreCommit.Should().Be(PreCommitMode.Skipped);

        _ws.Write("apps/one.cs", "class One {}\n");
        _ws.CommitOnMainThenBranch();
        _ws.Git("update-ref", "-d", "refs/remotes/origin/main");

        gate.Run(_ws.Context()).Should().BeFalse("missing origin/main must fail merge-readiness closed");

        _ws.Git("checkout", "-q", "main");
        _ws.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        _ws.Git("checkout", "-q", "feature");
        _ws.Write("apps/two.cs", "class Two {}\n");
        _ws.Git("add", "apps/two.cs");
        _ws.Git("commit", "-q", "-m", "clean feature commit");

        gate.Run(_ws.Context()).Should().BeTrue("a non-conflicting branch over origin/main passes merge-readiness");
        gate.Run(_ws.Context(pushBefore: "0000000000000000000000000000000000000000"))
            .Should().BeTrue("push-scoped CI runs skip merge-readiness cleanly");
    }
}

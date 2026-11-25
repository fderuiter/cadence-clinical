using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The quality gate registry (<see cref="QualityGates"/>, #5332): every gate can fail, names are unique, and the gate
/// tables generated into the docs are current. Each gate runs against a fixture repository that must make it fail; a
/// gate added to the registry without a fixture here fails <see cref="EveryGate_HasAFailingFixture"/>.
/// </summary>
public sealed class QualityGateRegistryTests : IDisposable
{
    // Built at run time so the repository's own security, query-linter and RTM gates do not report this file.
    private static readonly string PrivateKeyHeader = "-----BEGIN " + "PRIVATE KEY-----";
    private static readonly string BooleanComparison = "== " + "true";
    private static readonly string RequirementAttribute = "[GxP" + "Requirement(\"PRD-NOPE-999\")]";

    /// <summary>
    /// Gates whose real check needs a toolchain a unit test cannot provide hermetically. Their fixture lacks that
    /// toolchain, and the test proves the gate fails closed instead of passing. The comparison each one makes is the
    /// TypeScript compiler's or is covered elsewhere.
    /// </summary>
    private static readonly Dictionary<string, string> FailsClosedOnly = new(StringComparer.Ordinal)
    {
        ["prettier"] = "runs `pnpm exec prettier` against workspace node_modules; the fixture has no node_modules",
        ["eslint"] = "runs `pnpm exec eslint` against workspace node_modules; the fixture has no node_modules",
        ["typecheck"] = "runs `pnpm -r typecheck` against workspace node_modules; the fixture has no node_modules",
        ["api-clients"] = "runs `pnpm --filter web` and `pnpm -r typecheck` over the real web workspace; the fixture has no apps/web/tsconfig.json",
        ["schema-doc"] = "builds Cadence.SchemaDocExporter and every module's EF Core model; the fixture has no exporter project " +
                         "(the rendering is covered by SchemaDocumentationTests)",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"quality-gate-fixture-{Guid.NewGuid():N}");

    public QualityGateRegistryTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // git marks its object files read-only.
        foreach (var file in new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_root, recursive: true);
    }

    public static TheoryData<string> GateNames() => new(QualityGates.All.Select(g => g.Name));

    /// <summary>Builds, in the fixture root, a repository that breaks one gate, and returns the context to run it with.</summary>
    private Dictionary<string, Func<GateContext>> Fixtures() => new(StringComparer.Ordinal)
    {
        ["dotnet-format"] = () =>
        {
            WriteDotnetProject("root = true\n[*.cs]\nindent_style = space\nindent_size = 4\n",
                "namespace Fixture;\n\npublic static class Bad\n{\n        public static int Value => 1;\n}\n");
            return Context();
        },
        ["dotnet-format-style"] = () =>
        {
            WriteDotnetProject("root = true\n[*.cs]\nindent_style = space\nindent_size = 4\ncsharp_style_namespace_declarations = file_scoped:warning\n",
                "namespace Fixture\n{\n    public static class Bad\n    {\n        public static int Value => 1;\n    }\n}\n");
            return Context();
        },
        ["prettier"] = () => Context(),
        ["eslint"] = () => Context(),
        ["typecheck"] = () => Context(),
        ["actionlint"] = () =>
        {
            Write(".github/workflows/broken.yml",
                "name: Broken\non: invalid_trigger_event\njobs:\n  bad:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo \"${{ invalid.context.value }}\"\n");
            return Context();
        },
        ["hadolint"] = () =>
        {
            Write("Dockerfile", "FROM ubuntu:24.04\nWORKDIR relative/path\nEXPOSE 99999\n");
            return Context();
        },
        ["shellcheck"] = () =>
        {
            Write("scripts/broken.sh", "#!/bin/sh\nUNUSED_VAR=1\necho $1\n");
            return Context();
        },
        ["yaml"] = () =>
        {
            Write(".github/workflows/broken.yml", "name: \"unclosed\njobs:\n\tbad: true\n");
            return Context();
        },
        ["trailing-whitespace"] = () =>
        {
            Write("docs/note.md", "# Note   \n");
            return Context();
        },
        ["end-of-file"] = () =>
        {
            Write("docs/note.md", "# Note without newline");
            return Context();
        },
        ["clean-secrets"] = () =>
        {
            Write(".secrets.baseline", "{ \"results\": ");
            return Context();
        },
        ["detect-secrets"] = () =>
        {
            Write("baselines/secrets/config.json",
                "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": []\n}\n");
            CommitOnMainThenBranch();
            Write("config/leak.txt", $"{PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");
            Git("add", "config/leak.txt");
            Git("commit", "-q", "-m", "leak secret on branch");
            return Context();
        },
        ["duplication"] = () =>
        {
            string body = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"        int value{i} = Compute({i}, {i * 7});"));
            Write("apps/alpha/src/One.cs", $"class One\n{{\n    void Run()\n    {{\n{body}\n    }}\n}}\n");
            Write("apps/beta/src/Two.cs", $"class Two\n{{\n    void Go()\n    {{\n{body}\n    }}\n}}\n");
            return Context();
        },
        ["cli-docs"] = () =>
        {
            Write("docs/cli.md", "# Cadence Unified Developer CLI Reference\n\n`cadence doctor` `cadence check` `cadence fix` `cadence gxp`\n\n" +
                                 Region(GateDocs.RenderCliTable()));
            Write("docs/CI.md", "# CI\n\n" + Region("| Gate | What it checks |\n| --- | --- |\n| `stale` | A gate that no longer exists |\n"));
            return Context();
        },
        ["adrs"] = () =>
        {
            Write("docs/adr/use-a-thing.md", "# ADR-0001: Use a thing\n\nNo sections.\n");
            return Context();
        },
        ["arch-drift"] = () =>
        {
            Write("docker/docker-compose.yml", "name: cadence\n");
            return Context();
        },
        ["sanitizer"] = () =>
        {
            Write("docs/requirements.md", "# Requirements\n\nCovers @req:prd-sys-001.\n");
            return Context();
        },
        ["markdown"] = () =>
        {
            Write("docs/guide.md", "# Guide\n\nSee [the missing page](missing-page.md).\n");
            return Context();
        },
        ["env-vars"] = () =>
        {
            Write("apps/alpha/src/Settings.cs", "class Settings { string? V = System.Environment.GetEnvironmentVariable(\"FIXTURE_UNDOCUMENTED\"); }\n");
            Write(".env.example", "# no variables\n");
            Write("docs/ENVIRONMENT_VARIABLES.md", "# Environment variables\n");
            return Context();
        },
        ["dev-env"] = () =>
        {
            // The port comparisons themselves are covered by DevEnvironmentValidatorServiceTests.
            Write("apps/gateway/src/Program.cs", "class Program { }\n");
            return Context();
        },
        ["readmes"] = () =>
        {
            Write("apps/alpha/src/Program.cs", "class Program { }\n");
            return Context();
        },
        ["codeowners"] = () =>
        {
            Write(".github/CODEOWNERS", "* @fderuiter\n/nonexistent/path/ @fderuiter\n");
            return Context();
        },
        ["workflow-security"] = () =>
        {
            Write(".github/workflows/ci.yml",
                "name: CI\non: push\npermissions:\n  contents: read\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v7\n");
            return Context();
        },
        ["rulesets"] = () =>
        {
            Write(".github/rulesets/main.json", """
                {
                  "name": "main-branch-protection",
                  "target": "branch",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
                  "rules": [
                    { "type": "deletion" },
                    { "type": "non_fast_forward" },
                    { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                    { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": false, "required_status_checks": [{ "context": "Removed Legacy Check" }] } }
                  ]
                }
                """);
            Write(".github/rulesets/release_branches.json", """
                {
                  "name": "release-branches-protection",
                  "target": "branch",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/heads/release/*"], "exclude": [] } },
                  "rules": [
                    { "type": "deletion" },
                    { "type": "non_fast_forward" },
                    { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                    { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": true, "required_status_checks": [{ "context": "CI Gate", "integration_id": 15368 }, { "context": "Check for Merge Conflicts", "integration_id": 15368 }] } }
                  ]
                }
                """);
            Write(".github/rulesets/version_tags.json", """
                {
                  "name": "version-tags-protection",
                  "target": "tag",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
                  "rules": [{ "type": "deletion" }, { "type": "non_fast_forward" }]
                }
                """);
            Write(".github/workflows/ci.yml", "name: CI\non:\n  pull_request:\njobs:\n  unit-tests:\n    name: Unit Tests\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n  ci-gate:\n    name: CI Gate\n    needs: [unit-tests]\n    if: always()\n    runs-on: ubuntu-latest\n    steps:\n      - env:\n          UNIT_RESULT: ${{ needs.unit-tests.result }}\n        run: |\n          if [ \"$UNIT_RESULT\" != \"success\" ]; then exit 1; fi\n");
            Write(".github/workflows/conflict-check.yml", "name: Conflict Check\non:\n  pull_request:\njobs:\n  conflict-check:\n    name: Check for Merge Conflicts\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n");
            return Context();
        },
        ["imports"] = () =>
        {
            Write("apps/alpha/src/Uses.cs", "using Cadence.Apps.Beta.Domain;\n\nclass Uses { }\n");
            return Context();
        },
        ["permission-matrix"] = () =>
        {
            PermissionMatrixRatchetService.WriteBaselineShards(_root, PermissionMatrixRatchetService.GetRuntimeGrants());
            Write(
                "packages/security/src/Rbac/PermissionMatrix.Shadow.cs",
                "namespace Cadence.Security.Rbac;\npublic static partial class PermissionMatrix\n{\n    static void Extra() => Grant(\"crc\", \"shadow_resource\", \"escalate\");\n}\n");
            return Context();
        },
        ["security"] = () =>
        {
            Write("config/service.pem", $"{PrivateKeyHeader}\nMIIB\n");
            return Context();
        },
        ["deid"] = () =>
        {
            Write("apps/alpha/src/Seed.cs", "var a = 1;\nvar ssn = \"000-12-3456\";\n");
            return Context(files: ["apps/alpha/src/Seed.cs"]);
        },
        ["openapi"] = () =>
        {
            // The pre-commit variant with no staged C#: docs/api is compared with docs/openapi, which serves nothing.
            Directory.CreateDirectory(Path.Combine(_root, "docs", "openapi"));
            Write("docs/api/retired.md", "# Retired service\n");
            return Context(files: ["docs/api/retired.md"], preCommit: true);
        },
        ["api-clients"] = () => Context(),
        ["schema-doc"] = () => Context(),
        ["path-patterns"] = () =>
        {
            Write("scratch/notes.txt", "left behind\n");
            return Context();
        },
        ["query-linter"] = () =>
        {
            Write("apps/alpha/src/Filter.cs", $"class Filter {{ bool Keep(bool flag) => flag {BooleanComparison}; }}\n");
            return Context();
        },
        ["vulnerabilities"] = () =>
        {
            // An exemption with an out-of-range score fails before any package manager is asked.
            Write("docs/SDLC/vulnerability_exclusions_ledger.json",
                """[{ "vulnerability_id": "GHSA-0000", "package_name": "pkg", "severity": 9, "occurrence": 1, "detectability": 1, "rpn": 9, "justification": "j", "status": "Approved" }]""");
            return Context();
        },
        ["dependencies"] = () =>
        {
            Write("apps/web/package.json", """{ "dependencies": { "node-forge": "1.3.1" } }""");
            return Context();
        },
        ["doc-index"] = () =>
        {
            Write("docs/DOCUMENTATION_INDEX.md", "# Documentation index\n");
            Write("docs/unlisted.md", "# Unlisted\n");
            return Context();
        },
        ["demo-manifest"] = () =>
        {
            Write("packages/demo-fixtures/src/manifest.js", "export const DEMO_MANIFEST = {};\n");
            return Context();
        },
        ["rtm"] = () =>
        {
            Write("tests/Fixture/FixtureTests.cs",
                $"public class FixtureTests\n{{\n    [Fact]\n    {RequirementAttribute}\n    public void Proves() {{ }}\n}}\n");
            return Context();
        },
        ["gxp-snapshot"] = () =>
        {
            Write("docs/SDLC/Requirements_Traceability_Matrix.md", "# RTM\n");
            CommitOnMainThenBranch();
            Write("docs/SDLC/Requirements_Traceability_Matrix.md", "# RTM regenerated on a branch\n");
            Git("commit", "-q", "-am", "regenerate the RTM");
            return Context();
        },
        ["baseline-ratchet"] = () =>
        {
            Write("tests/Arch/rules.baseline.txt", "# may only shrink\nK-XS-1|App|A\n");
            CommitOnMainThenBranch();
            Write("tests/Arch/rules.baseline.txt", "# may only shrink\nK-XS-1|App|A\nK-XS-1|App|B\n");
            Git("commit", "-q", "-am", "grow the baseline");
            return Context();
        },
        ["merge-readiness"] = () =>
        {
            Write("apps/shared.cs", "class Shared { int Value = 1; }\n");
            CommitOnMainThenBranch();
            Write("apps/shared.cs", "class Shared { int Value = 2; }\n");
            Git("commit", "-q", "-am", "feature change");
            Git("checkout", "-q", "main");
            Write("apps/shared.cs", "class Shared { int Value = 3; }\n");
            Git("commit", "-q", "-am", "upstream change");
            Git("update-ref", "refs/remotes/origin/main", "HEAD");
            Git("checkout", "-q", "feature");
            return Context();
        },
        ["public-bundle"] = () =>
        {
            const string validCsp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'; media-src 'self'; worker-src 'self'; connect-src 'self'\">";
            Write("docs/.vitepress/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><p>Portal</p></body></html>\n");
            Write("apps/subject-portal/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><p>Participant Portal</p></body></html>\n");
            Write("apps/web/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><img src=\"https://images.unsplash.com/photo-1\"></body></html>\n");
            return Context();
        },
        ["branch-freshness"] = () =>
        {
            Write("README.md", "# Cadence\n");
            CommitOnMainThenBranch();
            Git("checkout", "-q", "main");
            Write("README.md", "# Cadence advanced on main\n");
            Git("commit", "-q", "-am", "advance main");
            Git("update-ref", "refs/remotes/origin/main", "HEAD");
            Git("checkout", "-q", "feature");
            return Context();
        },
        ["architecture-tests"] = () =>
        {
            WriteBrokenTestProject("tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj");
            return Context();
        },
        ["unit-tests"] = () =>
        {
            WriteBrokenTestProject("tests/Cadence.UnitTests/Cadence.UnitTests.csproj");
            return Context();
        },
    };

    [Theory]
    [MemberData(nameof(GateNames))]
    public void EveryGate_FailsOnAFixtureThatBreaksIt(string gateName)
    {
        var gate = QualityGates.Find(gateName)!;
        var context = Fixtures()[gateName]();

        gate.Run(context).Should().BeFalse(
            FailsClosedOnly.TryGetValue(gateName, out string? why)
                ? $"'{gateName}' must fail closed when its toolchain is missing ({why})"
                : $"the fixture breaks what '{gateName}' checks");
    }

    [Fact]
    public void EveryGate_HasAFailingFixture()
    {
        QualityGates.All.Should().HaveCount(46);
        Fixtures().Keys.Should().BeEquivalentTo(QualityGates.All.Select(g => g.Name));
        FailsClosedOnly.Keys.Should().BeSubsetOf(QualityGates.All.Select(g => g.Name));
    }

    [Fact]
    public void EveryFileScopedGate_TreatsExplicitEmptyTargetFilesAsNoOp()
    {
        string reqPath = Path.Combine(_root, "docs", "requirements.md");
        const string originalReqDoc = "# Requirements\n\nCovers @req:prd-sys-001   \n";
        Write("docs/requirements.md", originalReqDoc);
        Write("scratch/notes.txt", "forbidden directory\n");
        Write(".github/workflows/broken.yml", "name: \"unclosed\n");
        Write("Dockerfile", "FROM ubuntu:latest\nWORKDIR relative/path\n");
        Write("scripts/broken.sh", "#!/bin/sh\nUNUSED_VAR=1\necho $1\n");
        Write("config/service.pem", $"{PrivateKeyHeader}\nMIIB\n");
        Write("apps/alpha/src/Seed.cs", $"class Seed {{ string Ssn = \"000-12-3456\"; bool Check(bool f) => f {BooleanComparison}; }}\n");
        Directory.CreateDirectory(Path.Combine(_root, "docs", "openapi"));
        Write("docs/api/retired.md", "# Retired service\n");

        var emptyPreCommitContext = Context(files: [], preCommit: true);
        emptyPreCommitContext.Files.Should().NotBeNull().And.BeEmpty();

        foreach (var gate in QualityGates.All.Where(g => g.TakesFiles))
        {
            gate.Run(emptyPreCommitContext).Should().BeTrue(
                $"file-scoped gate '{gate.Name}' must treat an explicit empty TargetFiles list ([]) as a no-op");
        }

        File.ReadAllText(reqPath).Should().Be(originalReqDoc,
            "unstaged Markdown files must never be rewritten on an empty-file pre-commit run");
    }

    [Fact]
    public void RunQualityGates_SkipsExcludedGatesByNameOrAlias_AndFailsClosedOnInvalidExcludeUsage()
    {
        Write("scratch/notes.txt", "left behind\n");
        string[] unrelatedGates = [.. QualityGates.All
            .Select(g => g.Name)
            .Where(n => n is not "dependencies" and not "path-patterns")];

        QualityGateOrchestrator.RunQualityGates(excludedGates: ["no-such-gate"], repoRoot: _root)
            .Should().Be(1, "unknown excluded gate names must fail closed");
        QualityGateOrchestrator.RunQualityGates(gateName: "dependencies", excludedGates: ["path-patterns"], repoRoot: _root)
            .Should().Be(1, "--exclude applies only when running all gates");
        QualityGateOrchestrator.RunQualityGates(excludedGates: unrelatedGates, repoRoot: _root)
            .Should().Be(1, "path-patterns still runs and fails when not excluded");
        QualityGateOrchestrator.RunQualityGates(excludedGates: [.. unrelatedGates, "validate-path-patterns"], repoRoot: _root)
            .Should().Be(0, "excluding path-patterns by alias skips it while running the remaining gate");
    }

    [Fact]
    public void RunQualityGates_SkipsPublicBundleOnlyWhenAllThreeBundleDirectoriesAreAbsent_AndFailsClosedOnPartialDistOrDirectCall()
    {
        string[] allOtherGates = [.. QualityGates.All
            .Select(g => g.Name)
            .Where(n => n != "public-bundle")];

        QualityGateOrchestrator.RunQualityGates(excludedGates: allOtherGates, repoRoot: _root)
            .Should().Be(0, "unscoped cadence check must skip post-build public-bundle when no dist/ directory exists");

        QualityGateOrchestrator.RunQualityGates(gateName: "public-bundle", repoRoot: _root)
            .Should().Be(1, "explicit cadence check public-bundle must fail closed when dist/ directories are absent");

        Write("apps/web/dist/index.html", "<!doctype html><html><head></head><body>partial</body></html>\n");
        QualityGateOrchestrator.RunQualityGates(excludedGates: allOtherGates, repoRoot: _root)
            .Should().Be(1, "unscoped cadence check must fail closed when partial dist/ output exists");
    }

    [Fact]
    public void GateNamesAndAliases_AreUniqueAndFound()
    {
        var names = QualityGates.All.SelectMany(g => g.AllNames).ToList();

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n == n.ToLowerInvariant() && !n.Contains('_'));
        QualityGates.All.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Checks));
        QualityGates.All.Where(g => g.PreCommit != PreCommitMode.Full).Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.PreCommitNote));

        QualityGates.Find("Detect_Code_Duplication").Should().BeSameAs(QualityGates.Find("duplication"));
        QualityGates.Find("prettier-check").Should().BeNull("Prettier runs in the pre-commit hook and CI, not as a gate");
        QualityGates.Find("contracts").Should().BeNull("the architecture tests enforce the layering it claimed to check");
    }

    [Fact]
    public void GateTables_InTheRepository_MatchTheRegistry()
    {
        GateDocs.Check(QualityGateOrchestrator.FindRepoRoot()).Should().BeTrue("run 'pnpm cadence generate-cli-docs'");
    }

    [Fact]
    public void ReplaceRegion_RewritesOnlyBetweenTheMarkers_AndRejectsAMissingRegion()
    {
        string document = $"before\n{GateDocs.BeginMarker}\nold\n{GateDocs.EndMarker}\nafter\n";

        GateDocs.ReplaceRegion(document, "new\n").Should()
            .Be($"before\n{GateDocs.BeginMarker}\n\nnew\n\n{GateDocs.EndMarker}\nafter\n");
        GateDocs.ReplaceRegion("no markers\n", "new").Should().BeNull();
    }

    [Fact]
    public void RenderTable_PadsColumnsAndEscapesPipes()
    {
        GateDocs.RenderTable(["A", "Long header"], [["`a|b`", "x"]]).Should().Be(
            "| A      | Long header |\n" +
            "| ------ | ----------- |\n" +
            "| `a\\|b` | x           |\n");
    }

    [Fact]
    public void SchemaDocGate_InPreCommitMode_SkipsWhenNoModelFilesStaged()
    {
        var gate = QualityGates.Find("schema-doc")!;
        var context = Context(files: ["apps/web/src/App.vue", "docs/guide.md"], preCommit: true);

        gate.Run(context).Should().BeTrue("pre-commit hook skips schema documentation check when staged files do not affect EF Core models");
    }

    [Fact]
    public void SchemaDocGate_InPreCommitMode_RunsWhenModelFilesStaged()
    {
        var gate = QualityGates.Find("schema-doc")!;
        var context = Context(files: ["apps/econsent/src/Domain/EconsentEntities.cs"], preCommit: true);

        gate.Run(context).Should().BeFalse("pre-commit hook executes schema documentation drift check when EF Core entity files are staged");
    }

    [Fact]
    public void DuplicationGate_InPreCommitMode_RejectsFrontendAndStylesheetDuplicates()
    {
        string cssAliases = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"  --alias-{i}: var(--semantic-token-{i});"));
        Write("apps/web/src/style.css", $":root {{\n{cssAliases}\n}}\n");
        Write("apps/subject-portal/src/style.css", $":root {{\n{cssAliases}\n}}\n");

        var gate = QualityGates.Find("duplication")!;
        var context = Context(files: ["apps/web/src/style.css", "apps/subject-portal/src/style.css"], preCommit: true);

        gate.Run(context).Should().BeFalse("duplication gate rejects staged CSS duplicates over 15 normalized lines without style.css exemptions");

        string jsLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const val{i} = input + {i};"));
        Write("apps/web/src/dup.js", $"export function compute(input) {{\n{jsLines}\n  return val18;\n}}\n");
        Write("apps/subject-portal/src/dup.js", $"export function compute(input) {{\n{jsLines}\n  return val18;\n}}\n");

        var jsContext = Context(files: ["apps/web/src/dup.js", "apps/subject-portal/src/dup.js"], preCommit: true);
        gate.Run(jsContext).Should().BeFalse("duplication gate rejects staged JavaScript duplicates over 15 normalized lines");

        string tsLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const typedVal{i}: number = seed + {i};"));
        Write("apps/web/src/composables/dup.ts", $"export function computeTyped(seed: number): number {{\n{tsLines}\n  return typedVal18;\n}}\n");
        Write("apps/subject-portal/src/composables/dup.ts", $"export function computeTyped(seed: number): number {{\n{tsLines}\n  return typedVal18;\n}}\n");

        var tsContext = Context(files: ["apps/web/src/composables/dup.ts", "apps/subject-portal/src/composables/dup.ts"], preCommit: true);
        gate.Run(tsContext).Should().BeFalse("duplication gate rejects staged TypeScript duplicates over 15 normalized lines");

        string vueLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  <p class=\"row-{i}\">{{{{ item.label{i} }}}}</p>"));
        Write("apps/web/src/views/DupA.vue", $"<template>\n{vueLines}\n</template>\n");
        Write("apps/web/src/views/DupB.vue", $"<template>\n{vueLines}\n</template>\n");

        var vueContext = Context(files: ["apps/web/src/views/DupA.vue", "apps/web/src/views/DupB.vue"], preCommit: true);
        gate.Run(vueContext).Should().BeFalse("duplication gate rejects staged Vue component duplicates over 15 normalized lines");
    }

    [Fact]
    public void DuplicationGate_EnforcesFrontendExtensions_HonorsShrinkOnlyBaseline_AndPrunesStaleEntries()
    {
        var gate = QualityGates.Find("duplication")!;

        string dtsLines = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"  readonly field{i}: string;"));
        Write("apps/web/src/types/a.d.ts", $"export interface AlphaDto {{\n{dtsLines}\n}}\n");
        Write("apps/web/src/types/b.d.ts", $"export interface BetaDto {{\n{dtsLines}\n}}\n");
        gate.Run(Context(files: ["apps/web/src/types/a.d.ts", "apps/web/src/types/b.d.ts"]))
            .Should().BeTrue("ambient .d.ts type declarations are excluded from duplication enforcement");

        string tsLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const step{i} = seed + {i};"));
        Write("apps/web/src/utils/calcA.ts", $"export function calcA(seed: number): number {{\n{tsLines}\n  return step18;\n}}\n");
        Write("apps/web/src/utils/calcB.ts", $"export function calcB(seed: number): number {{\n{tsLines}\n  return step18;\n}}\n");
        gate.Run(Context(files: ["apps/web/src/utils/calcA.ts", "apps/web/src/utils/calcB.ts"]))
            .Should().BeFalse("unbaselined TypeScript duplicates over 15 lines must fail");

        string cssRules = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  --panel-space-{i}: {i * 4}px;"));
        Write("apps/web/src/panelA.css", $".panel {{\n{cssRules}\n}}\n");
        Write("apps/web/src/panelB.css", $".panel {{\n{cssRules}\n}}\n");
        gate.Run(Context(files: ["apps/web/src/panelA.css", "apps/web/src/panelB.css"]))
            .Should().BeFalse("non-exempt CSS duplicates over 15 lines must fail");

        string vueLines = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const row{i} = props.offset + {i};"));
        string vueA = $"<script setup lang=\"ts\">\nfunction renderRows(props: {{ offset: number }}) {{\n{vueLines}\n  return row18;\n}}\n</script>\n";
        string vueB = $"<script setup lang=\"ts\">\nfunction renderRows(props: {{ offset: number }}) {{\n{vueLines}\n  return row18;\n}}\n</script>\n";
        Write("apps/web/src/views/ViewA.vue", vueA);
        Write("apps/web/src/views/ViewB.vue", vueB);

        var vueScope = Context(files: ["apps/web/src/views/ViewA.vue", "apps/web/src/views/ViewB.vue"]);
        gate.Run(vueScope).Should().BeFalse("unbaselined Vue duplicates over 15 lines must fail");

        var summary = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "duplication_summary.json")))!;
        string[] vueKeys = [.. summary["duplicates"]!.AsArray()
            .Select(n => n!["key"]!.GetValue<string>())
            .Where(k => k.Contains("ViewA.vue", StringComparison.Ordinal))];
        vueKeys.Should().NotBeEmpty();

        Cadence.ToolingCore.Services.DuplicationDetectorService.WriteBaseline(_root, vueKeys);
        gate.Run(vueScope).Should().BeTrue("baselined Vue clone windows pass");

        // Shifting lines in ViewA.vue without changing the duplicated block body keeps the content-hash key valid.
        Write("apps/web/src/views/ViewA.vue", "<!-- header banner -->\n\n" + vueA);
        gate.Run(vueScope).Should().BeTrue("content-hashed baseline keys survive line-number shifts");

        // Refactoring ViewB.vue removes the clone, turning the baseline entries stale until pruned.
        Write("apps/web/src/views/ViewB.vue", "<script setup lang=\"ts\">\nconst simplified = true;\n</script>\n");
        gate.Run(vueScope).Should().BeFalse("stale duplication baseline entries must fail closed until pruned");

        string? previousUpdate = Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE");
        try
        {
            Environment.SetEnvironmentVariable("CADENCE_UPDATE_BASELINE", "1");

            // Unbaselined TypeScript duplicates must still fail even when CADENCE_UPDATE_BASELINE=1 is set.
            gate.Run(Context(files: ["apps/web/src/utils/calcA.ts", "apps/web/src/utils/calcB.ts"]))
                .Should().BeFalse("CADENCE_UPDATE_BASELINE=1 may only prune stale entries and must never baseline new duplicates");

            gate.Run(vueScope).Should().BeTrue("CADENCE_UPDATE_BASELINE=1 prunes resolved baseline entries");
            File.Exists(Path.Combine(_root, Cadence.ToolingCore.Services.DuplicationDetectorService.BaselineFileName))
                .Should().BeFalse("emptied .duplication-baseline.txt is deleted automatically");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CADENCE_UPDATE_BASELINE", previousUpdate);
        }
    }

    [Fact]
    public void DemoManifestGate_InPreCommitMode_SkipsUnrelatedFiles_AndRunsWhenSeedOrFixtureFilesStaged()
    {
        var gate = QualityGates.Find("demo-manifest")!;
        gate.TakesFiles.Should().BeTrue();
        gate.PreCommit.Should().Be(PreCommitMode.Quick);

        gate.Run(Context(files: ["apps/web/src/App.vue", "docs/guide.md"], preCommit: true))
            .Should().BeTrue("pre-commit hook skips demo-manifest when staged files do not touch demo seed or fixture inputs");

        gate.Run(Context(files: ["packages/demo-fixtures/demo-seed.json"], preCommit: true))
            .Should().BeFalse("pre-commit hook runs demo-manifest when demo-seed.json is staged and fixture inputs are missing or drifted");
    }

    [Fact]
    public void BranchFreshnessAndTestSuiteGates_SkipPreCommit_AndBranchFreshnessPassesWhenOriginMainIsAncestor()
    {
        QualityGates.Find("branch-freshness")!.PreCommit.Should().Be(PreCommitMode.Skipped);
        QualityGates.Find("architecture-tests")!.PreCommit.Should().Be(PreCommitMode.Quick);
        QualityGates.Find("unit-tests")!.PreCommit.Should().Be(PreCommitMode.Skipped);

        Write("README.md", "# Cadence\n");
        CommitOnMainThenBranch();
        Write("README.md", "# Cadence feature work\n");
        Git("commit", "-q", "-am", "feature commit");

        QualityGates.Find("branch-freshness")!.Run(Context())
            .Should().BeTrue("feature branch descending from origin/main satisfies branch-freshness");

        Git("checkout", "-q", "main");
        Write("README.md", "# Cadence advanced on main\n");
        Git("commit", "-q", "-am", "advance main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("checkout", "-q", "feature");

        QualityGates.Find("branch-freshness")!.Run(Context(pushBefore: "0000000000000000000000000000000000000000"))
            .Should().BeTrue("push-scoped CI runs (--push-before) skip branch-freshness so historical tag and release pushes succeed");
        QualityGates.Find("branch-freshness")!.Run(Context(baseRef: "refs/remotes/origin/release/1.0"))
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
        Write("src/Sample.cs", "internal static class Sample {}\n");
        Write(".secrets.baseline", unnormalized);

        var gate = QualityGates.Find("clean-secrets")!;
        gate.Run(Context()).Should().BeFalse("unnormalized .secrets.baseline must fail clean-secrets check");
        File.ReadAllText(Path.Combine(_root, ".secrets.baseline")).Should().Be(
            unnormalized,
            "cadence check clean-secrets must never mutate .secrets.baseline on disk");

        Cadence.ToolingCore.Services.SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: _root).Should().Be(0);
        gate.Run(Context()).Should().BeTrue("normalized .secrets.baseline passes clean-secrets check");
    }

    [Fact]
    public void TrailingWhitespaceGate_EnforcesLegacyMarkdownSpecsInUnscopedAndPreCommitModes()
    {
        Write("docs/SRS.md", "# Software Requirements Specification   \n");
        var gate = QualityGates.Find("trailing-whitespace")!;

        gate.Run(Context()).Should().BeFalse("unscoped trailing-whitespace check must not exempt docs/SRS.md");
        gate.Run(Context(files: ["docs/SRS.md"], preCommit: true)).Should().BeFalse("pre-commit trailing-whitespace check also rejects trailing spaces");
    }

    [Fact]
    public void DeleteOnlyPreCommit_PreservesEmptyTargetFiles_AndSkipsFullRepoScans()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "openapi"));
        Cadence.ToolingCore.Services.ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot: _root).Should().Be(0);
        Write("obsolete.md", "# Obsolete\n");
        Write("docs/guide.md", "# Guide   \n\nSee [missing](nope.md).\n");
        Write("scratch/notes.txt", "untracked-pattern-violation\n");
        CommitOnMainThenBranch();

        Git("rm", "-q", "obsolete.md");
        Cadence.ToolingCore.Services.PreCommitOpenApiService.GetStagedFiles(_root).Should().BeEmpty();

        string[] nonFileGates = [.. QualityGates.All
            .Where(g => !g.TakesFiles)
            .Select(g => g.Name)];
        QualityGateOrchestrator.RunQualityGates(excludedGates: nonFileGates, preCommit: true, repoRoot: _root)
            .Should().Be(0, "a delete-only commit has zero surviving staged files and must complete all pre-commit file gates without scanning the full repository");
    }

    [Fact]
    public void MergeReadinessGate_FailsWhenBaseRefMissingOrConflicted_AndPassesWhenCleanOrPushScoped()
    {
        var gate = QualityGates.Find("merge-readiness")!;
        QualityGates.Find("check-merge-readiness").Should().BeSameAs(gate);
        gate.PreCommit.Should().Be(PreCommitMode.Skipped);

        Write("apps/one.cs", "class One {}\n");
        CommitOnMainThenBranch();
        Git("update-ref", "-d", "refs/remotes/origin/main");

        gate.Run(Context()).Should().BeFalse("missing origin/main must fail merge-readiness closed");

        Git("checkout", "-q", "main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("checkout", "-q", "feature");
        Write("apps/two.cs", "class Two {}\n");
        Git("add", "apps/two.cs");
        Git("commit", "-q", "-m", "clean feature commit");

        gate.Run(Context()).Should().BeTrue("a non-conflicting branch over origin/main passes merge-readiness");
        gate.Run(Context(pushBefore: "0000000000000000000000000000000000000000"))
            .Should().BeTrue("push-scoped CI runs skip merge-readiness cleanly");
    }

    [Fact]
    public void DotnetFormatGate_UnscopedFolderMode_SkipsExcludedDirectoriesWithoutSolutionAndFailsOnBadSource()
    {
        var gate = QualityGates.Find("dotnet-format")!;
        Write(".editorconfig", "root = true\n[*.cs]\nindent_style = space\nindent_size = 4\n");
        Write("apps/tickets/src/Clean.cs", "namespace Tickets;\n\npublic static class Clean\n{\n    public static int Value => 1;\n}\n");

        const string malformed = "namespace Ignored;\n\npublic static class Bad\n{\n        public static int Value => 1;\n}\n";
        Write("apps/tickets/src/obj/Debug/net10.0/Generated.g.cs", malformed);
        Write("apps/tickets/src/bin/Debug/net10.0/Generated.cs", malformed);
        Write(".cadence/cli-artifacts/Generated.cs", malformed);
        Write("apps/web/node_modules/pkg/Generated.cs", malformed);

        File.Exists(Path.Combine(_root, "Cadence.sln")).Should().BeFalse();
        gate.Run(Context()).Should().BeTrue(
            "unscoped dotnet-format must run in folder mode without Cadence.sln and ignore excluded build/tool directories");

        Write("apps/tickets/src/Malformed.cs", malformed);
        gate.Run(Context()).Should().BeFalse(
            "unscoped folder-mode dotnet-format must fail when a non-excluded C# source file violates .editorconfig whitespace");
    }

    [Fact]
    public void ApiClientsAndVulnerabilitiesGates_AreQuickAndFileScopedInPreCommit()
    {
        var apiClients = QualityGates.Find("api-clients")!;
        apiClients.TakesFiles.Should().BeTrue();
        apiClients.PreCommit.Should().Be(PreCommitMode.Quick);
        apiClients.PreCommitNote.Should().NotBeNullOrWhiteSpace();

        var vulnerabilities = QualityGates.Find("vulnerabilities")!;
        vulnerabilities.TakesFiles.Should().BeTrue();
        vulnerabilities.PreCommit.Should().Be(PreCommitMode.Quick);
        vulnerabilities.PreCommitNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RunQualityGates_FailFastStopsAtFirstFailureInPreCommit_AndReportsAllFailuresWhenUnscoped()
    {
        // Both trailing-whitespace ("note.md   \n") and path-patterns ("scratch/notes.txt") fail.
        // trailing-whitespace runs before path-patterns in QualityGates.All.
        Write("docs/note.md", "# Note   \n");
        Write("scratch/notes.txt", "forbidden directory\n");
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Git("config", "commit.gpgsign", "false");
        Git("add", "docs/note.md", "scratch/notes.txt");

        string[] excluded = [.. QualityGates.All
            .Select(g => g.Name)
            .Where(n => n is not "trailing-whitespace" and not "path-patterns")];

        string preCommitOutput = CaptureConsoleOutput(() =>
            QualityGateOrchestrator.RunQualityGates(excludedGates: excluded, preCommit: true, repoRoot: _root)
                .Should().Be(1));
        preCommitOutput.Should().Contain("[TRAILING WHITESPACE]")
            .And.NotContain("[PATH VIOLATION]", "pre-commit defaults to fail-fast and exits on the first failing gate");

        string explicitNonFailFastPreCommitOutput = CaptureConsoleOutput(() =>
            QualityGateOrchestrator.RunQualityGates(excludedGates: excluded, preCommit: true, repoRoot: _root, failFast: false)
                .Should().Be(1));
        explicitNonFailFastPreCommitOutput.Should().Contain("[TRAILING WHITESPACE]")
            .And.Contain("[PATH VIOLATION]", "--fail-fast=false overrides the pre-commit fail-fast default");

        string fullOutput = CaptureConsoleOutput(() =>
            QualityGateOrchestrator.RunQualityGates(excludedGates: excluded, preCommit: false, repoRoot: _root)
                .Should().Be(1));
        fullOutput.Should().Contain("[TRAILING WHITESPACE]")
            .And.Contain("[PATH VIOLATION]", "unscoped cadence check continues running remaining gates");

        string fullFailFastOutput = CaptureConsoleOutput(() =>
            QualityGateOrchestrator.RunQualityGates(excludedGates: excluded, preCommit: false, repoRoot: _root, failFast: true)
                .Should().Be(1));
        fullFailFastOutput.Should().Contain("[TRAILING WHITESPACE]")
            .And.NotContain("[PATH VIOLATION]", "explicit --fail-fast stops full cadence check on the first failure");
    }

    [Fact]
    public void ArchitectureTestsGate_InPreCommitMode_SkipsDocsAndNonTestFrontendAssets_AndRunsWhenBackendBaselineOrTestsStaged()
    {
        WriteBrokenTestProject("tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj");
        var gate = QualityGates.Find("architecture-tests")!;
        gate.TakesFiles.Should().BeTrue();
        gate.PreCommit.Should().Be(PreCommitMode.Quick);

        gate.Run(Context(
                files:
                [
                    "docs/guide.md",
                    "README.md",
                    "apps/web/src/App.vue",
                    "apps/web/src/styles/tokens.css",
                    "apps/web/src/stores/study.js",
                    "packages/ui/src/Button.vue",
                ],
                preCommit: true))
            .Should().BeTrue("pre-commit hook must skip architecture-tests when only docs or non-test frontend assets are staged");

        gate.Run(Context(files: ["apps/payments/src/Endpoints/PaymentEndpoints.cs"], preCommit: true))
            .Should().BeFalse("pre-commit hook must run architecture-tests when backend C# files are staged");

        gate.Run(Context(files: ["baselines/direct-state-writes/Cadence.Apps.Payments.baseline.txt"], preCommit: true))
            .Should().BeFalse("pre-commit hook must run architecture-tests when sharded ratchet baselines are staged");

        gate.Run(Context(files: ["apps/subject-portal/tests/e2e/portal-accessibility.spec.js"], preCommit: true))
            .Should().BeFalse("pre-commit hook must run architecture-tests when frontend test specs are staged");
    }

    [Fact]
    public void ImportsAndPermissionMatrixGates_CatchReflectionStringsAndUnbaselinedRbacGrants()
    {
        Write(
            "apps/ctms/src/Endpoints/ReflectionBypass.cs",
            "namespace Cadence.Apps.Ctms.Endpoints;\npublic static class ReflectionBypass\n{\n    public static string Sibling => \"Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext\";\n}\n");
        QualityGates.Find("imports")!.Run(Context())
            .Should().BeFalse("cross-service reflection string literals must fail the imports gate when unbaselined");

        PermissionMatrixRatchetService.WriteBaselineShards(_root, PermissionMatrixRatchetService.GetRuntimeGrants());
        var permissionGate = QualityGates.Find("permission-matrix")!;
        permissionGate.Run(Context()).Should().BeTrue("runtime PermissionMatrix grants matching the baseline must pass");

        Write(
            "apps/payments/src/Rbac/ShadowGrant.cs",
            "namespace Cadence.Apps.Payments.Rbac;\npublic static class ShadowGrant\n{\n    public static void Init() => Grant(\"pi\", \"payments_shadow\", \"approve\");\n}\n");
        permissionGate.Run(Context()).Should().BeFalse("unbaselined static Grant(...) calls in service sources must fail permission-matrix");
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private GateContext Context(string[]? files = null, bool preCommit = false, string? baseRef = null, string? pushBefore = null) =>
        new(_root, files, preCommit, BaseRef: baseRef, PushBefore: pushBefore);

    private static string Region(string body) => $"{GateDocs.BeginMarker}\n\n{body.TrimEnd('\n')}\n\n{GateDocs.EndMarker}\n";

    private void WriteDotnetProject(string editorconfig, string source)
    {
        Write(".editorconfig", editorconfig);
        Write("Fixture.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("Bad.cs", source);
        Write("Cadence.sln",
            "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Fixture\", \"Fixture.csproj\", \"{6C0A2C5B-6A43-4D5B-9C59-2B0D0C1E7A11}\"\n" +
            "EndProject\n");
    }

    private void WriteBrokenTestProject(string relativeCsprojPath)
    {
        string projectDir = Path.GetDirectoryName(relativeCsprojPath)!.Replace('\\', '/');
        Write(relativeCsprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <IsTestProject>true</IsTestProject>\n  </PropertyGroup>\n</Project>\n");
        Write($"{projectDir}/Broken.cs", "namespace BrokenFixture;\n\npublic static class Broken\n{\n    public static int Fail( => 1;\n}\n");
    }

    private void CommitOnMainThenBranch()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Git("config", "commit.gpgsign", "false");
        Git("add", "-A");
        Git("commit", "-q", "-m", "main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("checkout", "-q", "-b", "feature");
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
    }
}

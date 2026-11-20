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
        ["api-clients"] = "runs `pnpm --filter web` and `tsc` over the real web workspace; the fixture has no apps/web/tsconfig.json",
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
        ["clean-secrets"] = () =>
        {
            Write(".secrets.baseline", "{ \"results\": ");
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
            return Context(files: [], preCommit: true);
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
        ["public-bundle"] = () =>
        {
            const string validCsp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'; media-src 'self'; worker-src 'self'; connect-src 'self'\">";
            Write("docs/.vitepress/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><p>Portal</p></body></html>\n");
            Write("apps/web/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><img src=\"https://images.unsplash.com/photo-1\"></body></html>\n");
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
        QualityGates.All.Should().HaveCount(30);
        Fixtures().Keys.Should().BeEquivalentTo(QualityGates.All.Select(g => g.Name));
        FailsClosedOnly.Keys.Should().BeSubsetOf(QualityGates.All.Select(g => g.Name));
    }

    [Fact]
    public void RunQualityGates_SkipsExcludedGatesByNameOrAlias_AndFailsClosedOnInvalidExcludeUsage()
    {
        Write("scratch/notes.txt", "left behind\n");
        string[] unrelatedGates = QualityGates.All
            .Select(g => g.Name)
            .Where(n => n is not "dependencies" and not "path-patterns")
            .ToArray();

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
    public void DuplicationGate_InPreCommitMode_IgnoresNonCSharpDuplicates()
    {
        string cssAliases = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"  --alias-{i}: var(--semantic-token-{i});"));
        Write("apps/web/src/style.css", $":root {{\n{cssAliases}\n}}\n");
        Write("apps/subject-portal/src/style.css", $":root {{\n{cssAliases}\n}}\n");

        var gate = QualityGates.Find("duplication")!;
        var context = Context(files: ["apps/web/src/style.css", "apps/subject-portal/src/style.css"], preCommit: true);

        gate.Run(context).Should().BeTrue("duplication gate fails only when a duplicate involves a C# file, even when pre-commit passes staged files");
    }

    private GateContext Context(string[]? files = null, bool preCommit = false) => new(_root, files, preCommit);

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

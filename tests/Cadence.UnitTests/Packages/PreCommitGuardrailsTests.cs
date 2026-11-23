using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class PreCommitGuardrailsTests : IDisposable
{
    private static readonly string PrivateKeyHeader = "-----BEGIN " + "PRIVATE KEY-----";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cadence-guardrails-test-{Guid.NewGuid():N}");

    public PreCommitGuardrailsTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_tempDir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WorkflowSecurityGate_AnswersActionPinningAliases_AndScopesToStagedWorkflowFiles()
    {
        var gate = QualityGates.Find("workflow-security")!;
        QualityGates.Find("action-pinning").Should().BeSameAs(gate);
        QualityGates.Find("check-action-pinning").Should().BeSameAs(gate);
        gate.TakesFiles.Should().BeTrue();

        Write("README.md", "# Docs change\n");
        Write(".github/workflows/ci.yml",
            """
            name: CI
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
            """);

        gate.Run(new GateContext(_tempDir, ["README.md"], PreCommit: true)).Should().BeTrue(
            "staging non-workflow files must skip untouched workflow files in pre-commit mode");
        gate.Run(new GateContext(_tempDir, [".github/workflows/ci.yml"], PreCommit: true)).Should().BeFalse(
            "staging an unpinned workflow file must fail workflow-security in pre-commit mode");
    }

    [Fact]
    public void YamlValidator_AcceptsAllRepositoryYamlFiles()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();

        YamlSyntaxValidatorService.ValidateYaml(repoRoot: repoRoot).Should().Be(0);
    }

    [Theory]
    [InlineData("jobs:\n\tbuild:\n    runs-on: ubuntu-latest\n", "Tabs are not allowed")]
    [InlineData("name: \"unterminated\non: push\n", "Unclosed double-quoted")]
    [InlineData("name: 'unterminated\non: push\n", "Unclosed single-quoted")]
    [InlineData("name: \"bad\\qescape\"\n", "Invalid escape sequence")]
    [InlineData("foo: bar: baz\n", "Nested mapping colon")]
    [InlineData("jobs:\n  build: 1\n  build: 2\n", "Duplicate mapping key")]
    [InlineData("items: [1, 2\n", "Unclosed flow collection")]
    [InlineData("items: [1, 2}\n", "Mismatched flow bracket")]
    [InlineData("parent:\n    child1: 1\n  bad_dedent: 2\n", "Bad indentation")]
    public void YamlValidator_RejectsMalformedYaml(string content, string expectedError)
    {
        var errors = YamlSyntaxValidatorService.ValidateContent(content, "broken.yml");

        errors.Should().NotBeEmpty();
        string.Join("\n", errors).Should().Contain(expectedError);
    }

    [Fact]
    public void FileHygiene_DetectsTrailingWhitespace_AndMissingFinalNewline_WhileSkippingBinaries()
    {
        Write("clean.txt", "line one\r\nline two\r\n");
        File.WriteAllBytes(Path.Combine(_tempDir, "binary.dat"), [0x00, 0x20, 0x20]);

        FileHygieneValidatorService.ValidateTrailingWhitespace(repoRoot: _tempDir).Should().Be(0);
        FileHygieneValidatorService.ValidateEndOfFileNewline(repoRoot: _tempDir).Should().Be(0);

        Write("trailing.md", "# Heading \n");
        FileHygieneValidatorService.ValidateTrailingWhitespace(["trailing.md"], _tempDir).Should().Be(1);

        Write("no-eof.json", "{ \"ok\": true }");
        FileHygieneValidatorService.ValidateEndOfFileNewline(["no-eof.json"], _tempDir).Should().Be(1);
    }

    [Fact]
    public void PosixProcessLauncher_ExecutesShebangLessScriptsViaShFallback()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string shimPath = Path.Combine(_tempDir, "pnpm-shim-no-shebang");
        File.WriteAllText(shimPath, "printf '%s:%s' \"$1\" \"$TEST_TOKEN\"\n");
        File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var psi = new ProcessStartInfo(shimPath)
        {
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--version");
        psi.Environment["TEST_TOKEN"] = "10.28.2";

        using var process = PosixProcessLauncher.Start(psi);
        process.Should().NotBeNull();
        string stdout = process!.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, stderr);
        stdout.Should().Be("--version:10.28.2");
    }

    [Fact]
    public void SecretsBaselineCleaner_WritesCanonicalExcludeFilter_AndDeletesLegacyExcludeObject()
    {
        string baselinePath = Write(".secrets.baseline",
            """
            {
              "version": "1.5.0",
              "generated_at": "2026-01-01T00:00:00Z",
              "plugins_used": [{ "name": "PrivateKeyDetector" }],
              "filters_used": [
                {
                  "path": "detect_secrets.filters.regex.should_exclude_file",
                  "pattern": ["pnpm-lock\\.yaml"]
                }
              ],
              "results": {
                "pnpm-lock.yaml": [{ "type": "Hex High Entropy String", "hashed_secret": "abc", "line_number": 10 }],
                "src/Sample.cs": [{ "type": "Hex High Entropy String", "hashed_secret": "def", "line_number": 42 }]
              },
              "exclude": {
                "files": "legacy",
                "lines": null
              }
            }
            """);

        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir).Should().Be(0);

        var parsed = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject();
        parsed.ContainsKey("generated_at").Should().BeFalse();
        parsed.ContainsKey("exclude").Should().BeFalse("detect-secrets v1.5.0 ignores the legacy top-level exclude key");

        var filter = parsed["filters_used"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["path"]?.GetValue<string>() == "detect_secrets.filters.regex.should_exclude_file");
        filter["pattern"]!.AsArray().Select(p => p!.GetValue<string>())
            .Should().Equal(SecretsBaselineCleanerService.CanonicalExcludePattern);

        var results = parsed["results"]!.AsObject();
        results.ContainsKey("pnpm-lock.yaml").Should().BeFalse();
        results["src/Sample.cs"]!.AsArray()[0]!.AsObject().ContainsKey("line_number").Should().BeFalse();
    }

    [Fact]
    public void DetectSecretsGate_FailsClosedWhenToolIsUnavailable()
    {
        Write(".secrets.baseline", "{ \"version\": \"1.5.0\", \"plugins_used\": [], \"filters_used\": [], \"results\": {} }\n");
        Write("README.md", "# Clean\n");

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = _tempDir,
            [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
        };

        DetectSecretsGateService.ResolveTool(_tempDir, key => env.GetValueOrDefault(key)).Should().BeNull();
        DetectSecretsGateService.Scan(_tempDir, ["README.md"], preCommit: true, getEnvironmentVariable: key => env.GetValueOrDefault(key))
            .Should().Be(1, "missing detect-secrets must fail closed instead of skipping");
    }

    [Fact]
    public void DetectSecretsGate_BlocksUnbaselinedSecret_AndLeavesBaselineUnchanged()
    {
        InitGitRepo();
        string baselineContent =
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": [],\n  \"results\": {}\n}\n";
        string baselinePath = Write(".secrets.baseline", baselineContent);
        Write("clean.txt", "nothing sensitive here\n");
        Write("secret.txt", $"{PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");

        string stubBinDir = CreateDetectSecretsHookStub();
        string systemPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var stubEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = string.IsNullOrEmpty(systemPath) ? stubBinDir : $"{stubBinDir}{Path.PathSeparator}{systemPath}",
            [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
        };

        DetectSecretsGateService.Scan(_tempDir, ["clean.txt"], preCommit: true, key => stubEnv.GetValueOrDefault(key)).Should().Be(0);
        File.ReadAllText(baselinePath).Should().Be(baselineContent);

        DetectSecretsGateService.Scan(_tempDir, ["secret.txt"], preCommit: true, key => stubEnv.GetValueOrDefault(key)).Should().Be(1);
        File.ReadAllText(baselinePath).Should().Be(baselineContent);

        var realTool = DetectSecretsGateService.ResolveTool(
            QualityGateOrchestrator.FindRepoRoot(),
            key => key == DetectSecretsGateService.DisableUvxFallbackEnvVar ? "1" : Environment.GetEnvironmentVariable(key));
        if (realTool is not null && Path.GetDirectoryName(realTool.FileName) is { Length: > 0 } realBinDir)
        {
            var realEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = string.IsNullOrEmpty(systemPath) ? realBinDir : $"{realBinDir}{Path.PathSeparator}{systemPath}",
                [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
            };

            DetectSecretsGateService.Scan(_tempDir, ["clean.txt"], preCommit: true, key => realEnv.GetValueOrDefault(key)).Should().Be(0);
            File.ReadAllText(baselinePath).Should().Be(baselineContent);

            DetectSecretsGateService.Scan(_tempDir, ["secret.txt"], preCommit: true, key => realEnv.GetValueOrDefault(key)).Should().Be(1);
            File.ReadAllText(baselinePath).Should().Be(baselineContent);
        }
    }

    [Fact]
    public void CiPlan_DetectSecretsStep_DelegatesToCadenceCli_AndBaselineRetainsCanonicalExcludeFilter()
    {
        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var step = securityJob.Steps.Single(s => s.Name == "Run detect-secrets against the baseline");

        step.RequiredTool.Should().Be("detect-secrets-hook");
        var cmd = step.Commands.Should().ContainSingle().Subject;
        cmd.FileName.Should().Be("node");
        cmd.Arguments.Should().Equal("scripts/run-cli.js", "check", "detect-secrets");
        cmd.ForwardsBaseRef.Should().BeTrue();

        string repoBaselinePath = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), ".secrets.baseline");
        var repoBaseline = JsonNode.Parse(File.ReadAllText(repoBaselinePath))!.AsObject();
        repoBaseline.ContainsKey("exclude").Should().BeFalse();
        var filter = repoBaseline["filters_used"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["path"]?.GetValue<string>() == "detect_secrets.filters.regex.should_exclude_file");
        filter["pattern"]!.AsArray().Select(p => p!.GetValue<string>())
            .Should().Equal(SecretsBaselineCleanerService.CanonicalExcludePattern);
    }

    [Fact]
    public void LocalCiRunner_DetectSecretsStep_ForwardsCustomBaseRef_AndPropagatesCliExitCode()
    {
        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var step = securityJob.Steps.Single(s => s.Name == "Run detect-secrets against the baseline");
        var onlyStepJob = new CiJob("security", "Security", [step]);
        var options = new LocalCiOptions(AllowMissingTools: false, AllowDbSkip: false, BaseRef: "origin/release/1.0");

        var capturedArgs = new List<string>();
        int simulatedExitCode = 0;

        var env = new LocalCiEnvironment
        {
            RepoRoot = _tempDir,
            Run = (command, extra, _) =>
            {
                capturedArgs = [.. command.Arguments, .. extra];
                return simulatedExitCode;
            },
            IsToolOnPath = _ => true,
            ProbePostgres = () => new CiPostgresStatus(true, "ok"),
            FindFiles = (_, _) => [],
            DeleteDirectory = _ => { },
            FindSkippedDatabaseTests = _ => [],
        };

        var passResult = LocalCiRunner.Run([onlyStepJob], options, env).Single();
        passResult.Outcome.Should().Be(CiOutcome.Passed);
        capturedArgs.Should().Equal("scripts/run-cli.js", "check", "detect-secrets", "--base", "origin/release/1.0");

        simulatedExitCode = 1;
        var failResult = LocalCiRunner.Run([onlyStepJob], options, env).Single();
        failResult.Outcome.Should().Be(CiOutcome.Failed);
    }

    [Fact]
    public void DetectSecretsGate_ScansTrackedFilesOnCleanMain_AndCoversMultiCommitAndZeroShaPushes()
    {
        InitGitRepo();
        Write(".gitignore", "stub-bin/\n");
        Write(".secrets.baseline",
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": [],\n  \"results\": {}\n}\n");
        Write("apps/leak.txt", $"{PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "initial main with leaked secret");
        RunGit("update-ref", "refs/remotes/origin/main", "HEAD");

        string stubBinDir = CreateDetectSecretsHookStub();
        string systemPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var stubEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = string.IsNullOrEmpty(systemPath) ? stubBinDir : $"{stubBinDir}{Path.PathSeparator}{systemPath}",
            [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
        };

        // Clean main (HEAD == origin/main) must fall back to git ls-files -z instead of scanning 0 files.
        DetectSecretsGateService.Scan(_tempDir, targetFiles: null, preCommit: false, key => stubEnv.GetValueOrDefault(key))
            .Should().Be(1, "HEAD == origin/main must scan tracked files rather than exiting 0 with 0 files");

        // Fix main, then create a 2-commit push where Commit 1 leaks a secret and Commit 2 only edits README.md.
        Write("apps/leak.txt", "clean content\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "clean main");
        RunGit("update-ref", "refs/remotes/origin/main", "HEAD");
        DetectSecretsGateService.Scan(_tempDir, targetFiles: null, preCommit: false, key => stubEnv.GetValueOrDefault(key))
            .Should().Be(0);

        string beforeSha = CaptureGit("rev-parse", "HEAD").Trim();
        Write("apps/commit1-secret.txt", $"{PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "commit 1 adds secret");

        Write("README.md", "# Commit 2 clean doc update\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "commit 2 clean readme");
        RunGit("tag", "v2.0.0");

        DetectSecretsGateService.Scan(_tempDir, targetFiles: null, preCommit: false, key => stubEnv.GetValueOrDefault(key), pushBefore: beforeSha)
            .Should().Be(1, "multi-commit pushBefore..HEAD must catch secrets in earlier pushed commits");
        DetectSecretsGateService.Scan(_tempDir, targetFiles: null, preCommit: false, key => stubEnv.GetValueOrDefault(key),
            pushBefore: "0000000000000000000000000000000000000000")
            .Should().Be(1, "zero-SHA push must fall back to tracked files");
        DetectSecretsGateService.Scan(_tempDir, targetFiles: null, preCommit: false, key => stubEnv.GetValueOrDefault(key), pushBefore: "v2.0.0")
            .Should().Be(1, "tag push must fall back to tracked files");
    }

    [Fact]
    public void FrontendQualityGates_FailClosedWhenWorkspaceDependenciesMissing()
    {
        Write("apps/web/src/sample.ts", "export const x = 1;\n");

        FrontendQualityGateService.CheckPrettier(_tempDir, ["apps/web/src/sample.ts"], preCommit: true).Should().Be(1);
        FrontendQualityGateService.CheckEslint(_tempDir, ["apps/web/src/sample.ts"], preCommit: true).Should().Be(1);
    }

    [Fact]
    public void VulnerabilityAuditor_BuildPipAuditCandidates_ChecksCadenceVenvBeforeLegacyVenv()
    {
        string binSubdir = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        string exeName = OperatingSystem.IsWindows() ? "pip-audit.exe" : "pip-audit";
        string cadenceVenvExe = Write(Path.Combine(".cadence", "venv", binSubdir, exeName), "");
        string legacyVenvExe = Write(Path.Combine(".venv", binSubdir, exeName), "");

        var candidates = VulnerabilityAuditorService.BuildPipAuditCandidates(_tempDir);

        candidates.Select(c => c.FileName).Take(2).Should().Equal(cadenceVenvExe, legacyVenvExe);
    }

    [Fact]
    public void PreCommitHook_FailsClosedWhenNodeOrDotnetMissingFromPath()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string hookPath = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), ".githooks", "pre-commit");
        string emptyBin = Path.Combine(_tempDir, "empty-bin");
        string isolatedHome = Path.Combine(_tempDir, "isolated-home");
        Directory.CreateDirectory(emptyBin);
        Directory.CreateDirectory(isolatedHome);

        (int noNodeExit, string noNodeErr) = RunHook(hookPath, emptyBin, isolatedHome);
        noNodeExit.Should().Be(1);
        noNodeErr.Should().Contain("node");

        string fakeNode = Path.Combine(emptyBin, "node");
        File.WriteAllText(fakeNode, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(fakeNode, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int noDotnetExit, string noDotnetErr) = RunHook(hookPath, emptyBin, isolatedHome);
        noDotnetExit.Should().Be(1);
        noDotnetErr.Should().Contain("dotnet");
    }

    [Fact]
    public void GetStagedFiles_DuringMergeCommit_ExcludesVerbatimUpstreamFilesAndKeepsMergedEdits()
    {
        InitGitRepo();
        Write("shared.txt", "base\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "init");

        RunGit("checkout", "-q", "-b", "feature");
        Write("feature-only.txt", "feature\n");
        Write("shared.txt", "feature-change\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "feature work");

        RunGit("checkout", "-q", "main");
        Write("upstream-only.txt", "upstream\n");
        Write("shared.txt", "upstream-change\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "upstream work");

        RunGit("checkout", "-q", "feature");
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("merge");
        psi.ArgumentList.Add("--no-commit");
        psi.ArgumentList.Add("main");
        using (var mergeProc = Process.Start(psi)!)
        {
            mergeProc.StandardOutput.ReadToEnd();
            mergeProc.StandardError.ReadToEnd();
            mergeProc.WaitForExit();
        }

        Write("shared.txt", "resolved-both-sides\n");
        RunGit("add", "shared.txt");

        var staged = PreCommitOpenApiService.GetStagedFiles(_tempDir);
        staged.Should().Equal("shared.txt");

        var changedSinceBase = PreCommitOpenApiService.TryGetChangedFilesSinceBase(_tempDir, "main");
        changedSinceBase.Should().Equal("feature-only.txt", "shared.txt");
    }

    [Fact]
    public void CanonicalExcludePattern_ExcludesGeneratedEfMigrationMetadata_WhileScanningMigrationCode()
    {
        SecretsBaselineCleanerService.IsExcludedFile(
            "packages/kernel/src/Cadence.Kernel.Persistence/Migrations/20261120152149_CommercialSubscriptionsAndEntitlements.Designer.cs")
            .Should().BeTrue();
        SecretsBaselineCleanerService.IsExcludedFile(
            "packages/kernel/src/Cadence.Kernel.Persistence/Migrations/idempotent_migration.sql")
            .Should().BeTrue();
        SecretsBaselineCleanerService.IsExcludedFile(
            "packages/kernel/src/Cadence.Kernel.Persistence/Migrations/20261120152149_CommercialSubscriptionsAndEntitlements.cs")
            .Should().BeFalse();
        SecretsBaselineCleanerService.IsExcludedFile("apps/web/dist/assets/index.js")
            .Should().BeFalse("ADR-2497 keeps dist/ unexcluded from secret baseline scanning");
    }

    [Fact]
    public void GitIndexReader_StreamsStagedBlobsViaCatFileBatch_WithoutIndexLock_AndMaterializesWorkspace()
    {
        InitGitRepo();
        Write("src/App.cs", "namespace Demo;\npublic static class App { public const int Version = 1; }\n");
        string scriptDiskPath = Write("scripts/run.sh", "#!/bin/sh\necho staged-v1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptDiskPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        RunGit("add", ".");

        // Mutate working tree and delete script on disk to verify reader serves strictly from the Git index.
        Write("src/App.cs", "namespace Demo;\npublic static class App { public const int Version = 999; }\n");
        File.Delete(scriptDiskPath);

        string workspaceRoot;
        using (var reader = GitIndexReader.TryOpen(_tempDir))
        {
            reader.Should().NotBeNull();
            reader!.HasAuthoritativeIndex.Should().BeTrue();
            File.Exists(Path.Combine(_tempDir, ".git", "index.lock")).Should().BeFalse(
                "GitIndexReader must use --no-optional-locks without acquiring .git/index.lock");

            reader.FileExists("src/App.cs").Should().BeTrue();
            reader.FileExists("scripts/run.sh").Should().BeTrue("deleted working-tree file remains staged in index");
            reader.DirectoryExists("scripts").Should().BeTrue();

            reader.TryReadText("src/App.cs", out string appText).Should().BeTrue();
            appText.Should().Contain("Version = 1").And.NotContain("999");

            reader.TryReadLines("scripts/run.sh", out string[] scriptLines).Should().BeTrue();
            scriptLines.Should().Equal("#!/bin/sh", "echo staged-v1");

            using var workspace = reader.Materialize(["src/App.cs", "scripts/run.sh"], createGitMarker: true);
            workspaceRoot = workspace.RootPath;
            Directory.Exists(Path.Combine(workspaceRoot, ".git")).Should().BeTrue();
            File.ReadAllText(Path.Combine(workspaceRoot, "src", "App.cs")).Should().Contain("Version = 1");
            File.ReadAllText(Path.Combine(workspaceRoot, "scripts", "run.sh")).Should().Contain("staged-v1");
        }

        Directory.Exists(workspaceRoot).Should().BeFalse("StagedBlobWorkspace.Dispose must remove temporary files");
    }

    [Fact]
    public void FileScopedPreCommitGates_BlockStagedViolations_WhenUnstagedWorkingTreeEditsMaskThem()
    {
        InitGitRepo();

        const string baselineJson =
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": [],\n  \"results\": {}\n}\n";
        string dupBody = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"        int item{i} = Calculate({i}, {i + 3});"));
        string boolLiteralExpr = "== " + "true";

        Write(".secrets.baseline", baselineJson);
        Write(".deid-baseline.txt", "# empty baseline\n");
        Write("docs/trailing.txt", "staged trailing space   \n");
        Write("docs/no-eof.txt", "staged missing newline");
        Write(".github/workflows/ci.yml",
            "name: \"unterminated\non: push\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n");
        Write("config/secret.txt", $"{PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");
        Write("apps/alpha/src/Phi.cs", "namespace Alpha;\npublic static class Phi { public static string Ssn => \"000-12-3456\"; }\n");
        Write("apps/alpha/src/Query.cs", $"namespace Alpha;\npublic static class Query {{ public static bool Bad(bool x) => x {boolLiteralExpr}; }}\n");
        Write("apps/alpha/src/DupOne.cs", $"namespace Alpha;\nclass DupOne {{\n    void Run() {{\n{dupBody}\n    }}\n}}\n");
        Write("apps/beta/src/DupTwo.cs", $"namespace Beta;\nclass DupTwo {{\n    void Go() {{\n{dupBody}\n    }}\n}}\n");
        Write("tmp/forbidden.txt", "staged forbidden path\n");
        string reqDocPath = Write("docs/req.md", "# Requirements\n\nCovers @req:prd-sys-001 and [missing](nope.md).\n");

        RunGit("add", ".");

        // Now overwrite EVERY working-tree file with completely clean content (and delete tmp/forbidden.txt from disk)!
        const string cleanWorkingTreeReqDoc = "# Requirements\n\nCovers @req:PRD-SYS-001 cleanly.\n";
        Write("docs/trailing.txt", "clean working tree\n");
        Write("docs/no-eof.txt", "clean working tree\n");
        Write(".github/workflows/ci.yml",
            "name: CI\non: push\npermissions:\n  contents: read\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7\n        with:\n          persist-credentials: false\n");
        Write("config/secret.txt", "clean placeholder\n");
        Write("apps/alpha/src/Phi.cs", "namespace Alpha;\npublic static class Phi { public static string Safe => \"ok\"; }\n");
        Write("apps/alpha/src/Query.cs", "namespace Alpha;\npublic static class Query { public static bool Ok(bool x) => x; }\n");
        Write("apps/alpha/src/DupOne.cs", "namespace Alpha;\nclass DupOne { }\n");
        Write("apps/beta/src/DupTwo.cs", "namespace Beta;\nclass DupTwo { }\n");
        Write("docs/req.md", cleanWorkingTreeReqDoc);
        File.Delete(Path.Combine(_tempDir, "tmp", "forbidden.txt"));
        Directory.Delete(Path.Combine(_tempDir, "tmp"));

        string stubBinDir = CreateDetectSecretsHookStub();
        string systemPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var stubEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = string.IsNullOrEmpty(systemPath) ? stubBinDir : $"{stubBinDir}{Path.PathSeparator}{systemPath}",
            [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
        };

        string[] stagedFiles = [.. PreCommitOpenApiService.GetStagedFiles(_tempDir)];
        using var reader = GitIndexReader.TryOpen(_tempDir);
        reader.Should().NotBeNull();
        var ctx = new GateContext(_tempDir, stagedFiles, PreCommit: true, IndexReader: reader);

        QualityGates.Find("trailing-whitespace")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("end-of-file")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("yaml")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("workflow-security")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("security")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("deid")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("query-linter")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("duplication")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("path-patterns")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("markdown")!.Run(ctx).Should().BeFalse();
        QualityGates.Find("sanitizer")!.Run(ctx).Should().BeFalse();
        DetectSecretsGateService.Scan(_tempDir, stagedFiles, preCommit: true, key => stubEnv.GetValueOrDefault(key), reader)
            .Should().Be(1, "detect-secrets must scan materialized staged blobs rather than masked working-tree files");

        File.ReadAllText(reqDocPath).Should().Be(cleanWorkingTreeReqDoc,
            "sanitizer in pre-commit mode must report lowercase requirement tags without mutating working-tree Markdown");
    }

    [Fact]
    public void DeletionOnlyAndCleanMergeCommits_PreserveEmptyTargetFiles_AndLeaveUnstagedDocsUntouched()
    {
        InitGitRepo();
        Write("obsolete.txt", "delete me\n");
        string guidePath = Write("docs/guide.md", "# Guide\n\nClean committed doc.\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "initial");

        // Create an upstream branch commit for a clean merge test later.
        RunGit("checkout", "-q", "-b", "upstream");
        Write("upstream-note.txt", "from upstream\n");
        RunGit("add", "upstream-note.txt");
        RunGit("commit", "-q", "-m", "upstream addition");

        // Back on main: stage only a deletion (`git rm`), while leaving unstaged lowercase @req tag in docs/guide.md.
        RunGit("checkout", "-q", "main");
        RunGit("rm", "-q", "obsolete.txt");
        const string dirtyUnstagedDoc = "# Guide\n\nUnstaged draft tag @req:prd-sys-001   \n";
        File.WriteAllText(guidePath, dirtyUnstagedDoc);

        PreCommitOpenApiService.GetStagedFiles(_tempDir).Should().BeEmpty();
        QualityGateOrchestrator.RunQualityGates(gateName: "sanitizer", preCommit: true, repoRoot: _tempDir).Should().Be(0);
        QualityGateOrchestrator.RunQualityGates(gateName: "path-patterns", preCommit: true, repoRoot: _tempDir).Should().Be(0);
        QualityGateOrchestrator.RunQualityGates(gateName: "markdown", preCommit: true, repoRoot: _tempDir).Should().Be(0);
        File.ReadAllText(guidePath).Should().Be(dirtyUnstagedDoc,
            "git rm commits must not scan or rewrite unstaged docs/**/*.md files");

        // Commit the deletion and start a clean --no-commit merge (`MERGE_HEAD` with only verbatim upstream changes).
        File.WriteAllText(guidePath, "# Guide\n\nClean committed doc.\n");
        RunGit("commit", "-q", "-m", "remove obsolete");
        RunGit("merge", "--no-commit", "--no-ff", "upstream");
        File.Exists(Path.Combine(_tempDir, ".git", "MERGE_HEAD")).Should().BeTrue();

        File.WriteAllText(guidePath, dirtyUnstagedDoc);
        PreCommitOpenApiService.GetStagedFiles(_tempDir).Should().BeEmpty();
        QualityGateOrchestrator.RunQualityGates(gateName: "sanitizer", preCommit: true, repoRoot: _tempDir).Should().Be(0);
        QualityGateOrchestrator.RunQualityGates(gateName: "trailing-whitespace", preCommit: true, repoRoot: _tempDir).Should().Be(0);
        File.ReadAllText(guidePath).Should().Be(dirtyUnstagedDoc,
            "clean merge commits must treat empty staged file list as a no-op and leave unstaged docs untouched");
    }

    [Fact]
    public void ActionlintAndHadolintGates_FailClosedWhenMissing_AndValidateStagedBlobsFromCadenceBin()
    {
        InitGitRepo();
        Write(".github/workflows/ci.yml", "name: CI\non: INVALID_WORKFLOW_MARKER\n");
        Write("docker/Dockerfile.api", "FROM ubuntu:24.04\n# INVALID_DOCKERFILE_MARKER\n");
        Write("scripts/check.sh", "#!/usr/bin/env bash\n# INVALID_SHELL_MARKER\n");
        Write(".githooks/pre-commit", "#!/bin/sh\n# INVALID_SHELL_MARKER\n");
        RunGit("add", ".");

        // Mask files on disk with clean content so only staged index inspection catches them.
        Write(".github/workflows/ci.yml", "name: CI\non: push\n");
        Write("docker/Dockerfile.api", "FROM ubuntu:24.04\n");
        Write("scripts/check.sh", "#!/usr/bin/env bash\necho ok\n");
        Write(".githooks/pre-commit", "#!/bin/sh\necho ok\n");

        var emptyPathEnv = new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = _tempDir };
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, getEnvironmentVariable: k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "actionlint gate must fail closed when binary is missing");
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, getEnvironmentVariable: k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "hadolint gate must fail closed when binary is missing");
        CliLinterGateService.ResolveShellcheck(_tempDir, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().BeNull("shellcheck resolution must return null when binary is missing");
        CliLinterGateService.RunShellcheck(_tempDir, [".githooks/pre-commit"], preCommit: true, getEnvironmentVariable: k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "shellcheck gate must fail closed when binary is missing");

        CreateCadenceBinLinterStubs();
        CliLinterGateService.ResolveShellcheck(_tempDir, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().NotBeNull("shellcheck resolution must prefer .cadence/bin");
        using var reader = GitIndexReader.TryOpen(_tempDir);
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "actionlint must validate staged workflow blob even when working tree is clean");
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "hadolint must validate staged Dockerfile blob even when working tree is clean");
        CliLinterGateService.RunShellcheck(_tempDir, ["scripts/check.sh"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "shellcheck must validate staged .sh blob even when working tree is clean");
        CliLinterGateService.RunShellcheck(_tempDir, [".githooks/pre-commit"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "shellcheck must validate staged extensionless shebang hook blob even when working tree is clean");

        // Now stage the clean versions and verify all three pass.
        RunGit("add", ".");
        using var cleanReader = GitIndexReader.TryOpen(_tempDir);
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, cleanReader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(0);
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, cleanReader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(0);
        CliLinterGateService.RunShellcheck(_tempDir, ["scripts/check.sh", ".githooks/pre-commit"], preCommit: true, cleanReader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(0);
    }

    [Fact]
    public void PreCommitHook_GuardsDotnetRootWhenDotnetHomeHasOnlySentinels_AndPreservesDotnetToolsOnPath()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string hookPath = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), ".githooks", "pre-commit");
        string mockUsrBin = Path.Combine(_tempDir, "usr-bin");
        string sentinelHome = Path.Combine(_tempDir, "sentinel-home");
        Directory.CreateDirectory(mockUsrBin);
        Directory.CreateDirectory(Path.Combine(sentinelHome, ".dotnet", "tools"));
        File.WriteAllText(Path.Combine(sentinelHome, ".dotnet", "10.0.100.dotnetFirstUseSentinel"), "");
        File.WriteAllText(Path.Combine(sentinelHome, ".dotnet", "tools", "dotnet-cadence"), "#!/bin/sh\nexit 0\n");

        string envCaptureFile = Path.Combine(_tempDir, "captured-env.txt");
        string fakeDotnet = Path.Combine(mockUsrBin, "dotnet");
        File.WriteAllText(fakeDotnet, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(fakeDotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string fakeNode = Path.Combine(mockUsrBin, "node");
        File.WriteAllText(
            fakeNode,
            $"#!/bin/sh\nprintf 'DOTNET_ROOT=%s\\nPATH=%s\\n' \"${{DOTNET_ROOT:-<unset>}}\" \"$PATH\" > '{envCaptureFile}'\nexit 0\n");
        File.SetUnixFileMode(fakeNode, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int exitCode, string stderr) = RunHook(hookPath, mockUsrBin, sentinelHome);
        exitCode.Should().Be(0, stderr);
        string captured = File.ReadAllText(envCaptureFile);
        captured.Should().Contain("DOTNET_ROOT=<unset>",
            "DOTNET_ROOT must not be pointed at ~/.dotnet when dotnet is already on PATH and ~/.dotnet lacks host/fxr");
        captured.Should().Contain($"{Path.Combine(sentinelHome, ".dotnet", "tools")}",
            "~/.dotnet/tools must remain on PATH");

        // Now remove system dotnet and populate a complete local ~/.dotnet runtime (dotnet + host/fxr).
        File.Delete(fakeDotnet);
        string localDotnetDir = Path.Combine(sentinelHome, ".dotnet");
        Directory.CreateDirectory(Path.Combine(localDotnetDir, "host", "fxr", "10.0.0"));
        string localDotnetExe = Path.Combine(localDotnetDir, "dotnet");
        File.WriteAllText(localDotnetExe, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(localDotnetExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int localExit, string localErr) = RunHook(hookPath, mockUsrBin, sentinelHome);
        localExit.Should().Be(0, localErr);
        string localCaptured = File.ReadAllText(envCaptureFile);
        localCaptured.Should().Contain($"DOTNET_ROOT={localDotnetDir}");
        localCaptured.Should().Contain($"{localDotnetDir}:");
    }

    private void CreateCadenceBinLinterStubs()
    {
        string binDir = Path.Combine(_tempDir, ".cadence", "bin");
        Directory.CreateDirectory(binDir);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                Path.Combine(binDir, "actionlint.exe"),
                "stub");
            File.WriteAllText(
                Path.Combine(binDir, "hadolint.exe"),
                "stub");
            File.WriteAllText(
                Path.Combine(binDir, "shellcheck.exe"),
                "stub");
            return;
        }

        string actionlintPath = Path.Combine(binDir, "actionlint");
        File.WriteAllText(
            actionlintPath,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"-version\" ]; then echo '1.7.12'; exit 0; fi\n" +
            "for f in \"$@\"; do\n" +
            "  if grep -q 'INVALID_WORKFLOW_MARKER' \"$f\" 2>/dev/null; then\n" +
            "    echo \"$f:1:1: invalid workflow marker\"\n" +
            "    exit 1\n" +
            "  fi\n" +
            "done\n" +
            "exit 0\n");
        File.SetUnixFileMode(actionlintPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string hadolintPath = Path.Combine(binDir, "hadolint");
        File.WriteAllText(
            hadolintPath,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then echo 'Haskell Dockerfile Linter 2.15.1'; exit 0; fi\n" +
            "if [ \"${1:-}\" != \"--failure-threshold\" ] || [ \"${2:-}\" != \"warning\" ]; then echo 'expected --failure-threshold warning'; exit 2; fi\n" +
            "shift 2\n" +
            "for f in \"$@\"; do\n" +
            "  if [ -f \"$f\" ] && grep -q 'INVALID_DOCKERFILE_MARKER' \"$f\" 2>/dev/null; then\n" +
            "    echo \"$f:2 DL3000 warning: invalid dockerfile marker\"\n" +
            "    exit 1\n" +
            "  fi\n" +
            "done\n" +
            "exit 0\n");
        File.SetUnixFileMode(hadolintPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string shellcheckPath = Path.Combine(binDir, "shellcheck");
        File.WriteAllText(
            shellcheckPath,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then printf 'ShellCheck - shell script analysis tool\\nversion: 0.11.0\\n'; exit 0; fi\n" +
            "for f in \"$@\"; do\n" +
            "  if [ -f \"$f\" ] && grep -q 'INVALID_SHELL_MARKER' \"$f\" 2>/dev/null; then\n" +
            "    echo \"$f:2:1: warning: invalid shell marker [SC2123]\"\n" +
            "    exit 1\n" +
            "  fi\n" +
            "done\n" +
            "exit 0\n");
        File.SetUnixFileMode(shellcheckPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void SecretsBaselineCleaner_CheckMode_DetectsUnnormalizedBaselineWithoutMutatingDisk()
    {
        const string unnormalized =
            """
            {
              "version": "1.5.0",
              "generated_at": "2026-01-01T00:00:00Z",
              "plugins_used": [{ "name": "PrivateKeyDetector" }],
              "filters_used": [],
              "results": {
                "src/Sample.cs": [{ "type": "Hex High Entropy String", "hashed_secret": "def", "line_number": 42 }]
              }
            }
            """;
        string baselinePath = Write(".secrets.baseline", unnormalized);

        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir, check: true).Should().Be(1);
        File.ReadAllText(baselinePath).Should().Be(unnormalized, "--check mode must never write to .secrets.baseline");

        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir, check: false).Should().Be(0);
        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir, check: true).Should().Be(0);
    }

    [Fact]
    public void FrontendQualityGates_SkipEmptyTargetListWithoutFailingClosed()
    {
        FrontendQualityGateService.CheckPrettier(_tempDir, [], preCommit: true).Should().Be(0);
        FrontendQualityGateService.CheckEslint(_tempDir, [], preCommit: true).Should().Be(0);
    }

    [Fact]
    public void FindRepoRoot_ResolvesRepositoryFromNestedSubdirectory()
    {
        Write("Cadence.sln", "\n");
        string nestedDir = Path.Combine(_tempDir, "apps", "web", "src", "components");
        Directory.CreateDirectory(nestedDir);

        string originalCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(nestedDir);
            string expected = Path.GetFullPath(_tempDir);
            Path.GetFullPath(PreCommitOpenApiService.FindRepoRoot()).Should().Be(expected);
            Path.GetFullPath(QualityGateOrchestrator.FindRepoRoot()).Should().Be(expected);
            Path.GetFullPath(FrontendQualityGateService.FindRepoRoot()).Should().Be(expected);
            Path.GetFullPath(AdrValidatorService.FindRepoRoot()).Should().Be(expected);
            Path.GetFullPath(PublicBundleValidatorService.FindRepoRoot()).Should().Be(expected);
            Path.GetFullPath(DeidComplianceService.FindRepoRoot()).Should().Be(expected);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    [Fact]
    public void PreCommitOpenApiService_ClassifiesBackendContractAndEfCoreSchemaPaths()
    {
        var none = PreCommitOpenApiService.AnalyzeBackendContractChanges(["apps/web/src/App.vue", "docs/cli.md"]);
        none.ShouldExport.Should().BeFalse();
        none.HasCsharpFiles.Should().BeFalse();

        var serviceOnly = PreCommitOpenApiService.AnalyzeBackendContractChanges(["apps/execution/src/Endpoints/FormEndpoints.cs"]);
        serviceOnly.ShouldExport.Should().BeTrue();
        serviceOnly.TargetServices.Should().Equal("execution");
        serviceOnly.HasCsharpFiles.Should().BeTrue();

        var shared = PreCommitOpenApiService.AnalyzeBackendContractChanges(["packages/contracts/src/StudyDto.cs"]);
        shared.ShouldExport.Should().BeTrue();
        shared.TargetServices.Should().BeNull("shared contract changes export all backend services");
        shared.HasCsharpFiles.Should().BeTrue();

        PreCommitOpenApiService.AffectsEfCoreSchema("apps/web/src/models/FormModel.ts").Should().BeFalse();
        PreCommitOpenApiService.AffectsEfCoreSchema("docs/schema/index.md").Should().BeFalse();
        PreCommitOpenApiService.AffectsEfCoreSchema("apps/econsent/src/Domain/ConsentEntity.cs").Should().BeTrue();
        PreCommitOpenApiService.AffectsEfCoreSchema("packages/kernel/src/Cadence.Kernel.Persistence/Migrations/20260101_Init.cs").Should().BeTrue();
    }

    private (int ExitCode, string Stderr) RunHook(string hookPath, string pathEnv, string homeDir)
    {
        var psi = new ProcessStartInfo("/bin/sh")
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(hookPath);
        psi.Environment["PATH"] = pathEnv;
        psi.Environment["HOME"] = homeDir;
        psi.Environment.Remove("DOTNET_ROOT");

        using var process = Process.Start(psi)!;
        _ = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr);
    }

    private string Write(string relativePath, string content)
    {
        string fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private void InitGitRepo()
    {
        RunGit("init", "-q", "-b", "main");
        RunGit("config", "user.email", "test@example.invalid");
        RunGit("config", "user.name", "Test");
    }

    private void RunGit(params string[] args) => _ = CaptureGit(args);

    private string CaptureGit(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, err);
        return output;
    }

    private string CreateDetectSecretsHookStub()
    {
        string binDir = Path.Combine(_tempDir, "stub-bin");
        Directory.CreateDirectory(binDir);

        if (OperatingSystem.IsWindows())
        {
            string cmdPath = Path.Combine(binDir, "detect-secrets-hook.cmd");
            File.WriteAllText(
                cmdPath,
                "@echo off\r\n" +
                "if \"%~1\"==\"--version\" (\r\n" +
                "  echo 1.5.0\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                ":loop\r\n" +
                "if \"%~1\"==\"\" exit /b 3\r\n" +
                "if \"%~1\"==\"--baseline\" (\r\n" +
                "  shift\r\n" +
                "  shift\r\n" +
                "  goto loop\r\n" +
                ")\r\n" +
                "if \"%~1\"==\"--exclude-files\" (\r\n" +
                "  shift\r\n" +
                "  shift\r\n" +
                "  goto loop\r\n" +
                ")\r\n" +
                "findstr /C:\"PRIVATE KEY\" \"%~1\" >nul 2>&1\r\n" +
                "if not errorlevel 1 exit /b 1\r\n" +
                "shift\r\n" +
                "goto loop\r\n");
            return binDir;
        }

        string shPath = Path.Combine(binDir, "detect-secrets-hook");
        File.WriteAllText(
            shPath,
            "#!/bin/sh\n" +
            "set -eu\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then\n" +
            "    echo \"1.5.0\"\n" +
            "    exit 0\n" +
            "fi\n" +
            "if command -v git >/dev/null 2>&1; then\n" +
            "    git diff --name-only >/dev/null\n" +
            "fi\n" +
            "baseline=\"\"\n" +
            "while [ \"$#\" -gt 0 ]; do\n" +
            "    case \"$1\" in\n" +
            "        --baseline)\n" +
            "            baseline=\"$2\"\n" +
            "            shift 2\n" +
            "            ;;\n" +
            "        --exclude-files)\n" +
            "            shift 2\n" +
            "            ;;\n" +
            "        *)\n" +
            "            if grep -q \"PRIVATE KEY\" \"$1\" 2>/dev/null; then\n" +
            "                echo \"Potential secrets about to be committed to git repo! Secret Type: Private Key ($1)\"\n" +
            "                exit 1\n" +
            "            fi\n" +
            "            shift\n" +
            "            ;;\n" +
            "    esac\n" +
            "done\n" +
            "if [ -n \"$baseline\" ] && [ -f \"$baseline\" ]; then\n" +
            "    printf '%s\\n' '{\"mutated_temp_baseline\":true}' > \"$baseline\"\n" +
            "fi\n" +
            "exit 3\n");
        File.SetUnixFileMode(shPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return binDir;
    }

    [Fact]
    public void SetupWizard_LintersOnly_VerifiesSha256ChecksumsAndRejectsTamperedPayloads()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string wizardPath = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), "scripts", "setup_wizard.sh");
        string payloadDir = Path.Combine(_tempDir, "payloads");
        string binOut = Path.Combine(_tempDir, "cadence-bin");
        Directory.CreateDirectory(payloadDir);

        string fakeActionlint = Path.Combine(payloadDir, "actionlint");
        File.WriteAllText(fakeActionlint, "#!/bin/sh\necho '1.7.12'\n");
        File.SetUnixFileMode(fakeActionlint, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string tarPath = Path.Combine(payloadDir, "actionlint.tar.gz");
        var tarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        tarPsi.ArgumentList.Add("-czf");
        tarPsi.ArgumentList.Add(tarPath);
        tarPsi.ArgumentList.Add("actionlint");
        using (var tarProc = Process.Start(tarPsi)!)
        {
            tarProc.WaitForExit();
            tarProc.ExitCode.Should().Be(0);
        }

        string hadolintPayload = Path.Combine(payloadDir, "hadolint-bin");
        File.WriteAllText(hadolintPayload, "#!/bin/sh\necho 'Haskell Dockerfile Linter 2.15.1'\n");
        File.SetUnixFileMode(hadolintPayload, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string shellcheckStageDir = Path.Combine(payloadDir, "shellcheck-v0.11.0");
        Directory.CreateDirectory(shellcheckStageDir);
        string fakeShellcheck = Path.Combine(shellcheckStageDir, "shellcheck");
        File.WriteAllText(fakeShellcheck, "#!/bin/sh\nprintf 'ShellCheck - shell script analysis tool\\nversion: 0.11.0\\n'\n");
        File.SetUnixFileMode(fakeShellcheck, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string scTarXzPath = Path.Combine(payloadDir, "shellcheck.tar.xz");
        var scTarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        scTarPsi.ArgumentList.Add("-cJf");
        scTarPsi.ArgumentList.Add(scTarXzPath);
        scTarPsi.ArgumentList.Add("shellcheck-v0.11.0/shellcheck");
        using (var scTarProc = Process.Start(scTarPsi)!)
        {
            scTarProc.WaitForExit();
            scTarProc.ExitCode.Should().Be(0);
        }

        string alSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tarPath)));
        string hlSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hadolintPayload)));
        string scSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scTarXzPath)));
        string zeroSha = new('0', 64);

        (int badExit, string badOut) = RunWizardLintersOnly(
            wizardPath,
            binOut,
            $"file://{tarPath}",
            zeroSha,
            $"file://{hadolintPayload}",
            zeroSha,
            $"file://{scTarXzPath}",
            zeroSha);
        badExit.Should().Be(1);
        badOut.Should().Contain("Checksum verification failed for actionlint");
        badOut.Should().Contain("Checksum verification failed for hadolint");
        badOut.Should().Contain("Checksum verification failed for shellcheck");
        File.Exists(Path.Combine(binOut, "actionlint")).Should().BeFalse();
        File.Exists(Path.Combine(binOut, "hadolint")).Should().BeFalse();
        File.Exists(Path.Combine(binOut, "shellcheck")).Should().BeFalse();

        (int okExit, string okOut) = RunWizardLintersOnly(
            wizardPath,
            binOut,
            $"file://{tarPath}",
            alSha,
            $"file://{hadolintPayload}",
            hlSha,
            $"file://{scTarXzPath}",
            scSha);
        okExit.Should().Be(0, okOut);
        okOut.Should().Contain("actionlint 1.7.12 SHA-256 verified");
        okOut.Should().Contain("hadolint 2.15.1 SHA-256 verified");
        okOut.Should().Contain("shellcheck 0.11.0 SHA-256 verified");
        File.Exists(Path.Combine(binOut, "actionlint")).Should().BeTrue();
        File.Exists(Path.Combine(binOut, "hadolint")).Should().BeTrue();
        File.Exists(Path.Combine(binOut, "shellcheck")).Should().BeTrue();
    }

    [Fact]
    public void ShellDiscovery_AndFullPathPatternScan_CoverTrackedSubdirectoriesAndShebangHooks()
    {
        InitGitRepo();
        Write("scripts/valid.sh", "#!/usr/bin/env bash\necho ok\n");
        Write(".githooks/pre-commit", "#!/bin/sh\necho hook\n");
        Write("LICENSE", "Proprietary license text\n");
        Write("docs/notes/rogue.sh", "#!/bin/sh\necho misplaced\n");
        Write("tools/Widget.vue", "<template><div /></template>\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "initial");

        var shellStep = new CiStep("lint-and-static", "Run ShellCheck on shell scripts") { Files = CiFileSource.ShellScripts };
        LocalCiEnvironment.DiscoverFiles(_tempDir, shellStep, "origin/main")
            .Should().Equal("./.githooks/pre-commit", "./docs/notes/rogue.sh", "./scripts/valid.sh");

        PathPatternValidatorService.ValidatePathPatterns(files: null, repoRoot: _tempDir)
            .Should().Be(1, "full path-patterns scan must inspect nested tracked directories and flag misplaced .sh and .vue files");

        Directory.CreateDirectory(Path.Combine(_tempDir, "apps", "web", "src"));
        RunGit("mv", "docs/notes/rogue.sh", "scripts/rogue.sh");
        RunGit("mv", "tools/Widget.vue", "apps/web/src/Widget.vue");
        PathPatternValidatorService.ValidatePathPatterns(files: null, repoRoot: _tempDir)
            .Should().Be(0, "relocating .sh into scripts/ and .vue into apps/ must satisfy path-patterns");
    }

    [Fact]
    public void EslintBaselineAndDuplicationDetector_EnforceShrinkOnlyFrontendAndTypeScriptRatchets()
    {
        string keyA = FrontendQualityGateService.EslintWarningBaselineKey("apps/web/src/App.vue", "no-alert", "  window.alert('legacy');");
        string keyB = FrontendQualityGateService.EslintWarningBaselineKey("apps/web/src/App.vue", "vue/max-attributes-per-line", "  <div a=\"1\" b=\"2\" />");
        var findingA = new FrontendQualityGateService.EslintFinding("apps/web/src/App.vue", 10, 3, 1, "no-alert", "Unexpected alert.");
        var findingB = new FrontendQualityGateService.EslintFinding("apps/web/src/App.vue", 20, 3, 1, "vue/max-attributes-per-line", "Too many attributes.");

        var (unbaselined, staleWhenEmpty) = FrontendQualityGateService.CompareEslintWarningsWithBaseline([(findingA, keyA), (findingB, keyB)], [keyA]);
        unbaselined.Should().ContainSingle().Which.Should().Be(findingB);
        staleWhenEmpty.Should().BeEmpty();

        var (noneNew, staleEntries) = FrontendQualityGateService.CompareEslintWarningsWithBaseline([(findingA, keyA)], [keyA, keyB]);
        noneNew.Should().BeEmpty();
        staleEntries.Should().Equal(keyB);

        InitGitRepo();
        string tsClone = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"  const computedRow{i} = normalizeMetric(input, {i}, {i * 3});"));
        Write("apps/web/src/composables/useAlpha.ts", $"export function useAlpha(input: number) {{\n{tsClone}\n  return computedRow1;\n}}\n");
        Write("apps/web/src/composables/useBeta.tsx", $"export function useBeta(input: number) {{\n{tsClone}\n  return computedRow1;\n}}\n");
        // Generated OpenAPI clients and demo-fixtures manifest declarations must be excluded even with identical lines.
        Write("apps/web/src/api/generated/execution.ts", $"export function generatedA(input: number) {{\n{tsClone}\n  return computedRow1;\n}}\n");
        Write("packages/demo-fixtures/src/manifest.d.ts", $"export function generatedB(input: number) {{\n{tsClone}\n  return computedRow1;\n}}\n");
        RunGit("add", ".");

        DuplicationDetectorService.DetectDuplication(repoRoot: _tempDir)
            .Should().Be(1, "unbaselined hand-written .ts/.tsx clones must fail closed");

        string? prevUpdate = Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE");
        try
        {
            Environment.SetEnvironmentVariable("CADENCE_UPDATE_BASELINE", "1");
            DuplicationDetectorService.DetectDuplication(repoRoot: _tempDir).Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CADENCE_UPDATE_BASELINE", prevUpdate);
        }

        string baselineContent = File.ReadAllText(Path.Combine(_tempDir, DuplicationDetectorService.BaselineFile));
        baselineContent.Should().Contain("apps/web/src/composables/useAlpha.ts|apps/web/src/composables/useBeta.tsx|")
            .And.NotContain("api/generated")
            .And.NotContain("manifest.d.ts");

        DuplicationDetectorService.DetectDuplication(repoRoot: _tempDir)
            .Should().Be(0, "baselined clones must pass when unchanged");

        // Removing the clone without updating the baseline must fail as a stale entry so the baseline shrinks.
        Write("apps/web/src/composables/useBeta.tsx", "export const useBeta = () => 0;\n");
        DuplicationDetectorService.DetectDuplication(repoRoot: _tempDir)
            .Should().Be(1, "stale entries in code-duplication.baseline.txt must fail until shrunk");
    }

    [Fact]
    public void FrontendEslintGate_BuildsZeroMaxWarningsArguments_ForStdinBatchAndWorkspaceModes()
    {
        var stdinArgs = FrontendQualityGateService.BuildEslintArgs(["--stdin", "--stdin-filename", "apps/web/src/App.vue"]);
        stdinArgs.Should().Equal(
            "exec",
            "eslint",
            "--no-warn-ignored",
            "--max-warnings",
            "0",
            "--stdin",
            "--stdin-filename",
            "apps/web/src/App.vue");

        var batchArgs = FrontendQualityGateService.BuildEslintArgs(["--", "apps/web/src/App.vue"]);
        batchArgs.Should().Equal(
            "exec",
            "eslint",
            "--no-warn-ignored",
            "--max-warnings",
            "0",
            "--",
            "apps/web/src/App.vue");

        var workspaceArgs = FrontendQualityGateService.BuildEslintArgs(["."]);
        workspaceArgs.Should().Equal(
            "exec",
            "eslint",
            "--no-warn-ignored",
            "--max-warnings",
            "0",
            ".");
    }

    [Fact]
    public void SetupWizard_LintersOnly_FailsClosedWhenXzIsMissing()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string wizardPath = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), "scripts", "setup_wizard.sh");
        string payloadDir = Path.Combine(_tempDir, "no-xz-payloads");
        Directory.CreateDirectory(payloadDir);

        string fakeActionlint = Path.Combine(payloadDir, "actionlint");
        File.WriteAllText(fakeActionlint, "#!/bin/sh\necho '1.7.12'\n");
        File.SetUnixFileMode(fakeActionlint, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string tarPath = Path.Combine(payloadDir, "actionlint.tar.gz");
        var tarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        tarPsi.ArgumentList.Add("-czf");
        tarPsi.ArgumentList.Add(tarPath);
        tarPsi.ArgumentList.Add("actionlint");
        using (var tarProc = Process.Start(tarPsi)!)
        {
            tarProc.WaitForExit();
            tarProc.ExitCode.Should().Be(0);
        }

        string hadolintPayload = Path.Combine(payloadDir, "hadolint-bin");
        File.WriteAllText(hadolintPayload, "#!/bin/sh\necho 'Haskell Dockerfile Linter 2.15.1'\n");
        File.SetUnixFileMode(hadolintPayload, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string alSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tarPath)));
        string hlSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hadolintPayload)));

        string minimalPathDir = Path.Combine(_tempDir, "no-xz-bin");
        Directory.CreateDirectory(minimalPathDir);
        foreach (string tool in new[] { "uname", "dirname", "mkdir", "mktemp", "rm", "mv", "cp", "chmod", "curl", "tar", "gzip", "sha256sum", "shasum", "awk", "tr", "head" })
        {
            string? resolved = ResolveFromSystemPath(tool);
            if (resolved is not null)
            {
                File.CreateSymbolicLink(Path.Combine(minimalPathDir, tool), resolved);
            }
        }

        (int exitCode, string combined) = RunWizardWithCustomPath(
            wizardPath,
            minimalPathDir,
            Path.Combine(_tempDir, "no-xz-cadence-bin"),
            $"file://{tarPath}",
            alSha,
            $"file://{hadolintPayload}",
            hlSha);

        exitCode.Should().Be(1);
        combined.Should().Contain("xz is required to extract ShellCheck (.tar.xz)");
    }

    [Fact]
    public void DetectSecretsGateService_RunsMultipleBatchesInParallel_WithIsolatedTempBaselines_WithoutMutatingCommittedBaseline()
    {
        InitGitRepo();
        const string baselineJson =
            """
            {
              "version": "1.5.0",
              "plugins_used": [{ "name": "PrivateKeyDetector" }],
              "filters_used": [],
              "results": {}
            }
            """;
        string baselinePath = Write(".secrets.baseline", baselineJson);
        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir, check: false).Should().Be(0);
        string committedBaseline = File.ReadAllText(baselinePath);

        string[] files = new string[215];
        for (int i = 0; i < files.Length; i++)
        {
            string rel = $"src/batch_item_{i:D3}.txt";
            Write(rel, $"safe content {i}\n");
            files[i] = rel;
        }

        string stubBinDir = CreateDetectSecretsHookStub();
        string systemPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var stubEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = string.IsNullOrEmpty(systemPath) ? stubBinDir : $"{stubBinDir}{Path.PathSeparator}{systemPath}",
            [DetectSecretsGateService.DisableUvxFallbackEnvVar] = "1",
        };

        DetectSecretsGateService.Scan(_tempDir, files, preCommit: false, key => stubEnv.GetValueOrDefault(key))
            .Should().Be(0, "215 clean files across parallel 200-file batches must exit 0 even when detect-secrets-hook mutates its temp baseline and exits 3");
        File.ReadAllText(baselinePath).Should().Be(committedBaseline, "committed .secrets.baseline must never be mutated");

        Write("src/batch_item_205.txt", $"{PrivateKeyHeader}\nMIIB\n");
        DetectSecretsGateService.Scan(_tempDir, files, preCommit: false, key => stubEnv.GetValueOrDefault(key))
            .Should().Be(1, "an unbaselined secret in any parallel batch must fail closed");
        File.ReadAllText(baselinePath).Should().Be(committedBaseline);
    }

    private static (int ExitCode, string CombinedOutput) RunWizardWithCustomPath(
        string wizardPath,
        string pathEnv,
        string cadenceBinDir,
        string actionlintUrl,
        string actionlintSha,
        string hadolintUrl,
        string hadolintSha,
        string shellcheckUrl = "",
        string shellcheckSha = "")
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wizardPath);
        psi.ArgumentList.Add("--linters-only");
        psi.Environment["PATH"] = pathEnv;
        psi.Environment["CADENCE_BIN_DIR"] = cadenceBinDir;
        psi.Environment["ACTIONLINT_DOWNLOAD_URL"] = actionlintUrl;
        psi.Environment["ACTIONLINT_SHA256"] = actionlintSha;
        psi.Environment["HADOLINT_DOWNLOAD_URL"] = hadolintUrl;
        psi.Environment["HADOLINT_SHA256"] = hadolintSha;
        psi.Environment["SHELLCHECK_DOWNLOAD_URL"] = shellcheckUrl;
        psi.Environment["SHELLCHECK_SHA256"] = shellcheckSha;

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout + stderr);
    }

    private static string? ResolveFromSystemPath(string binaryName)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(entry, binaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (int ExitCode, string CombinedOutput) RunWizardLintersOnly(
        string wizardPath,
        string cadenceBinDir,
        string actionlintUrl,
        string actionlintSha,
        string hadolintUrl,
        string hadolintSha,
        string shellcheckUrl,
        string shellcheckSha)
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wizardPath);
        psi.ArgumentList.Add("--linters-only");
        psi.Environment["CADENCE_BIN_DIR"] = cadenceBinDir;
        psi.Environment["ACTIONLINT_DOWNLOAD_URL"] = actionlintUrl;
        psi.Environment["ACTIONLINT_SHA256"] = actionlintSha;
        psi.Environment["HADOLINT_DOWNLOAD_URL"] = hadolintUrl;
        psi.Environment["HADOLINT_SHA256"] = hadolintSha;
        psi.Environment["SHELLCHECK_DOWNLOAD_URL"] = shellcheckUrl;
        psi.Environment["SHELLCHECK_SHA256"] = shellcheckSha;

        using var proc = Process.Start(psi)!;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return (proc.ExitCode, stdout + stderr);
    }
}

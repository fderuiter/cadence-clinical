using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public void SecretsBaselineCleaner_SplitsIntoConfigAndPerUnitShards_AndDeletesLegacyMonolithicBaseline()
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
                "apps/web/src/Sample.ts": [{ "type": "Hex High Entropy String", "hashed_secret": "def", "line_number": 42 }],
                "src/RootSample.cs": [{ "type": "Hex High Entropy String", "hashed_secret": "ghi", "line_number": 7 }]
              },
              "exclude": {
                "files": "legacy",
                "lines": null
              }
            }
            """);

        SecretsBaselineCleanerService.CleanSecretsBaseline(baselinePath, _tempDir).Should().Be(0);
        File.Exists(baselinePath).Should().BeFalse("legacy .secrets.baseline must be deleted after splitting into shards");

        string configPath = Path.Combine(_tempDir, SecretsBaselineCleanerService.ConfigFile);
        var parsedConfig = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        parsedConfig.ContainsKey("generated_at").Should().BeFalse();
        parsedConfig.ContainsKey("exclude").Should().BeFalse("detect-secrets v1.5.0 ignores the legacy top-level exclude key");
        parsedConfig.ContainsKey("results").Should().BeFalse("config.json stores shared metadata only");

        var filter = parsedConfig["filters_used"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["path"]?.GetValue<string>() == "detect_secrets.filters.regex.should_exclude_file");
        filter["pattern"]!.AsArray().Select(p => p!.GetValue<string>())
            .Should().Equal(SecretsBaselineCleanerService.CanonicalExcludePattern);

        var webShard = JsonNode.Parse(File.ReadAllText(Path.Combine(_tempDir, "baselines/secrets/apps/web.secrets.baseline.json")))!.AsObject();
        webShard["results"]!.AsObject()["apps/web/src/Sample.ts"]!.AsArray()[0]!.AsObject().ContainsKey("line_number").Should().BeFalse();

        var rootShard = JsonNode.Parse(File.ReadAllText(Path.Combine(_tempDir, "baselines/secrets/root.secrets.baseline.json")))!.AsObject();
        rootShard["results"]!.AsObject().ContainsKey("pnpm-lock.yaml").Should().BeFalse();
        rootShard["results"]!.AsObject().ContainsKey("src/RootSample.cs").Should().BeTrue();

        SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: _tempDir, check: true).Should().Be(0);
    }

    [Fact]
    public void DetectSecretsGate_FailsClosedWhenToolIsUnavailable()
    {
        Write(SecretsBaselineCleanerService.ConfigFile, "{ \"version\": \"1.5.0\", \"plugins_used\": [], \"filters_used\": [] }\n");
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
    public void DetectSecretsGate_BlocksUnbaselinedSecret_AndLeavesShardsUnchanged()
    {
        InitGitRepo();
        string configContent =
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [\n    {\n      \"name\": \"PrivateKeyDetector\"\n    }\n  ],\n  \"filters_used\": []\n}\n";
        string baselinePath = Write(SecretsBaselineCleanerService.ConfigFile, configContent);
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
        File.ReadAllText(baselinePath).Should().Be(configContent);

        DetectSecretsGateService.Scan(_tempDir, ["secret.txt"], preCommit: true, key => stubEnv.GetValueOrDefault(key)).Should().Be(1);
        File.ReadAllText(baselinePath).Should().Be(configContent);

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
            File.ReadAllText(baselinePath).Should().Be(configContent);

            DetectSecretsGateService.Scan(_tempDir, ["secret.txt"], preCommit: true, key => realEnv.GetValueOrDefault(key)).Should().Be(1);
            File.ReadAllText(baselinePath).Should().Be(configContent);
        }
    }

    [Fact]
    public void CiPlan_DetectSecretsStep_DelegatesToCadenceCli_AndBaselineConfigRetainsCanonicalExcludeFilter()
    {
        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var step = securityJob.Steps.Single(s => s.Name == "Run detect-secrets against the baseline");

        step.RequiredTool.Should().Be("detect-secrets-hook");
        var cmd = step.Commands.Should().ContainSingle().Subject;
        cmd.FileName.Should().Be("node");
        cmd.Arguments.Should().Equal("scripts/run-cli.js", "check", "detect-secrets");
        cmd.ForwardsBaseRef.Should().BeTrue();

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        File.Exists(Path.Combine(repoRoot, ".secrets.baseline")).Should().BeFalse();
        string repoConfigPath = Path.Combine(repoRoot, SecretsBaselineCleanerService.ConfigFile);
        var repoBaseline = JsonNode.Parse(File.ReadAllText(repoConfigPath))!.AsObject();
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
            FindSkippedDatabaseTests = (_, _) => [],
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
        Write(SecretsBaselineCleanerService.ConfigFile,
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": []\n}\n");
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
        FrontendQualityGateService.CheckTypecheck(_tempDir, ["apps/web/src/sample.ts"], preCommit: true).Should().Be(1);
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
            .Should().BeFalse("retired idempotent SQL dumps are no longer excluded from secret scanning");
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
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": []\n}\n";
        string dupBody = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"        int item{i} = Calculate({i}, {i + 3});"));
        string boolLiteralExpr = "== " + "true";

        Write(SecretsBaselineCleanerService.ConfigFile, baselineJson);
        Write("baselines/deid/root.baseline.txt", "# header\nREADME.md|dates|0123456789abcdef\n");
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
    public void MsBuildPreCommitGates_IgnoreUnrelatedUntrackedCsharpFiles_AndRejectOverlappingProjectClosureEdits()
    {
        InitGitRepo();

        const string rootBuildProps =
            "<Project>\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n";
        const string coreEntityV1 = "namespace Core;\n\npublic abstract class BaseEntity\n{\n    public int Id { get; set; }\n}\n";
        const string openApiSpecJson =
            "{\n  \"openapi\": \"3.0.1\",\n  \"info\": { \"title\": \"Execution API\", \"version\": \"v1\" },\n  \"paths\": {\n    \"/api/v1/forms\": {\n      \"get\": {\n        \"summary\": \"Lists forms\",\n        \"responses\": { \"200\": { \"description\": \"OK\" } }\n      }\n    }\n  }\n}\n";
        const string schemaDocMd = "# `execution` database schema\n\n- `forms`\n";

        Write(".gitignore", "**/bin/\n**/obj/\n");
        Write("Directory.Build.props", rootBuildProps);
        Write(".editorconfig", "root = true\n\n[*.cs]\ndotnet_style_qualification_for_field = false:warning\n");

        Write("packages/core/src/Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("packages/core/src/BaseEntity.cs", coreEntityV1);

        Write("packages/contracts/src/Contracts.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"..\\..\\core\\src\\Core.csproj\" />\n  </ItemGroup>\n</Project>\n");
        Write("packages/contracts/src/FormDto.cs", "namespace Contracts;\n\npublic sealed record FormDto(int Id);\n");

        Write("apps/execution/src/Cadence.Apps.Execution.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"..\\..\\..\\packages\\contracts\\src\\Contracts.csproj\" />\n  </ItemGroup>\n</Project>\n");
        Write("apps/execution/src/Endpoints/FormEndpoints.cs",
            "namespace Execution.Endpoints;\n\npublic static class FormEndpoints\n{\n    public static int Version => 1;\n}\n");
        Write("apps/execution/src/Entities/FormEntity.cs",
            "using Core;\n\nnamespace Execution.Entities;\n\npublic sealed class FormEntity : BaseEntity\n{\n    public string Title { get; set; } = string.Empty;\n}\n");

        Write("apps/safety/src/Cadence.Apps.Safety.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("apps/safety/src/SafetyEndpoints.cs", "namespace Safety;\n\npublic static class SafetyEndpoints\n{\n    public static int Version => 1;\n}\n");

        Write("apps/unrelated/src/Unrelated.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("apps/unrelated/src/Worker.cs", "namespace Unrelated;\n\npublic sealed class Worker;\n");

        Write("packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"..\\..\\..\\..\\apps\\execution\\src\\Cadence.Apps.Execution.csproj\" />\n    <ProjectReference Include=\"..\\..\\..\\..\\apps\\safety\\src\\Cadence.Apps.Safety.csproj\" />\n  </ItemGroup>\n</Project>\n");
        Write("packages/tooling_core/src/Cadence.OpenApiExporter/Program.cs",
            "string outDir = args[Array.IndexOf(args, \"--output-dir\") + 1];\n" +
            "Directory.CreateDirectory(outDir);\n" +
            $"File.WriteAllText(Path.Combine(outDir, \"execution_openapi.json\"), {System.Text.Json.JsonSerializer.Serialize(openApiSpecJson)});\n");

        Write("packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"..\\..\\..\\..\\apps\\execution\\src\\Cadence.Apps.Execution.csproj\" />\n  </ItemGroup>\n</Project>\n");
        Write("packages/tooling_core/src/Cadence.SchemaDocExporter/Program.cs",
            "string outDir = args[Array.IndexOf(args, \"--output-dir\") + 1];\n" +
            "Directory.CreateDirectory(outDir);\n" +
            $"File.WriteAllText(Path.Combine(outDir, \"execution.md\"), {System.Text.Json.JsonSerializer.Serialize(schemaDocMd)});\n");

        Write("docs/openapi/execution_openapi.json", openApiSpecJson);
        ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot: _tempDir).Should().Be(0);
        Write("docs/schema/execution.md", schemaDocMd);

        RunDotnetInTemp("restore", "packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj");
        RunDotnetInTemp("restore", "packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj");
        RunDotnetInTemp("restore", "apps/unrelated/src/Unrelated.csproj");

        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "initial multi-project fixture");

        // Stage clean edits in execution endpoint + EF entity.
        Write("apps/execution/src/Endpoints/FormEndpoints.cs",
            "namespace Execution.Endpoints;\n\npublic static class FormEndpoints\n{\n    public static int Version => 2;\n}\n");
        Write("apps/execution/src/Entities/FormEntity.cs",
            "using Core;\n\nnamespace Execution.Entities;\n\npublic sealed class FormEntity : BaseEntity\n{\n    public string Title { get; set; } = \"Updated\";\n}\n");
        RunGit("add", "apps/execution/src/Endpoints/FormEndpoints.cs", "apps/execution/src/Entities/FormEntity.cs");

        string[] stagedFiles = [.. PreCommitOpenApiService.GetStagedFiles(_tempDir)];
        using var reader = GitIndexReader.TryOpen(_tempDir);
        reader.Should().NotBeNull();
        var ctx = new GateContext(_tempDir, stagedFiles, PreCommit: true, IndexReader: reader);

        var styleGate = QualityGates.Find("dotnet-format-style")!;
        var openApiGate = QualityGates.Find("openapi")!;
        var schemaDocGate = QualityGates.Find("schema-doc")!;

        // 1. Untracked .cs files in unrelated projects (including broken syntax outside closure and WIP in another service) must be ignored.
        Write("apps/unrelated/src/BrokenScratch.cs", "namespace Unrelated { public class { broken syntax\n");
        Write("apps/safety/src/WipSafetyDraft.cs", "namespace Safety;\n\npublic sealed class WipSafetyDraft;\n");

        styleGate.Run(ctx).Should().BeTrue("dotnet-format-style must ignore untracked .cs files outside the staged project closure");
        openApiGate.Run(ctx).Should().BeTrue("openapi must ignore untracked .cs files outside the affected service's transitive closure");
        schemaDocGate.Run(ctx).Should().BeTrue("schema-doc must ignore untracked .cs files outside SchemaDocExporter's transitive closure");

        // 2. Unstaged edit in a transitive ProjectReference (apps/execution -> Contracts -> Core) must fail closed across all three gates.
        Write("packages/core/src/BaseEntity.cs", coreEntityV1 + "// unstaged transitive edit\n");
        styleGate.Run(ctx).Should().BeFalse("dotnet-format-style must reject unstaged edits in transitive ProjectReferences");
        openApiGate.Run(ctx).Should().BeFalse("openapi must reject unstaged edits in transitive ProjectReferences");
        schemaDocGate.Run(ctx).Should().BeFalse("schema-doc must reject unstaged edits in transitive ProjectReferences");
        Write("packages/core/src/BaseEntity.cs", coreEntityV1);

        // 3. Untracked .cs file inside the target project itself must fail closed across all three gates.
        string directUntracked = Write("apps/execution/src/UntrackedDraft.cs", "namespace Execution;\n\npublic sealed class UntrackedDraft;\n");
        styleGate.Run(ctx).Should().BeFalse("dotnet-format-style must reject untracked .cs files inside the target project");
        openApiGate.Run(ctx).Should().BeFalse("openapi must reject untracked .cs files inside the target project");
        schemaDocGate.Run(ctx).Should().BeFalse("schema-doc must reject untracked .cs files inside the target project");
        File.Delete(directUntracked);

        // 4. Unstaged edit to a root MSBuild configuration file (Directory.Build.props) must fail closed across all three gates.
        Write("Directory.Build.props", rootBuildProps + "<!-- unstaged root msbuild edit -->\n");
        styleGate.Run(ctx).Should().BeFalse("dotnet-format-style must reject unstaged edits to root MSBuild files");
        openApiGate.Run(ctx).Should().BeFalse("openapi must reject unstaged edits to root MSBuild files");
        schemaDocGate.Run(ctx).Should().BeFalse("schema-doc must reject unstaged edits to root MSBuild files");
    }

    private void RunDotnetInTemp(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
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
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, stdout + stderr);
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
        RunGit("add", ".");

        // Mask both files on disk with clean content so only staged index inspection catches them.
        Write(".github/workflows/ci.yml", "name: CI\non: push\n");
        Write("docker/Dockerfile.api", "FROM ubuntu:24.04\n");

        var emptyPathEnv = new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = _tempDir };
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, getEnvironmentVariable: k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "actionlint gate must fail closed when binary is missing");
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, getEnvironmentVariable: k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "hadolint gate must fail closed when binary is missing");
        CliLinterGateService.ResolveShellcheck(_tempDir, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().BeNull("shellcheck resolution must return null when binary is missing");

        CreateCadenceBinLinterStubs();
        CliLinterGateService.ResolveShellcheck(_tempDir, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().NotBeNull("shellcheck resolution must prefer .cadence/bin");
        using var reader = GitIndexReader.TryOpen(_tempDir);
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "actionlint must validate staged workflow blob even when working tree is clean");
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, reader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(1, "hadolint must validate staged Dockerfile blob even when working tree is clean");

        // Now stage the clean versions and verify both pass.
        RunGit("add", ".");
        using var cleanReader = GitIndexReader.TryOpen(_tempDir);
        CliLinterGateService.RunActionlint(_tempDir, [".github/workflows/ci.yml"], preCommit: true, cleanReader, k => emptyPathEnv.GetValueOrDefault(k))
            .Should().Be(0);
        CliLinterGateService.RunHadolint(_tempDir, ["docker/Dockerfile.api"], preCommit: true, cleanReader, k => emptyPathEnv.GetValueOrDefault(k))
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

        // Now remove system dotnet and verify runtime-only ~/.dotnet (dotnet + host/fxr without sdk/10.*) is NOT promoted.
        File.Delete(fakeDotnet);
        string localDotnetDir = Path.Combine(sentinelHome, ".dotnet");
        Directory.CreateDirectory(Path.Combine(localDotnetDir, "host", "fxr", "10.0.0"));
        string localDotnetExe = Path.Combine(localDotnetDir, "dotnet");
        File.WriteAllText(localDotnetExe, "#!/bin/sh\nif [ \"${1:-}\" = \"--version\" ]; then echo '10.0.100'; fi\nexit 0\n");
        File.SetUnixFileMode(localDotnetExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int runtimeOnlyExit, _) = RunHook(hookPath, mockUsrBin, sentinelHome);
        runtimeOnlyExit.Should().Be(1,
            "runtime-only ~/.dotnet lacking sdk/10.* must not be promoted onto PATH");

        // Populate sdk/10.0.100 so ~/.dotnet satisfies dotnet + host/fxr + sdk/10.*.
        Directory.CreateDirectory(Path.Combine(localDotnetDir, "sdk", "10.0.100"));

        (int localExit, string localErr) = RunHook(hookPath, mockUsrBin, sentinelHome);
        localExit.Should().Be(0, localErr);
        string localCaptured = File.ReadAllText(envCaptureFile);
        localCaptured.Should().Contain($"DOTNET_ROOT={localDotnetDir}");
        localCaptured.Should().Contain($"{localDotnetDir}:");
    }

    [Fact]
    public void PreCommitHookAndRunCli_WithDotnet8FirstOnPath_PromoteVerifiedDotnet10AndExitZero()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string hookPath = Path.Combine(repoRoot, ".githooks", "pre-commit");
        string realNodeDir = Path.GetDirectoryName(PosixProcessLauncher.ResolveExecutablePath("node")!)!;

        // Build an isolated copy of scripts/run-cli.js + global.json so we never touch repoRoot/bin/cadence.
        string cliRepo = Path.Combine(_tempDir, "cli-repo");
        Directory.CreateDirectory(Path.Combine(cliRepo, "scripts"));
        Directory.CreateDirectory(Path.Combine(cliRepo, "packages", "cli", "src"));
        File.Copy(Path.Combine(repoRoot, "scripts", "run-cli.js"), Path.Combine(cliRepo, "scripts", "run-cli.js"));
        File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(cliRepo, "global.json"));
        File.WriteAllText(Path.Combine(cliRepo, "packages", "cli", "src", "Cadence.Cli.csproj"), "<Project />\n");

        // System dotnet 8 first on PATH: exits 155 when global.json requires .NET 10.
        string dotnet8Dir = Path.Combine(_tempDir, "usr-dotnet8");
        Directory.CreateDirectory(Path.Combine(dotnet8Dir, "host", "fxr", "8.0.10"));
        Directory.CreateDirectory(Path.Combine(dotnet8Dir, "sdk", "8.0.404"));
        string dotnet8Exe = Path.Combine(dotnet8Dir, "dotnet");
        File.WriteAllText(
            dotnet8Exe,
            "#!/bin/sh\n" +
            "if [ -f \"global.json\" ]; then\n" +
            "  echo 'The command could not be loaded (global.json requires 10.0.100).' >&2\n" +
            "  exit 155\n" +
            "fi\n" +
            "echo '8.0.404'\n" +
            "exit 0\n");
        File.SetUnixFileMode(dotnet8Exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Verified .NET 10 SDK in $HOME/.dotnet.
        string homeDir = Path.Combine(_tempDir, "dev-home");
        string homeDotnet = Path.Combine(homeDir, ".dotnet");
        string homeTools = Path.Combine(homeDotnet, "tools");
        Directory.CreateDirectory(Path.Combine(homeDotnet, "host", "fxr", "10.0.0"));
        Directory.CreateDirectory(Path.Combine(homeDotnet, "sdk", "10.0.100"));
        Directory.CreateDirectory(homeTools);
        string homeDotnetExe = Path.Combine(homeDotnet, "dotnet");
        string cliEnvCapture = Path.Combine(_tempDir, "cli-env.txt");
        File.WriteAllText(
            homeDotnetExe,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then\n" +
            "  echo '10.0.100'\n" +
            "  exit 0\n" +
            "fi\n" +
            "if [ \"${1:-}\" = \"publish\" ]; then\n" +
            "  out=''\n" +
            "  while [ \"$#\" -gt 0 ]; do\n" +
            "    if [ \"$1\" = \"-o\" ]; then out=\"$2\"; shift 2; else shift; fi\n" +
            "  done\n" +
            "  mkdir -p \"$out\"\n" +
            $"  cat << 'EOS' > \"$out/cadence\"\n#!/bin/sh\nprintf 'DOTNET_ROOT=%s\\nPATH=%s\\nARGS=%s\\n' \"${{DOTNET_ROOT:-<unset>}}\" \"$PATH\" \"$*\" > '{cliEnvCapture}'\necho '1.0.0-test'\nexit 0\nEOS\n" +
            "  chmod +x \"$out/cadence\"\n" +
            "  exit 0\n" +
            "fi\n" +
            "exit 0\n");
        File.SetUnixFileMode(homeDotnetExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // 1. Verify .githooks/pre-commit with .NET 8 first on PATH (and stale DOTNET_ROOT pointing at .NET 8).
        string hookEnvCapture = Path.Combine(_tempDir, "hook-env.txt");
        string mockNodeDir = Path.Combine(_tempDir, "mock-node");
        Directory.CreateDirectory(mockNodeDir);
        string fakeNode = Path.Combine(mockNodeDir, "node");
        File.WriteAllText(
            fakeNode,
            $"#!/bin/sh\nprintf 'DOTNET_ROOT=%s\\nPATH=%s\\nACTIVE_VER=%s\\n' \"${{DOTNET_ROOT:-<unset>}}\" \"$PATH\" \"$(dotnet --version)\" > '{hookEnvCapture}'\nexit 0\n");
        File.SetUnixFileMode(fakeNode, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int hookExit, string hookStderr) = RunHook(
            hookPath,
            $"{dotnet8Dir}:{mockNodeDir}:{homeDotnet}",
            homeDir,
            extraEnv: new Dictionary<string, string?> { ["DOTNET_ROOT"] = dotnet8Dir });
        hookExit.Should().Be(0, hookStderr);
        string hookCaptured = File.ReadAllText(hookEnvCapture);
        hookCaptured.Should().Contain($"DOTNET_ROOT={homeDotnet}");
        hookCaptured.Should().Contain("ACTIVE_VER=10.0.100");
        hookCaptured.Should().Contain($"PATH={homeDotnet}:");

        // 2. Verify `node scripts/run-cli.js --version` with .NET 8 first on PATH exits 0 instead of 155.
        (int cliExit, string cliOut, string cliErr) = RunNodeCli(
            Path.Combine(cliRepo, "scripts", "run-cli.js"),
            ["--version"],
            $"{dotnet8Dir}:{realNodeDir}:/bin:/usr/bin",
            homeDir,
            extraEnv: new Dictionary<string, string?> { ["DOTNET_ROOT"] = dotnet8Dir });
        cliExit.Should().Be(0, cliOut + cliErr);
        cliOut.Should().Contain("1.0.0-test");
        string cliCaptured = File.ReadAllText(cliEnvCapture);
        cliCaptured.Should().Contain($"DOTNET_ROOT={homeDotnet}");
        cliCaptured.Should().Contain($"PATH={homeDotnet}:");
        cliCaptured.Should().Contain(homeTools);

        // 3. Verify `node scripts/run-cli.js --version` with .NET 10 on PATH and sentinel-only $HOME/.dotnet leaves DOTNET_ROOT unset.
        string sentinelHome = Path.Combine(_tempDir, "cli-sentinel-home");
        string sentinelTools = Path.Combine(sentinelHome, ".dotnet", "tools");
        Directory.CreateDirectory(sentinelTools);
        File.WriteAllText(Path.Combine(sentinelHome, ".dotnet", "10.0.100.dotnetFirstUseSentinel"), "");

        (int sentinelCliExit, string sentinelCliOut, string sentinelCliErr) = RunNodeCli(
            Path.Combine(cliRepo, "scripts", "run-cli.js"),
            ["--version"],
            $"{homeDotnet}:{realNodeDir}:/bin:/usr/bin",
            sentinelHome);
        sentinelCliExit.Should().Be(0, sentinelCliOut + sentinelCliErr);
        string sentinelCaptured = File.ReadAllText(cliEnvCapture);
        sentinelCaptured.Should().Contain("DOTNET_ROOT=<unset>");
        sentinelCaptured.Should().Contain(sentinelTools);

        // 4. Verify %LOCALAPPDATA%/Microsoft/dotnet fallback when $HOME/.dotnet has only sentinels.
        string localAppData = Path.Combine(_tempDir, "localappdata");
        string winStyleDotnet = Path.Combine(localAppData, "Microsoft", "dotnet");
        Directory.CreateDirectory(Path.Combine(winStyleDotnet, "host", "fxr", "10.0.0"));
        Directory.CreateDirectory(Path.Combine(winStyleDotnet, "sdk", "10.0.100"));
        File.Copy(homeDotnetExe, Path.Combine(winStyleDotnet, "dotnet"));
        File.SetUnixFileMode(Path.Combine(winStyleDotnet, "dotnet"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        (int appDataExit, string appDataOut, string appDataErr) = RunNodeCli(
            Path.Combine(cliRepo, "scripts", "run-cli.js"),
            ["--version"],
            $"{dotnet8Dir}:{realNodeDir}:/bin:/usr/bin",
            sentinelHome,
            extraEnv: new Dictionary<string, string?> { ["LOCALAPPDATA"] = localAppData });
        appDataExit.Should().Be(0, appDataOut + appDataErr);
        string appDataCaptured = File.ReadAllText(cliEnvCapture);
        appDataCaptured.Should().Contain($"DOTNET_ROOT={winStyleDotnet}");
        appDataCaptured.Should().Contain($"PATH={winStyleDotnet}:");
    }

    [Fact]
    public void SetupWizard_NonAptHost_SourcesResolveDotnetEnvSoRestoreAndDoctorRunAfterHomeDotnetInstall()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string wizardRepo = Path.Combine(_tempDir, "wizard-repo");
        Directory.CreateDirectory(Path.Combine(wizardRepo, "scripts"));
        Directory.CreateDirectory(Path.Combine(wizardRepo, ".githooks"));

        foreach (string scriptName in new[] { "setup_wizard.sh", "setup_dotnet_env.sh", "resolve_dotnet_env.sh" })
        {
            string dst = Path.Combine(wizardRepo, "scripts", scriptName);
            File.Copy(Path.Combine(repoRoot, "scripts", scriptName), dst);
            File.SetUnixFileMode(dst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string hookDst = Path.Combine(wizardRepo, ".githooks", "pre-commit");
        File.Copy(Path.Combine(repoRoot, ".githooks", "pre-commit"), hookDst);
        File.SetUnixFileMode(hookDst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(wizardRepo, "global.json"));
        File.WriteAllText(Path.Combine(wizardRepo, ".env.example"), "ASPNETCORE_ENVIRONMENT=Development\n");
        File.WriteAllText(Path.Combine(wizardRepo, "Cadence.sln"), "\n");

        string invocationLog = Path.Combine(_tempDir, "wizard-invocations.log");
        File.WriteAllText(
            Path.Combine(wizardRepo, "scripts", "run-cli.js"),
            $"#!/bin/sh\nprintf 'run-cli:%s|DOTNET_ROOT=%s|DOTNET=%s\\n' \"$*\" \"${{DOTNET_ROOT:-<unset>}}\" \"$(command -v dotnet || echo missing)\" >> '{invocationLog}'\nexit 0\n");

        // Initialize git repository in wizardRepo so `git config core.hooksPath .githooks` succeeds.
        var gitInit = new ProcessStartInfo("git")
        {
            WorkingDirectory = wizardRepo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        gitInit.ArgumentList.Add("init");
        gitInit.ArgumentList.Add("-q");
        using (var gp = Process.Start(gitInit)!)
        {
            gp.WaitForExit();
            gp.ExitCode.Should().Be(0);
        }

        // Prepare local SHA-256-verified payloads for dotnet-install.sh, actionlint, and hadolint.
        string payloadDir = Path.Combine(_tempDir, "wizard-payloads");
        Directory.CreateDirectory(payloadDir);

        string fakeInstaller = Path.Combine(payloadDir, "dotnet-install.sh");
        File.WriteAllText(
            fakeInstaller,
            "#!/usr/bin/env bash\n" +
            "set -euo pipefail\n" +
            "install_dir=\"$HOME/.dotnet\"\n" +
            "while [ \"$#\" -gt 0 ]; do\n" +
            "  case \"$1\" in\n" +
            "    --install-dir) install_dir=\"$2\"; shift 2 ;;\n" +
            "    *) shift ;;\n" +
            "  esac\n" +
            "done\n" +
            "mkdir -p \"$install_dir/host/fxr/10.0.0\" \"$install_dir/sdk/10.0.100\"\n" +
            $"cat << 'EOS' > \"$install_dir/dotnet\"\n#!/bin/sh\nif [ \"${{1:-}}\" = \"--version\" ]; then echo '10.0.100'; exit 0; fi\nprintf 'dotnet:%s|DOTNET_ROOT=%s\\n' \"$*\" \"${{DOTNET_ROOT:-<unset>}}\" >> '{invocationLog}'\nexit 0\nEOS\n" +
            "chmod +x \"$install_dir/dotnet\"\n");
        File.SetUnixFileMode(fakeInstaller, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string installerSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fakeInstaller)));

        string fakeActionlint = Path.Combine(payloadDir, "actionlint");
        File.WriteAllText(fakeActionlint, "#!/bin/sh\necho '1.7.12'\n");
        File.SetUnixFileMode(fakeActionlint, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string actionlintTar = Path.Combine(payloadDir, "actionlint.tar.gz");
        var tarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        tarPsi.ArgumentList.Add("-czf");
        tarPsi.ArgumentList.Add(actionlintTar);
        tarPsi.ArgumentList.Add("actionlint");
        using (var tp = Process.Start(tarPsi)!)
        {
            tp.WaitForExit();
            tp.ExitCode.Should().Be(0);
        }

        string hadolintBin = Path.Combine(payloadDir, "hadolint-bin");
        File.WriteAllText(hadolintBin, "#!/bin/sh\necho 'Haskell Dockerfile Linter 2.15.1'\n");
        File.SetUnixFileMode(hadolintBin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string scExtractDir = Path.Combine(payloadDir, "shellcheck-v0.11.0");
        Directory.CreateDirectory(scExtractDir);
        string fakeShellcheck = Path.Combine(scExtractDir, "shellcheck");
        File.WriteAllText(fakeShellcheck, "#!/bin/sh\necho 'version: 0.11.0'\n");
        File.SetUnixFileMode(fakeShellcheck, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string shellcheckTar = Path.Combine(payloadDir, "shellcheck.tar.xz");
        var scTarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        scTarPsi.ArgumentList.Add("-cJf");
        scTarPsi.ArgumentList.Add(shellcheckTar);
        scTarPsi.ArgumentList.Add("shellcheck-v0.11.0/shellcheck");
        using (var sp = Process.Start(scTarPsi)!)
        {
            sp.WaitForExit();
            sp.ExitCode.Should().Be(0);
        }

        string alSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actionlintTar)));
        string hlSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hadolintBin)));
        string scSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(shellcheckTar)));

        // Construct a non-apt POSIX tool bin (failing apt-get/sudo, mock curl/node/pnpm/uv, plus standard POSIX utilities except dotnet).
        string mockBin = Path.Combine(_tempDir, "wizard-bin");
        Directory.CreateDirectory(mockBin);

        foreach (string util in new[] { "bash", "sh", "git", "tar", "gzip", "xz", "sha256sum", "shasum", "awk", "tr", "mktemp", "chmod", "cp", "mv", "rm", "mkdir", "cat", "id", "uname", "dirname", "pwd", "grep", "sed", "head", "tail", "cut" })
        {
            string? resolved = PosixProcessLauncher.ResolveExecutablePath(util);
            if (resolved is not null)
            {
                File.CreateSymbolicLink(Path.Combine(mockBin, util), resolved);
            }
        }

        WriteExecutable(Path.Combine(mockBin, "apt-get"), "#!/bin/sh\nexit 1\n");
        WriteExecutable(Path.Combine(mockBin, "sudo"), "#!/bin/sh\nexit 1\n");
        WriteExecutable(
            Path.Combine(mockBin, "curl"),
            "#!/bin/sh\n" +
            "out=''\nurl=''\n" +
            "while [ \"$#\" -gt 0 ]; do\n" +
            "  case \"$1\" in\n" +
            "    -o) out=\"$2\"; shift 2 ;;\n" +
            "    --connect-timeout|--retry) shift 2 ;;\n" +
            "    -*) shift ;;\n" +
            "    *) url=\"$1\"; shift ;;\n" +
            "  esac\n" +
            "done\n" +
            "case \"$url\" in\n" +
            $"  *dotnet-install.sh) cp '{fakeInstaller}' \"$out\" ;;\n" +
            "  file://*) cp \"${url#file://}\" \"$out\" ;;\n" +
            "  *) exit 1 ;;\n" +
            "esac\n");
        WriteExecutable(
            Path.Combine(mockBin, "node"),
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then echo 'v22.0.0'; exit 0; fi\n" +
            "script=\"$1\"; shift\n" +
            "exec /bin/sh \"$script\" \"$@\"\n");
        WriteExecutable(
            Path.Combine(mockBin, "pnpm"),
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then echo '10.32.1'; exit 0; fi\n" +
            "exit 0\n");

        // Pre-populate .cadence/venv stubs so setup_wizard.sh does not hit PyPI over the network.
        string venvBin = Path.Combine(wizardRepo, ".cadence", "venv", "bin");
        Directory.CreateDirectory(venvBin);
        WriteExecutable(Path.Combine(venvBin, "detect-secrets-hook"), "#!/bin/sh\necho '1.5.0'\n");

        string wizardHome = Path.Combine(_tempDir, "wizard-home");
        Directory.CreateDirectory(wizardHome);

        var wizardPsi = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = wizardRepo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        wizardPsi.ArgumentList.Add(Path.Combine(wizardRepo, "scripts", "setup_wizard.sh"));
        wizardPsi.ArgumentList.Add("--minimal");
        wizardPsi.Environment["HOME"] = wizardHome;
        wizardPsi.Environment["PATH"] = mockBin;
        wizardPsi.Environment.Remove("DOTNET_ROOT");
        wizardPsi.Environment["DOTNET_INSTALL_SH_SHA256"] = installerSha;
        wizardPsi.Environment["ACTIONLINT_DOWNLOAD_URL"] = $"file://{actionlintTar}";
        wizardPsi.Environment["ACTIONLINT_SHA256"] = alSha;
        wizardPsi.Environment["HADOLINT_DOWNLOAD_URL"] = $"file://{hadolintBin}";
        wizardPsi.Environment["HADOLINT_SHA256"] = hlSha;
        wizardPsi.Environment["SHELLCHECK_DOWNLOAD_URL"] = $"file://{shellcheckTar}";
        wizardPsi.Environment["SHELLCHECK_SHA256"] = scSha;

        (int wizardExit, string combined) = RunProcessCapture(wizardPsi);
        wizardExit.Should().Be(0, combined);
        combined.Should().NotContain("Skipped 'cadence doctor'",
            "setup_wizard.sh must keep $HOME/.dotnet on PATH after setup_dotnet_env.sh finishes");

        string logged = File.ReadAllText(invocationLog);
        string expectedDotnetRoot = Path.Combine(wizardHome, ".dotnet");
        logged.Should().Contain($"dotnet:restore {Path.Combine(wizardRepo, "Cadence.sln")}|DOTNET_ROOT={expectedDotnetRoot}");
        logged.Should().Contain($"run-cli:doctor|DOTNET_ROOT={expectedDotnetRoot}|DOTNET={Path.Combine(expectedDotnetRoot, "dotnet")}");
    }

    [Fact]
    public void PosixProcessLauncher_ResolvesWindowsCmdAndExeInPathOrder_AndHonorsCustomPathAndEnoexecFallback()
    {
        string dirA = Path.Combine(_tempDir, "win-a");
        string dirB = Path.Combine(_tempDir, "win-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        // dirA has both an extensionless POSIX corepack shim (`pnpm`) and a Windows `pnpm.cmd` wrapper.
        File.WriteAllText(Path.Combine(dirA, "pnpm"), "#!/bin/sh\necho posix\n");
        string pnpmCmdA = Path.Combine(dirA, "pnpm.cmd");
        File.WriteAllText(pnpmCmdA, "@echo off\r\n");

        // dirB has a standalone `pnpm.exe` binary.
        string pnpmExeB = Path.Combine(dirB, "pnpm.exe");
        File.WriteAllText(pnpmExeB, "MZ");

        var envCmdFirst = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = $"{dirA};{dirB}",
        };
        PosixProcessLauncher.ResolveExecutablePath("pnpm", _tempDir, envCmdFirst, isWindows: true)
            .Should().Be(pnpmCmdA, "dirA appears before dirB on PATH and extensionless POSIX shim must be skipped on Windows");

        var envExeFirst = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Path"] = $"{dirB};{dirA}",
        };
        PosixProcessLauncher.ResolveExecutablePath("pnpm", _tempDir, envExeFirst, isWindows: true)
            .Should().Be(pnpmExeB, "dirB appears before dirA on case-insensitive Path override");

        // Within the same directory on Windows, .exe takes precedence over .cmd.
        string pnpmExeA = Path.Combine(dirA, "pnpm.exe");
        File.WriteAllText(pnpmExeA, "MZ");
        PosixProcessLauncher.ResolveExecutablePath("pnpm", _tempDir, envCmdFirst, isWindows: true)
            .Should().Be(pnpmExeA);

        // UseShellExecute = true must always be rejected.
        var shellPsi = new ProcessStartInfo("pnpm") { UseShellExecute = true };
        Action actShell = () => PosixProcessLauncher.Start(shellPsi);
        actShell.Should().Throw<ArgumentException>();

        // Empty custom PATH override must NOT leak parent process PATH.
        var isolatedMissingPsi = new ProcessStartInfo("pnpm")
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        isolatedMissingPsi.Environment["PATH"] = Path.Combine(_tempDir, "empty-dir");
        Action actMissing = () => PosixProcessLauncher.Start(isolatedMissingPsi);
        actMissing.Should().Throw<Win32Exception>();

        if (!OperatingSystem.IsWindows() && File.Exists("/bin/sh"))
        {
            // Shebang-less POSIX pnpm script resolved via custom PATH must fall back to /bin/sh on ENOEXEC and preserve ArgumentList.
            string customPosixBin = Path.Combine(_tempDir, "posix-custom-bin");
            Directory.CreateDirectory(customPosixBin);
            string shebanglessPnpm = Path.Combine(customPosixBin, "pnpm");
            File.WriteAllText(shebanglessPnpm, "printf '%s\\n' \"$@\"\n");
            File.SetUnixFileMode(shebanglessPnpm, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var enoexecPsi = new ProcessStartInfo("pnpm")
            {
                WorkingDirectory = _tempDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            enoexecPsi.ArgumentList.Add("exec");
            enoexecPsi.ArgumentList.Add("arg with spaces & ; | $HOME");
            enoexecPsi.Environment["PATH"] = $"{customPosixBin}:/bin:/usr/bin";

            using var proc = PosixProcessLauncher.Start(enoexecPsi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            proc.ExitCode.Should().Be(0, stderr);
            stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("exec\narg with spaces & ; | $HOME\n");
        }
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
            "for f in \"$@\"; do\n" +
            "  if [ -f \"$f\" ] && grep -q 'INVALID_DOCKERFILE_MARKER' \"$f\" 2>/dev/null; then\n" +
            "    echo \"$f:2 DL3000 error: invalid dockerfile marker\"\n" +
            "    exit 1\n" +
            "  fi\n" +
            "done\n" +
            "exit 0\n");
        File.SetUnixFileMode(hadolintPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string shellcheckPath = Path.Combine(binDir, "shellcheck");
        File.WriteAllText(
            shellcheckPath,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then echo 'ShellCheck - shell script analysis tool'; echo 'version: 0.11.0'; exit 0; fi\n" +
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
        FrontendQualityGateService.CheckTypecheck(_tempDir, [], preCommit: true).Should().Be(0);
    }

    [Fact]
    public void CiPlan_FrontendStyleAndLintStep_DelegatesEslintToCadenceCli()
    {
        var lintJob = CiPlan.Jobs.Single(j => j.Key == "lint-and-static");
        var step = lintJob.Steps.Single(s => s.Name == "Run frontend style and lint");

        step.EnvironmentIfPassed.Should().Contain(
            new CiEnvironmentIfPassed(
                "native-aot-build/Publish the CLI as Native AOT",
                "CADENCE_CLI_CACHE_HIT",
                "true"));
        step.Commands.Should().HaveCount(3);
        var eslintCmd = step.Commands[1];
        eslintCmd.FileName.Should().Be("node");
        eslintCmd.Arguments.Should().Equal("scripts/run-cli.js", "check", "eslint");
        eslintCmd.ForwardsBaseRef.Should().BeFalse("ci.yml runs check eslint unscoped across all tracked files");

        var typecheckCmd = step.Commands[2];
        typecheckCmd.FileName.Should().Be("pnpm");
        typecheckCmd.Arguments.Should().Equal("-r", "typecheck");
    }

    [Fact]
    public void CheckEslint_UnscopedRun_LintsAllTrackedJsTsVueAndMjsFilesViaRootConfig_AndRunsDuplicateIdentifierCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        InitGitRepo();
        Directory.CreateDirectory(Path.Combine(_tempDir, "node_modules"));
        Write("eslint.config.mjs", "export default [];\n");
        Write(".claude/skills/run-cadence/driver.mjs", "export const ok = true;\n");
        Write("packages/usdm-schemas/src/index.ts", "export const version = 4;\n");
        Write("apps/web/src/stores/demo/clinicalEcrf.ts", "export const store = 1;\n");
        string dupLog = Path.Combine(_tempDir, "dup-check.log");
        Write("scripts/check_duplicate_identifiers.js", $"require('node:fs').writeFileSync({System.Text.Json.JsonSerializer.Serialize(dupLog)}, 'checked');\n");
        Write("docs/README.md", "# Ignored by EslintExtensions\n");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "seed tracked files");

        // Untracked scratch JS file should not be swept when git ls-files succeeds.
        Write("untracked-scratch.js", "BROKEN_ESLINT_MARKER\n");

        string binDir = Path.Combine(_tempDir, "pnpm-stub-bin");
        Directory.CreateDirectory(binDir);
        string argsCapture = Path.Combine(_tempDir, "pnpm-eslint-args.txt");
        string pnpmStub = Path.Combine(binDir, "pnpm");
        File.WriteAllText(
            pnpmStub,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then echo '10.34.6'; exit 0; fi\n" +
            $"printf '%s\\n' \"$*\" >> '{argsCapture}'\n" +
            "for arg in \"$@\"; do\n" +
            "  if [ -f \"$arg\" ] && grep -q 'BROKEN_ESLINT_MARKER' \"$arg\" 2>/dev/null; then exit 1; fi\n" +
            "done\n" +
            "exit 0\n");
        File.SetUnixFileMode(pnpmStub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string systemPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var stubEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = string.IsNullOrEmpty(systemPath) ? binDir : $"{binDir}{Path.PathSeparator}{systemPath}",
        };

        FrontendQualityGateService.CheckEslint(_tempDir, getEnvironmentVariable: k => stubEnv.GetValueOrDefault(k))
            .Should().Be(0);

        File.Exists(dupLog).Should().BeTrue("unscoped CheckEslint must run check_duplicate_identifiers.js when clinicalEcrf.ts is tracked");
        string captured = File.ReadAllText(argsCapture);
        captured.Should().StartWith("exec eslint --no-warn-ignored --max-warnings 0 -- ");
        captured.Should().NotContain("-r lint");
        captured.Should().Contain(".claude/skills/run-cadence/driver.mjs");
        captured.Should().Contain("eslint.config.mjs");
        captured.Should().Contain("packages/usdm-schemas/src/index.ts");
        captured.Should().Contain("apps/web/src/stores/demo/clinicalEcrf.ts");
        captured.Should().NotContain("untracked-scratch.js");
        captured.Should().NotContain("docs/README.md");

        Write(".claude/skills/run-cadence/driver.mjs", "BROKEN_ESLINT_MARKER\n");
        FrontendQualityGateService.CheckEslint(_tempDir, getEnvironmentVariable: k => stubEnv.GetValueOrDefault(k))
            .Should().Be(1, "unscoped CheckEslint must fail when a tracked root/skill .mjs file has an ESLint error");
    }

    [Fact]
    public void FrontendEslintGate_ValidatesDuplicateIdentifiersInStagedClinicalEcrfSlice()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string realRepoRoot = QualityGateOrchestrator.FindRepoRoot();
        string realScript = Path.Combine(realRepoRoot, "scripts", "check_duplicate_identifiers.js");
        Write("scripts/check_duplicate_identifiers.js", File.ReadAllText(realScript));
        string realNodeModules = Path.Combine(realRepoRoot, "node_modules");
        string symlinkNodeModules = Path.Combine(_tempDir, "node_modules");
        bool createdSymlink = Directory.Exists(realNodeModules);
        if (createdSymlink)
        {
            Directory.CreateSymbolicLink(symlinkNodeModules, realNodeModules);
        }
        else
        {
            Directory.CreateDirectory(symlinkNodeModules);
        }

        Directory.CreateDirectory(Path.Combine(_tempDir, "apps", "web"));

        string stubBin = Path.Combine(_tempDir, "pnpm-stub-bin");
        Directory.CreateDirectory(stubBin);
        string pnpmStub = Path.Combine(stubBin, "pnpm");
        File.WriteAllText(
            pnpmStub,
            "#!/bin/sh\ncase \" $* \" in\n  *\" --stdin \"*) cat >/dev/null || true ;;\nesac\nexit 0\n");
        File.SetUnixFileMode(pnpmStub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        InitGitRepo();
        const string slicePath = "apps/web/src/stores/demo/clinicalEcrf.ts";
        const string cleanSlice =
            """
            export function createClinicalEcrfState() {
              return {
                ecrfFields: [
                  { id: "vssbp", label: "Systolic BP", type: "number" },
                  { id: "vsdbp", label: "Diastolic BP", type: "number" },
                ],
              };
            }
            """;
        const string duplicateSlice =
            """
            export function createClinicalEcrfState() {
              return {
                ecrfFields: [
                  { id: "vssbp", label: "Systolic BP", type: "number" },
                  { id: "vssbp", label: "Duplicate Systolic BP", type: "number" },
                ],
              };
            }
            """;

        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{stubBin}{Path.PathSeparator}/tmp/bin{Path.PathSeparator}{originalPath}");

            Write(slicePath, cleanSlice);
            RunGit("add", slicePath);
            FrontendQualityGateService.CheckEslint(_tempDir, targetFiles: null, preCommit: true).Should().Be(0);

            Write(slicePath, duplicateSlice);
            RunGit("add", slicePath);
            // Even when the working tree looks clean, the staged git index duplicate must fail closed.
            Write(slicePath, cleanSlice);
            FrontendQualityGateService.CheckEslint(_tempDir, targetFiles: null, preCommit: true).Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(symlinkNodeModules))
            {
                Directory.Delete(symlinkNodeModules, recursive: false);
            }
        }
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
        string realRepoRoot = PreCommitOpenApiService.FindRepoRoot();

        var none = PreCommitOpenApiService.AnalyzeBackendContractChanges(["apps/web/src/App.vue", "docs/cli.md"], realRepoRoot);
        none.ShouldExport.Should().BeFalse();
        none.HasCsharpFiles.Should().BeFalse();

        var serviceOnly = PreCommitOpenApiService.AnalyzeBackendContractChanges(["apps/execution/src/Endpoints/FormEndpoints.cs"], realRepoRoot);
        serviceOnly.ShouldExport.Should().BeTrue();
        serviceOnly.TargetServices.Should().Equal("execution");
        serviceOnly.HasCsharpFiles.Should().BeTrue();

        var kernelDomain = PreCommitOpenApiService.AnalyzeBackendContractChanges(
            ["packages/kernel/src/Cadence.Kernel.Domain/Entities/Study.cs"],
            realRepoRoot);
        kernelDomain.ShouldExport.Should().BeTrue();
        kernelDomain.TargetServices.Should().Equal("kernel");
        kernelDomain.HasCsharpFiles.Should().BeTrue();

        var executionModule = PreCommitOpenApiService.AnalyzeBackendContractChanges(
            ["apps/execution/module/ExecutionModule.cs"],
            realRepoRoot);
        executionModule.ShouldExport.Should().BeTrue();
        executionModule.TargetServices.Should().Equal("execution_forms");
        executionModule.HasCsharpFiles.Should().BeTrue();

        var sharedContracts = PreCommitOpenApiService.AnalyzeBackendContractChanges(["packages/contracts/src/StudyDto.cs"], realRepoRoot);
        sharedContracts.ShouldExport.Should().BeTrue();
        sharedContracts.TargetServices.Should().Equal("ctms", "econsent", "etmf", "execution", "notifications", "payments");
        sharedContracts.HasCsharpFiles.Should().BeTrue();

        var nonExporterProjects = PreCommitOpenApiService.AnalyzeBackendContractChanges(
            ["packages/testing/src/FakeClock.cs", "apps/gateway/src/Program.cs"],
            realRepoRoot);
        nonExporterProjects.ShouldExport.Should().BeFalse("neither packages/testing nor apps/gateway is referenced by an OpenAPI-exporting host");
        nonExporterProjects.TargetServices.Should().BeNull();
        nonExporterProjects.HasCsharpFiles.Should().BeTrue();

        foreach (string globalBuildFile in new[]
        {
            "Cadence.sln",
            "global.json",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
        })
        {
            var globalChange = PreCommitOpenApiService.AnalyzeBackendContractChanges([globalBuildFile], realRepoRoot);
            globalChange.ShouldExport.Should().BeTrue($"{globalBuildFile} can alter all backend OpenAPI contracts");
            globalChange.TargetServices.Should().BeNull($"{globalBuildFile} triggers an unscoped all-service export");
            globalChange.HasCsharpFiles.Should().BeTrue();
        }

        PreCommitOpenApiService.AffectsEfCoreSchema("apps/web/src/models/FormModel.ts").Should().BeFalse();
        PreCommitOpenApiService.AffectsEfCoreSchema("docs/schema/index.md").Should().BeFalse();
        PreCommitOpenApiService.AffectsEfCoreSchema("apps/econsent/src/Domain/ConsentEntity.cs").Should().BeTrue();
        PreCommitOpenApiService.AffectsEfCoreSchema("packages/kernel/src/Cadence.Kernel.Persistence/Migrations/20260101_Init.cs").Should().BeTrue();
    }

    [Fact]
    public void ApiClientValidatorService_ScopesPreCommitRunsToOpenApiFrontendAndCanvasContractInputs()
    {
        ApiClientValidatorService.AffectsApiClients(_tempDir, "README.md").Should().BeFalse();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/kernel/src/Cadence.Kernel.Domain/Study.cs").Should().BeFalse();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/testing/src/FakeClock.cs").Should().BeFalse();

        ApiClientValidatorService.AffectsApiClients(_tempDir, "docs/openapi/ctms_openapi.json").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "apps/web/src/App.vue").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "apps/subject-portal/src/main.ts").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/ui/src/index.ts").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/usdm-schemas/src/index.ts").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "scripts/generate-api-types.js").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "scripts/generate-canvas-contracts.js").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "packages/security/src/Rbac/PermissionMatrix.cs").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "pnpm-lock.yaml").Should().BeTrue();
        ApiClientValidatorService.AffectsApiClients(_tempDir, "package.json").Should().BeTrue();

        // Fixture _tempDir has no apps/web/tsconfig.json: irrelevant pre-commit paths skip cleanly (0),
        // whereas relevant paths or unscoped runs fail closed (1).
        ApiClientValidatorService.ValidateApiClients(_tempDir, targetFiles: ["README.md"], preCommit: true).Should().Be(0);
        ApiClientValidatorService.ValidateApiClients(_tempDir, targetFiles: ["docs/openapi/ctms_openapi.json"], preCommit: true).Should().Be(1);
        ApiClientValidatorService.ValidateApiClients(_tempDir).Should().Be(1);
    }

    [Fact]
    public void ReadmeOnlyPreCommit_SkipsPackageAuditsTypecheckAndOpenApiExporterOffline()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("README.md", "# Cadence Clinical\n");
        Write("docs/SDLC/vulnerability_exclusions_ledger.json", "[]\n");
        Directory.CreateDirectory(Path.Combine(_tempDir, "docs", "openapi"));
        ApiDocsGeneratorService.GenerateApiDocs(repoRoot: _tempDir).Should().Be(0);

        string sentinelBin = Path.Combine(_tempDir, "sentinel-bin");
        string invocationsLog = Path.Combine(_tempDir, "forbidden-tools.log");
        Directory.CreateDirectory(sentinelBin);

        foreach (string tool in new[] { "dotnet", "pnpm", "pip-audit", "tsc" })
        {
            string scriptPath = Path.Combine(sentinelBin, tool);
            File.WriteAllText(scriptPath, $"#!/bin/sh\necho {tool} >> \"{invocationsLog}\"\nexit 99\n");
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{sentinelBin}:/usr/bin:/bin");

            var context = new GateContext(_tempDir, ["README.md"], PreCommit: true);
            QualityGates.Find("typecheck")!.Run(context).Should().BeTrue();
            QualityGates.Find("vulnerabilities")!.Run(context).Should().BeTrue();
            QualityGates.Find("api-clients")!.Run(context).Should().BeTrue();
            QualityGates.Find("openapi")!.Run(context).Should().BeTrue();

            File.Exists(invocationsLog).Should().BeFalse(
                "staging only README.md must complete typecheck, vulnerabilities, api-clients, and openapi offline without invoking dotnet, pnpm, pip-audit, or tsc");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private (int ExitCode, string Stderr) RunHook(
        string hookPath,
        string pathEnv,
        string homeDir,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
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
        psi.Environment.Remove("LOCALAPPDATA");
        psi.Environment.Remove("USERPROFILE");
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi)!;
        _ = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr);
    }

    private (int ExitCode, string Stdout, string Stderr) RunNodeCli(
        string runCliScriptPath,
        string[] cliArgs,
        string pathEnv,
        string homeDir,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        string nodeExe = PosixProcessLauncher.ResolveExecutablePath("node")!;
        var psi = new ProcessStartInfo(nodeExe)
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(runCliScriptPath);
        foreach (string arg in cliArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["PATH"] = pathEnv;
        psi.Environment["HOME"] = homeDir;
        psi.Environment.Remove("DOTNET_ROOT");
        psi.Environment.Remove("LOCALAPPDATA");
        psi.Environment.Remove("USERPROFILE");
        psi.Environment.Remove("GITHUB_ACTIONS");
        psi.Environment.Remove("CADENCE_CLI_CACHE_HIT");
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi)!;
        var stderrTask = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, stdout, stderr);
    }

    private static (int ExitCode, string CombinedOutput) RunProcessCapture(ProcessStartInfo psi)
    {
        using var proc = Process.Start(psi)!;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return (proc.ExitCode, stdout + stderr);
    }

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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

        string scExtractDir = Path.Combine(payloadDir, "shellcheck-v0.11.0");
        Directory.CreateDirectory(scExtractDir);
        string fakeShellcheck = Path.Combine(scExtractDir, "shellcheck");
        File.WriteAllText(fakeShellcheck, "#!/bin/sh\necho 'version: 0.11.0'\n");
        File.SetUnixFileMode(fakeShellcheck, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string scTarPath = Path.Combine(payloadDir, "shellcheck.tar.xz");
        var scTarPsi = new ProcessStartInfo("tar")
        {
            WorkingDirectory = payloadDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        scTarPsi.ArgumentList.Add("-cJf");
        scTarPsi.ArgumentList.Add(scTarPath);
        scTarPsi.ArgumentList.Add("shellcheck-v0.11.0/shellcheck");
        using (var scTarProc = Process.Start(scTarPsi)!)
        {
            scTarProc.WaitForExit();
            scTarProc.ExitCode.Should().Be(0);
        }

        string alSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tarPath)));
        string hlSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hadolintPayload)));
        string scSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scTarPath)));
        string zeroSha = new('0', 64);

        (int badExit, string badOut) = RunWizardLintersOnly(
            wizardPath,
            binOut,
            $"file://{tarPath}",
            zeroSha,
            $"file://{hadolintPayload}",
            zeroSha,
            $"file://{scTarPath}",
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
            $"file://{scTarPath}",
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
    public void FrontendEslintGate_IncludesCssExtension_AndRoutesStagedCssThroughPreCommitStdinLinting()
    {
        FrontendQualityGateService.SupportedEslintExtensions.Should().Contain(".css");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        InitGitRepo();
        Directory.CreateDirectory(Path.Combine(_tempDir, "node_modules"));
        string cleanCss = Write("apps/web/src/style.css", ".app-shell { color: var(--text-main); }\n");
        _ = cleanCss;
        RunGit("add", "apps/web/src/style.css");

        string logFile = Path.Combine(_tempDir, "pnpm-eslint-invocations.log");
        string stubBinDir = Path.Combine(_tempDir, "eslint-pnpm-bin");
        Directory.CreateDirectory(stubBinDir);
        string pnpmStub = Path.Combine(stubBinDir, "pnpm");
        WriteExecutable(
            pnpmStub,
            "#!/bin/sh\n" +
            "set -eu\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then\n" +
            "    echo \"10.28.2\"\n" +
            "    exit 0\n" +
            "fi\n" +
            $"printf '%s\\n' \"$*\" >> \"{logFile}\"\n" +
            "stdin_body=$(cat)\n" +
            "case \"$stdin_body\" in\n" +
            "    *\"#ff0000\"*)\n" +
            "        echo \"error Hardcoded color literal '#ff0000' is prohibited\" >&2\n" +
            "        exit 1\n" +
            "        ;;\n" +
            "esac\n" +
            "exit 0\n");

        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                string.IsNullOrEmpty(originalPath) ? stubBinDir : $"{stubBinDir}{Path.PathSeparator}{originalPath}");

            FrontendQualityGateService.CheckEslint(_tempDir, targetFiles: null, preCommit: true)
                .Should().Be(0, "staged token-clean .css files must pass ESLint stdin pre-commit validation");

            File.ReadAllText(logFile).Should().Contain(
                "exec eslint --no-warn-ignored --max-warnings 0 --stdin --stdin-filename apps/web/src/style.css");

            Write("apps/web/src/style.css", ".app-shell { color: #ff0000; }\n");
            RunGit("add", "apps/web/src/style.css");

            FrontendQualityGateService.CheckEslint(_tempDir, targetFiles: null, preCommit: true)
                .Should().Be(1, "staged .css files with hardcoded color literals must fail closed in pre-commit mode");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
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
        string configPath = Path.Combine(_tempDir, SecretsBaselineCleanerService.ConfigFile);
        string committedConfig = File.ReadAllText(configPath);

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
        File.ReadAllText(configPath).Should().Be(committedConfig, "committed baselines/secrets/config.json must never be mutated");

        Write("src/batch_item_205.txt", $"{PrivateKeyHeader}\nMIIB\n");
        DetectSecretsGateService.Scan(_tempDir, files, preCommit: false, key => stubEnv.GetValueOrDefault(key))
            .Should().Be(1, "an unbaselined secret in any parallel batch must fail closed");
        File.ReadAllText(configPath).Should().Be(committedConfig);
    }

    [Fact]
    public void ConcurrentShardPaydowns_AcrossAssembliesRuleAreasDeidUnitsAndSecretsUnits_MergeCleanlyViaGitMergeFileAndMergeTree()
    {
        InitGitRepo();

        // Seed initial per-owner shards across all four baseline gates with zero .gitattributes merge drivers.
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/Cadence.Apps.Execution.baseline.txt",
            "# boundary\nK-MOD-1|Cadence.Apps.Execution|ExecEntity.StudyId\nK-XS-1|Cadence.Apps.Execution|ExecDto.StudyId\n");
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/Cadence.Apps.Ctms.baseline.txt",
            "# boundary\nK-MOD-1|Cadence.Apps.Ctms|CtmsEntity.SiteId\n");
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/MOD.baseline.txt",
            "# coverage\nK-MOD-2\nK-MOD-3\n");
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/SIM.baseline.txt",
            "# coverage\nK-SIM-1\n");
        Write("baselines/deid/apps/web.baseline.txt",
            "# deid\napps/web/src/View.vue|dates|0011223344556677\napps/web/src/Store.ts|dates|8899aabbccddeeff\n");
        Write("baselines/deid/packages/ui.baseline.txt",
            "# deid\npackages/ui/src/Badge.vue|dates|fedcba9876543210\n");
        Write(SecretsBaselineCleanerService.ConfigFile,
            "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [],\n  \"filters_used\": []\n}\n");
        Write("baselines/secrets/apps/execution.secrets.baseline.json",
            "{\n  \"results\": {\n    \"apps/execution/src/Auth.cs\": [\n      {\n        \"hashed_secret\": \"aaa\",\n        \"type\": \"Secret Keyword\"\n      },\n      {\n        \"hashed_secret\": \"bbb\",\n        \"type\": \"Secret Keyword\"\n      }\n    ]\n  }\n}\n");
        Write("baselines/secrets/packages/auth.secrets.baseline.json",
            "{\n  \"results\": {\n    \"packages/auth/src/Jwt.cs\": [\n      {\n        \"hashed_secret\": \"ccc\",\n        \"type\": \"Secret Keyword\"\n      }\n    ]\n  }\n}\n");

        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "seed sharded baselines");

        // Verify git merge-file directly on disjoint shard updates vs base (Branch A updates its shards while Branch B leaves them at base, and vice versa).
        string mergeSimDir = Path.Combine(_tempDir, "merge-file-sim");
        Directory.CreateDirectory(mergeSimDir);
        string baseFile = Path.Combine(mergeSimDir, "base.txt");
        string oursFile = Path.Combine(mergeSimDir, "ours.txt");
        string theirsFile = Path.Combine(mergeSimDir, "theirs.txt");
        File.WriteAllText(baseFile, "# boundary\nK-MOD-1|Cadence.Apps.Execution|ExecEntity.StudyId\nK-XS-1|Cadence.Apps.Execution|ExecDto.StudyId\n");
        File.WriteAllText(oursFile, "# boundary\nK-XS-1|Cadence.Apps.Execution|ExecDto.StudyId\n");
        File.WriteAllText(theirsFile, File.ReadAllText(baseFile));
        RunGit("merge-file", "-p", oursFile, baseFile, theirsFile);
        Directory.Delete(mergeSimDir, recursive: true);

        // Branch A pays down Execution boundary, MOD rule, apps/web DEID, and apps/execution secret.
        RunGit("checkout", "-q", "-b", "branch-a");
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/Cadence.Apps.Execution.baseline.txt",
            "# boundary\nK-XS-1|Cadence.Apps.Execution|ExecDto.StudyId\n");
        Write("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/MOD.baseline.txt",
            "# coverage\nK-MOD-3\n");
        Write("baselines/deid/apps/web.baseline.txt",
            "# deid\napps/web/src/Store.ts|dates|8899aabbccddeeff\n");
        Write("baselines/secrets/apps/execution.secrets.baseline.json",
            "{\n  \"results\": {\n    \"apps/execution/src/Auth.cs\": [\n      {\n        \"hashed_secret\": \"bbb\",\n        \"type\": \"Secret Keyword\"\n      }\n    ]\n  }\n}\n");
        RunGit("add", "-A");
        RunGit("commit", "-q", "-m", "branch A paydown");

        // Branch B concurrently deletes empty Ctms boundary shard, SIM rule shard, packages/ui DEID shard, and packages/auth secret shard!
        RunGit("checkout", "-q", "main");
        RunGit("checkout", "-q", "-b", "branch-b");
        File.Delete(Path.Combine(_tempDir, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/Cadence.Apps.Ctms.baseline.txt"));
        File.Delete(Path.Combine(_tempDir, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/SIM.baseline.txt"));
        File.Delete(Path.Combine(_tempDir, "baselines/deid/packages/ui.baseline.txt"));
        File.Delete(Path.Combine(_tempDir, "baselines/secrets/packages/auth.secrets.baseline.json"));
        RunGit("add", "-A");
        RunGit("commit", "-q", "-m", "branch B paydown");

        // Merge Branch A and Branch B with standard Git 3-way merge (zero custom merge drivers): must succeed with exit code 0!
        var mergeResult = GitRevisionScopeService.PreviewMergeTree(_tempDir, "branch-a");
        mergeResult.Succeeded.Should().BeTrue();
        mergeResult.HasConflicts.Should().BeFalse();
        mergeResult.ConflictingFiles.Should().BeEmpty();
    }

    private static (int ExitCode, string CombinedOutput) RunWizardWithCustomPath(
        string wizardPath,
        string pathEnv,
        string cadenceBinDir,
        string actionlintUrl,
        string actionlintSha,
        string hadolintUrl,
        string hadolintSha)
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

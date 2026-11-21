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
    public void CiPlan_DetectSecretsStep_UsesCanonicalExcludeRegex_WithoutLiteralSingleQuotes()
    {
        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var step = securityJob.Steps.Single(s => s.Name == "Run detect-secrets against the baseline");

        var cmd = step.Commands.Should().ContainSingle().Subject;
        cmd.FileName.Should().Be("detect-secrets-hook");
        cmd.Arguments.Should().Equal("--baseline", ".secrets.baseline", "--exclude-files", SecretsBaselineCleanerService.CanonicalExcludePattern);
        cmd.Arguments.Should().NotContain(a => a.StartsWith('\'') || a.EndsWith('\''),
            "ProcessStartInfo.ArgumentList passes arguments literally without a shell");

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
    public void LocalCiRunner_DetectSecretsStep_AcceptsBaselineLineNumberUpdateExitCode_AndFailsOnSecretExitCode()
    {
        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var step = securityJob.Steps.Single(s => s.Name == "Run detect-secrets against the baseline");
        var onlyStepJob = new CiJob("security", "Security", [step]);
        var options = new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main");

        string? baselineState = "original-baseline";
        int simulatedExitCode = 3;

        var env = new LocalCiEnvironment
        {
            RepoRoot = _tempDir,
            Run = (_, _, _) =>
            {
                baselineState = "mutated-line-numbers";
                return simulatedExitCode;
            },
            ReadFile = _ => baselineState,
            RestoreFile = (_, content) => baselineState = content,
            IsToolOnPath = _ => true,
            ProbePostgres = () => new CiPostgresStatus(true, "ok"),
            FindFiles = (_, _) => ["apps/ctms/tests/test_site_startup.py"],
            DeleteDirectory = _ => { },
            FindSkippedDatabaseTests = _ => [],
        };

        var passResult = LocalCiRunner.Run([onlyStepJob], options, env).Single();
        passResult.Outcome.Should().Be(CiOutcome.Passed);
        baselineState.Should().Be("original-baseline");

        simulatedExitCode = 1;
        var failResult = LocalCiRunner.Run([onlyStepJob], options, env).Single();
        failResult.Outcome.Should().Be(CiOutcome.Failed);
        baselineState.Should().Be("original-baseline");
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

    private void RunGit(params string[] args)
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
        process.StandardOutput.ReadToEnd();
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, err);
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
}

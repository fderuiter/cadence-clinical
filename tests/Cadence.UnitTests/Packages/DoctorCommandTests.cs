using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DoctorCommandTests : IDisposable
{
    private readonly string _tempDir;

    public DoctorCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cadence-doctor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Execute_WithoutAutoFix_PerformsZeroWrites_AndReturnsOneWhenRequiredToolsAreMissing()
    {
        string repoRoot = CreateMinimalRepoFixture();
        string emptyPathDir = Path.Combine(_tempDir, "empty-path");
        string emptyBinDir = Path.Combine(repoRoot, ".cadence", "bin");
        Directory.CreateDirectory(emptyPathDir);

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = emptyPathDir,
            [DoctorProvisioningService.CadenceBinDirEnvVar] = emptyBinDir,
        };
        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Linux,
            Architecture.X64,
            env,
            (fileName, args, workingDir) =>
            {
                if (fileName == "dotnet" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.0.202", string.Empty);
                }

                if (fileName == "dotnet" && args.SequenceEqual(["tool", "run", "dotnet-ef", "--version"]))
                {
                    return new ProcessExecutionResult(1, string.Empty, "Run \"dotnet tool restore\" to make the \"dotnet-ef\" command available.");
                }

                if (fileName == "pnpm" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.30.3", string.Empty);
                }

                if (fileName == "git")
                {
                    return RunGit(workingDir, args);
                }

                return new ProcessExecutionResult(127, string.Empty, $"Not found: {fileName}");
            });

        int exitCode = DoctorCommand.Execute(autoFix: false, repoRoot: repoRoot, runtime: runtime);

        exitCode.Should().Be(1);
        Directory.Exists(Path.Combine(repoRoot, ".cadence")).Should().BeFalse("cadence doctor without --auto-fix must not create .cadence");
        File.Exists(Path.Combine(repoRoot, ".env")).Should().BeFalse("cadence doctor without --auto-fix must not initialize .env");
        Directory.Exists(Path.Combine(repoRoot, "packages", "ui", "dist")).Should().BeFalse("cadence doctor without --auto-fix must not build packages/ui/dist");
        ReadLocalHooksPath(repoRoot).Should().BeNull("cadence doctor without --auto-fix must not configure core.hooksPath");
        runtime.Invocations.Should().NotContain(inv =>
            inv.Contains("tool restore", StringComparison.Ordinal) ||
            inv.Contains("install", StringComparison.Ordinal) ||
            inv.Contains("venv", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_WithAutoFix_ProvisionsCadenceBinVenvDotnetToolsWorkspaceEnvAndGitHooks_ThenExitsZero()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string repoRoot = CreateMinimalRepoFixture();
        string payloadDir = Path.Combine(_tempDir, "payloads");
        Directory.CreateDirectory(payloadDir);

        (string alArchive, string alSha) = CreatePosixTarGzToolPayload(payloadDir, "actionlint", "#!/bin/sh\necho '1.7.12'\n");
        (string hlBinary, string hlSha) = CreatePosixRawToolPayload(payloadDir, "hadolint-linux-x86_64", "#!/bin/sh\necho 'Haskell Dockerfile Linter 2.15.1'\n");
        (string scArchive, string scSha) = CreatePosixShellcheckZipPayload(payloadDir, "#!/bin/sh\necho 'version: 0.11.0'\n");

        string emptyPathDir = Path.Combine(_tempDir, "isolated-path");
        Directory.CreateDirectory(emptyPathDir);

        bool dotnetToolsRestored = false;
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = emptyPathDir,
            [DoctorProvisioningService.ActionlintDownloadUrlEnvVar] = new Uri(alArchive).AbsoluteUri,
            [DoctorProvisioningService.ActionlintSha256EnvVar] = alSha,
            [DoctorProvisioningService.HadolintDownloadUrlEnvVar] = new Uri(hlBinary).AbsoluteUri,
            [DoctorProvisioningService.HadolintSha256EnvVar] = hlSha,
            [DoctorProvisioningService.ShellcheckDownloadUrlEnvVar] = new Uri(scArchive).AbsoluteUri,
            [DoctorProvisioningService.ShellcheckSha256EnvVar] = scSha,
        };

        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Linux,
            Architecture.X64,
            env,
            (fileName, args, workingDir) =>
            {
                if (fileName == "dotnet" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.0.202", string.Empty);
                }

                if (fileName == "dotnet" && args.SequenceEqual(["tool", "restore"]))
                {
                    dotnetToolsRestored = true;
                    return new ProcessExecutionResult(0, "Tool 'dotnet-ef' (version '10.0.15') was restored.", string.Empty);
                }

                if (fileName == "dotnet" && args.Count == 2 && args[0] == "restore")
                {
                    return new ProcessExecutionResult(0, "Restored Cadence.sln", string.Empty);
                }

                if (fileName == "dotnet" && args.SequenceEqual(["tool", "run", "dotnet-ef", "--version"]))
                {
                    return dotnetToolsRestored
                        ? new ProcessExecutionResult(0, "10.0.15", string.Empty)
                        : new ProcessExecutionResult(1, string.Empty, "Not restored");
                }

                if (fileName == "node" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "v22.14.0", string.Empty);
                }

                if (fileName == "pnpm" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.30.3", string.Empty);
                }

                if (fileName == "pnpm" && args.SequenceEqual(["install", "--frozen-lockfile"]))
                {
                    return new ProcessExecutionResult(0, "Done", string.Empty);
                }

                if (fileName == "uv" && args.Count == 2 && args[0] == "venv")
                {
                    Directory.CreateDirectory(Path.Combine(args[1], "bin"));
                    WritePosixExecutable(Path.Combine(args[1], "bin", "python"), "#!/bin/sh\necho 'Python 3.12'\n");
                    return new ProcessExecutionResult(0, "Created venv", string.Empty);
                }

                if (fileName == "uv" && args.Count >= 2 && args[0] == "pip" && args[1] == "install")
                {
                    string venvBin = Path.Combine(workingDir, ".cadence", "venv", "bin");
                    Directory.CreateDirectory(venvBin);
                    WritePosixExecutable(
                        Path.Combine(venvBin, "detect-secrets-hook"),
                        $"#!/bin/sh\necho '{DetectSecretsGateService.PinnedVersion}'\n");
                    return new ProcessExecutionResult(0, "Installed detect-secrets", string.Empty);
                }

                if (fileName == "tar" && args.Count == 4 && args[0] == "-xJf")
                {
                    ZipFile.ExtractToDirectory(args[1], args[3], overwriteFiles: true);
                    return new ProcessExecutionResult(0, string.Empty, string.Empty);
                }

                if (fileName == "git")
                {
                    return RunGit(workingDir, args);
                }

                if (File.Exists(fileName))
                {
                    return RunPosixBinary(fileName, args, workingDir);
                }

                return new ProcessExecutionResult(127, string.Empty, $"Unexpected command: {fileName} {string.Join(' ', args)}");
            },
            resolvableTools: ["uv", "xz"]);

        int autoFixExit = DoctorCommand.Execute(autoFix: true, minimal: true, repoRoot: repoRoot, runtime: runtime);

        autoFixExit.Should().Be(0);
        dotnetToolsRestored.Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "actionlint")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "hadolint")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "shellcheck")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "venv", "bin", "detect-secrets-hook")).Should().BeTrue();
        Directory.Exists(Path.Combine(repoRoot, "packages", "ui", "dist")).Should().BeFalse("apps resolve packages/ui directly from workspace source per ADR-2658");
        File.Exists(Path.Combine(repoRoot, ".env")).Should().BeTrue();
        ReadLocalHooksPath(repoRoot).Should().Be(".githooks");
        var preCommitMode = File.GetUnixFileMode(Path.Combine(repoRoot, ".githooks", "pre-commit"));
        (preCommitMode & UnixFileMode.UserExecute).Should().NotBe(0);
        runtime.Invocations.Should().NotContain(inv => inv.Contains("playwright", StringComparison.OrdinalIgnoreCase));

        int readOnlyExit = DoctorCommand.Execute(autoFix: false, repoRoot: repoRoot, runtime: runtime);
        readOnlyExit.Should().Be(0);
    }

    [Fact]
    public void Execute_WithAutoFixAndLintersOnly_ProvisionsOnlyCliLinters_WithoutTouchingVenvEnvOrGitHooks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string repoRoot = CreateMinimalRepoFixture();
        string payloadDir = Path.Combine(_tempDir, "linters-only-payloads");
        Directory.CreateDirectory(payloadDir);

        (string alArchive, string alSha) = CreatePosixTarGzToolPayload(payloadDir, "actionlint", "#!/bin/sh\necho '1.7.12'\n");
        (string hlBinary, string hlSha) = CreatePosixRawToolPayload(payloadDir, "hadolint-linux-x86_64", "#!/bin/sh\necho 'Haskell Dockerfile Linter 2.15.1'\n");
        (string scArchive, string scSha) = CreatePosixShellcheckZipPayload(payloadDir, "#!/bin/sh\necho 'version: 0.11.0'\n");

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = Path.Combine(_tempDir, "empty-linters-path"),
            [DoctorProvisioningService.ActionlintDownloadUrlEnvVar] = new Uri(alArchive).AbsoluteUri,
            [DoctorProvisioningService.ActionlintSha256EnvVar] = alSha,
            [DoctorProvisioningService.HadolintDownloadUrlEnvVar] = new Uri(hlBinary).AbsoluteUri,
            [DoctorProvisioningService.HadolintSha256EnvVar] = hlSha,
            [DoctorProvisioningService.ShellcheckDownloadUrlEnvVar] = new Uri(scArchive).AbsoluteUri,
            [DoctorProvisioningService.ShellcheckSha256EnvVar] = scSha,
        };
        Directory.CreateDirectory(env["PATH"]!);

        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Linux,
            Architecture.X64,
            env,
            (fileName, args, workingDir) =>
            {
                if (fileName == "tar" && args.Count == 4 && args[0] == "-xJf")
                {
                    ZipFile.ExtractToDirectory(args[1], args[3], overwriteFiles: true);
                    return new ProcessExecutionResult(0, string.Empty, string.Empty);
                }

                if (File.Exists(fileName))
                {
                    return RunPosixBinary(fileName, args, workingDir);
                }

                return new ProcessExecutionResult(127, string.Empty, $"Unexpected command: {fileName}");
            },
            resolvableTools: ["xz"]);

        int exitCode = DoctorCommand.Execute(autoFix: true, lintersOnly: true, repoRoot: repoRoot, runtime: runtime);

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "actionlint")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "hadolint")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "shellcheck")).Should().BeTrue();
        Directory.Exists(Path.Combine(repoRoot, ".cadence", "venv")).Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, ".env")).Should().BeFalse();
        ReadLocalHooksPath(repoRoot).Should().BeNull();
    }

    [Fact]
    public void ChecksumMatrix_CoversAllSixPlatformArchitectures_AndRejectsTamperedArchive()
    {
        var matrix = DoctorProvisioningService.GetPinnedChecksumMatrix();
        var expectedKeys = new[]
        {
            (ProvisioningPlatform.Linux, Architecture.X64),
            (ProvisioningPlatform.Linux, Architecture.Arm64),
            (ProvisioningPlatform.MacOS, Architecture.X64),
            (ProvisioningPlatform.MacOS, Architecture.Arm64),
            (ProvisioningPlatform.Windows, Architecture.X64),
            (ProvisioningPlatform.Windows, Architecture.Arm64),
        };

        matrix.Keys.Should().BeEquivalentTo(expectedKeys);
        foreach (var key in expectedKeys)
        {
            var entry = matrix[key];
            entry.ActionlintSha.Should().MatchRegex("^[0-9a-f]{64}$");
            entry.HadolintSha.Should().MatchRegex("^[0-9a-f]{64}$");
            entry.ShellcheckSha.Should().MatchRegex("^[0-9a-f]{64}$");

            DoctorProvisioningService.TryGetLinterDescriptors(key.Item1, key.Item2, out var descriptors, out string error)
                .Should().BeTrue(error);
            descriptors.Select(d => d.ToolName).Should().Equal("actionlint", "hadolint", "shellcheck");
        }

        DoctorProvisioningService.TryGetLinterDescriptors(ProvisioningPlatform.Unsupported, Architecture.X64, out _, out string unsupportedError)
            .Should().BeFalse();
        unsupportedError.Should().Contain("Unsupported operating system");

        string repoRoot = CreateMinimalRepoFixture();
        string tamperedFile = Path.Combine(_tempDir, "tampered.exe");
        File.WriteAllText(tamperedFile, "tampered-payload");

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DoctorProvisioningService.ActionlintDownloadUrlEnvVar] = new Uri(tamperedFile).AbsoluteUri,
            [DoctorProvisioningService.ActionlintSha256EnvVar] = new string('a', 64),
            [DoctorProvisioningService.HadolintDownloadUrlEnvVar] = new Uri(tamperedFile).AbsoluteUri,
            [DoctorProvisioningService.HadolintSha256EnvVar] = new string('b', 64),
            [DoctorProvisioningService.ShellcheckDownloadUrlEnvVar] = new Uri(tamperedFile).AbsoluteUri,
            [DoctorProvisioningService.ShellcheckSha256EnvVar] = new string('c', 64),
        };

        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Windows,
            Architecture.X64,
            env,
            (_, _, _) => new ProcessExecutionResult(0, string.Empty, string.Empty));

        var result = DoctorProvisioningService.Provision(
            new DoctorProvisioningOptions(repoRoot, LintersOnly: true),
            runtime);

        result.Succeeded.Should().BeFalse();
        result.Steps.Should().Contain(s => !s.Succeeded && s.Message.Contains("Checksum verification failed for actionlint", StringComparison.Ordinal));
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "actionlint.exe")).Should().BeFalse();
    }

    [Fact]
    public void Provision_WindowsArm64_WhenHadolintVersionExecutionFails_DeletesBinaryAndKeepsHooksInactive()
    {
        string repoRoot = CreateMinimalRepoFixture();
        string payloadDir = Path.Combine(_tempDir, "win-arm64-payloads");
        Directory.CreateDirectory(payloadDir);

        (string alZip, string alSha) = CreateZipToolPayload(payloadDir, "actionlint_1.7.12_windows_arm64.zip", "actionlint.exe", "fake-al-arm64");
        string hlExe = Path.Combine(payloadDir, "hadolint-windows-x86_64.exe");
        File.WriteAllText(hlExe, "fake-hl-x64");
        string hlSha = ComputeSha256Hex(hlExe);
        (string scZip, string scSha) = CreateZipToolPayload(payloadDir, "shellcheck-v0.11.0.zip", "shellcheck.exe", "fake-sc-x64");

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DoctorProvisioningService.ActionlintDownloadUrlEnvVar] = new Uri(alZip).AbsoluteUri,
            [DoctorProvisioningService.ActionlintSha256EnvVar] = alSha,
            [DoctorProvisioningService.HadolintDownloadUrlEnvVar] = new Uri(hlExe).AbsoluteUri,
            [DoctorProvisioningService.HadolintSha256EnvVar] = hlSha,
            [DoctorProvisioningService.ShellcheckDownloadUrlEnvVar] = new Uri(scZip).AbsoluteUri,
            [DoctorProvisioningService.ShellcheckSha256EnvVar] = scSha,
        };

        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Windows,
            Architecture.Arm64,
            env,
            (fileName, args, workingDir) =>
            {
                if (fileName == "dotnet" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.0.202", string.Empty);
                }

                if (fileName == "dotnet" && args.SequenceEqual(["tool", "restore"]))
                {
                    return new ProcessExecutionResult(0, "Restored", string.Empty);
                }

                if (fileName == "dotnet" && args.Count == 2 && args[0] == "restore")
                {
                    return new ProcessExecutionResult(0, "Restored Cadence.sln", string.Empty);
                }

                if (fileName == "node" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "v22.14.0", string.Empty);
                }

                if (fileName == "pnpm" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.30.3", string.Empty);
                }

                if (fileName == "uv" && args.Count == 2 && args[0] == "venv")
                {
                    Directory.CreateDirectory(Path.Combine(args[1], "Scripts"));
                    File.WriteAllText(Path.Combine(args[1], "Scripts", "python.exe"), "py");
                    return new ProcessExecutionResult(0, "ok", string.Empty);
                }

                if (fileName == "uv" && args.Count >= 2 && args[0] == "pip")
                {
                    string scriptsDir = Path.Combine(workingDir, ".cadence", "venv", "Scripts");
                    Directory.CreateDirectory(scriptsDir);
                    File.WriteAllText(Path.Combine(scriptsDir, "detect-secrets-hook.exe"), "ds");
                    return new ProcessExecutionResult(0, "ok", string.Empty);
                }

                if (fileName.EndsWith("detect-secrets-hook.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessExecutionResult(0, DetectSecretsGateService.PinnedVersion, string.Empty);
                }

                if (fileName.EndsWith("actionlint.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessExecutionResult(0, "1.7.12", string.Empty);
                }

                if (fileName.EndsWith("hadolint.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessExecutionResult(1, string.Empty, "This app can't run on your PC (x64 emulation unavailable).");
                }

                if (fileName.EndsWith("shellcheck.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessExecutionResult(0, "version: 0.11.0", string.Empty);
                }

                if (fileName == "git")
                {
                    return RunGit(workingDir, args);
                }

                return new ProcessExecutionResult(127, string.Empty, $"Unexpected command: {fileName}");
            },
            resolvableTools: ["uv"]);

        var result = DoctorProvisioningService.Provision(
            new DoctorProvisioningOptions(repoRoot, Minimal: true),
            runtime);

        result.Succeeded.Should().BeFalse();
        result.Steps.Should().Contain(s =>
            s.Step == "hadolint" &&
            !s.Succeeded &&
            s.Message.Contains("Windows x64 emulation is required", StringComparison.Ordinal));
        File.Exists(Path.Combine(repoRoot, ".cadence", "bin", "hadolint.exe")).Should().BeFalse("unusable hadolint.exe must be deleted");
        ReadLocalHooksPath(repoRoot).Should().BeNull("core.hooksPath must remain unset when blocking provisioning fails");
    }

    [Fact]
    public void SetupWizardSh_ExitsOneWithoutSettingHooksPath_WhenNodeOrDotnetIsMissing()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string realRepoRoot = QualityGateOrchestrator.FindRepoRoot();
        string wizardRepo = CreateMinimalRepoFixture();
        Directory.CreateDirectory(Path.Combine(wizardRepo, "scripts"));

        foreach (string scriptName in new[] { "setup_wizard.sh", "setup_dotnet_env.sh", "resolve_dotnet_env.sh" })
        {
            string dst = Path.Combine(wizardRepo, "scripts", scriptName);
            File.Copy(Path.Combine(realRepoRoot, "scripts", scriptName), dst, overwrite: true);
            WritePosixExecutable(dst, File.ReadAllText(dst));
        }

        File.Copy(Path.Combine(realRepoRoot, "global.json"), Path.Combine(wizardRepo, "global.json"), overwrite: true);

        // Case 1: dotnet present, node missing -> setup_wizard.sh exits 1 without setting core.hooksPath.
        string noNodeBin = Path.Combine(_tempDir, "no-node-bin");
        Directory.CreateDirectory(noNodeBin);
        SymlinkBasicPosixTools(noNodeBin);
        WritePosixExecutable(
            Path.Combine(noNodeBin, "dotnet"),
            """
            #!/bin/sh
            if [ "$1" = "--version" ]; then
              echo "10.0.202"
              exit 0
            fi
            if [ "$1" = "tool" ] && [ "$2" = "restore" ]; then
              exit 0
            fi
            if [ "$1" = "dev-certs" ]; then
              exit 0
            fi
            exit 0
            """);

        (int noNodeExit, string noNodeOut) = RunBashScript(
            Path.Combine(wizardRepo, "scripts", "setup_wizard.sh"),
            ["--minimal"],
            wizardRepo,
            noNodeBin);
        noNodeExit.Should().Be(1, noNodeOut);
        noNodeOut.Should().Contain("Node.js (node) not found on PATH");
        ReadLocalHooksPath(wizardRepo).Should().BeNull("setup_wizard.sh must not set core.hooksPath when node is missing");

        // Case 2: node & pnpm present, dotnet missing -> setup_wizard.sh exits 1 without setting core.hooksPath.
        string noDotnetBin = Path.Combine(_tempDir, "no-dotnet-bin");
        Directory.CreateDirectory(noDotnetBin);
        SymlinkBasicPosixTools(noDotnetBin);
        WritePosixExecutable(Path.Combine(noDotnetBin, "node"), "#!/bin/sh\necho 'v22.14.0'\n");
        WritePosixExecutable(Path.Combine(noDotnetBin, "pnpm"), "#!/bin/sh\necho '10.30.3'\n");

        (int noDotnetExit, string noDotnetOut) = RunBashScript(
            Path.Combine(wizardRepo, "scripts", "setup_wizard.sh"),
            ["--linters-only"],
            wizardRepo,
            noDotnetBin);
        noDotnetExit.Should().Be(1, noDotnetOut);
        noDotnetOut.Should().Contain(".NET 10 SDK (dotnet) not found");
        ReadLocalHooksPath(wizardRepo).Should().BeNull("setup_wizard.sh must not set core.hooksPath when dotnet is missing");

        // Case 3: full setup_wizard.sh (--minimal) when setup_dotnet_env.sh cannot install .NET -> exits 1 without setting core.hooksPath.
        WritePosixExecutable(Path.Combine(noDotnetBin, "curl"), "#!/bin/sh\nexit 1\n");
        (int failedInstallExit, string failedInstallOut) = RunBashScript(
            Path.Combine(wizardRepo, "scripts", "setup_wizard.sh"),
            ["--minimal"],
            wizardRepo,
            noDotnetBin);
        failedInstallExit.Should().Be(1, failedInstallOut);
        failedInstallOut.Should().Contain("aborting before configuring Git hooks");
        ReadLocalHooksPath(wizardRepo).Should().BeNull("setup_wizard.sh must not set core.hooksPath when setup_dotnet_env.sh fails");
    }

    [Fact]
    public void Provision_WhenPnpmInstallMutatesHooksPathBeforeFailing_RestoresInitialHooksPath()
    {
        string repoRoot = CreateMinimalRepoFixture();
        string payloadDir = Path.Combine(_tempDir, "rollback-payloads");
        Directory.CreateDirectory(payloadDir);

        (string alZip, string alSha) = CreateZipToolPayload(payloadDir, "actionlint_1.7.12_windows_x86_64.zip", "actionlint.exe", "fake-al");
        string hlExe = Path.Combine(payloadDir, "hadolint-windows-x86_64.exe");
        File.WriteAllText(hlExe, "fake-hl");
        string hlSha = ComputeSha256Hex(hlExe);
        (string scZip, string scSha) = CreateZipToolPayload(payloadDir, "shellcheck-v0.11.0.zip", "shellcheck.exe", "fake-sc");

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DoctorProvisioningService.ActionlintDownloadUrlEnvVar] = new Uri(alZip).AbsoluteUri,
            [DoctorProvisioningService.ActionlintSha256EnvVar] = alSha,
            [DoctorProvisioningService.HadolintDownloadUrlEnvVar] = new Uri(hlExe).AbsoluteUri,
            [DoctorProvisioningService.HadolintSha256EnvVar] = hlSha,
            [DoctorProvisioningService.ShellcheckDownloadUrlEnvVar] = new Uri(scZip).AbsoluteUri,
            [DoctorProvisioningService.ShellcheckSha256EnvVar] = scSha,
        };

        var runtime = new FakeDoctorRuntime(
            ProvisioningPlatform.Windows,
            Architecture.X64,
            env,
            (fileName, args, workingDir) =>
            {
                if (fileName == "dotnet" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.0.202", string.Empty);
                }

                if (fileName == "dotnet" && args.SequenceEqual(["tool", "restore"]))
                {
                    return new ProcessExecutionResult(0, "Restored", string.Empty);
                }

                if (fileName == "dotnet" && args.Count == 2 && args[0] == "restore")
                {
                    return new ProcessExecutionResult(0, "Restored Cadence.sln", string.Empty);
                }

                if (fileName == "node" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "v22.14.0", string.Empty);
                }

                if (fileName == "pnpm" && args.SequenceEqual(["--version"]))
                {
                    return new ProcessExecutionResult(0, "10.30.3", string.Empty);
                }

                if (fileName == "pnpm" && args.SequenceEqual(["install", "--frozen-lockfile"]))
                {
                    // Simulate root package.json "prepare": "node scripts/setup-git-hooks.js" mutating core.hooksPath before a workspace lifecycle script fails.
                    _ = RunGit(workingDir, ["config", "core.hooksPath", ".githooks"]);
                    return new ProcessExecutionResult(1, string.Empty, "Workspace postinstall failed");
                }

                if (fileName == "uv" && args.Count == 2 && args[0] == "venv")
                {
                    Directory.CreateDirectory(Path.Combine(args[1], "Scripts"));
                    File.WriteAllText(Path.Combine(args[1], "Scripts", "python.exe"), "py");
                    return new ProcessExecutionResult(0, "ok", string.Empty);
                }

                if (fileName == "uv" && args.Count >= 2 && args[0] == "pip")
                {
                    string scriptsDir = Path.Combine(workingDir, ".cadence", "venv", "Scripts");
                    Directory.CreateDirectory(scriptsDir);
                    File.WriteAllText(Path.Combine(scriptsDir, "detect-secrets-hook.exe"), "ds");
                    return new ProcessExecutionResult(0, "ok", string.Empty);
                }

                if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessExecutionResult(0, $"{DetectSecretsGateService.PinnedVersion} 1.7.12 2.15.1 0.11.0", string.Empty);
                }

                if (fileName == "git")
                {
                    return RunGit(workingDir, args);
                }

                return new ProcessExecutionResult(127, string.Empty, $"Unexpected: {fileName}");
            },
            resolvableTools: ["uv"]);

        var result = DoctorProvisioningService.Provision(
            new DoctorProvisioningOptions(repoRoot, Minimal: true),
            runtime);

        result.Succeeded.Should().BeFalse();
        ReadLocalHooksPath(repoRoot).Should().BeNull("core.hooksPath must be rolled back if pnpm prepare mutated it before a blocking step failed");
    }

    [Fact]
    public void SetupScriptsAndMakefile_DelegateProvisioningToCadenceDoctor_AndEnforceFailClosedPrerequisites()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();

        string wizardPs1 = File.ReadAllText(Path.Combine(repoRoot, "scripts", "setup_wizard.ps1"));
        wizardPs1.Should().Contain("[switch]$LintersOnly");
        wizardPs1.Should().Contain("Get-Command dotnet");
        wizardPs1.Should().Contain("Get-Command node");
        wizardPs1.Should().Contain("Get-Command pnpm");
        wizardPs1.Should().Contain("doctor", "setup_wizard.ps1 must delegate provisioning to cadence doctor --auto-fix");
        wizardPs1.Should().Contain("--auto-fix");
        wizardPs1.Should().NotContain("pnpm cadence setup", "deprecated/non-existent 'pnpm cadence setup --dev' hint must be removed");
        wizardPs1.Should().NotContain("core.hooksPath", "setup_wizard.ps1 must not directly set core.hooksPath before doctor succeeds");

        string wizardSh = File.ReadAllText(Path.Combine(repoRoot, "scripts", "setup_wizard.sh"));
        wizardSh.Should().Contain("command -v dotnet");
        wizardSh.Should().Contain("command -v node");
        wizardSh.Should().Contain("command -v pnpm");
        wizardSh.Should().Contain("DOCTOR_ARGS=(\"doctor\" \"--auto-fix\")");
        wizardSh.Should().Contain("run-cli.js\" \"${DOCTOR_ARGS[@]}\"");
        wizardSh.Should().NotContain("core.hooksPath", "setup_wizard.sh must not directly set core.hooksPath before doctor succeeds");

        foreach (string dotnetScript in new[]
                 {
                     "setup_dotnet_env.sh",
                     "setup_dotnet_env.ps1",
                     "setup_dotnet_wizard.sh",
                     "setup_dotnet_wizard.ps1",
                 })
        {
            string content = File.ReadAllText(Path.Combine(repoRoot, "scripts", dotnetScript));
            content.Should().Contain("dotnet tool restore", $"{dotnetScript} must restore pinned local tools from .config/dotnet-tools.json");
            content.Should().NotContain("--global", $"{dotnetScript} must not install unpinned global dotnet-ef tools");
        }

        string makefile = File.ReadAllText(Path.Combine(repoRoot, "Makefile"));
        makefile.Should().Contain("bash scripts/setup_wizard.sh --dev --minimal");
    }

    private string CreateMinimalRepoFixture()
    {
        string repoRoot = Path.Combine(_tempDir, $"repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, ".config"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".githooks"));
        Directory.CreateDirectory(Path.Combine(repoRoot, "packages", "ui"));

        File.WriteAllText(Path.Combine(repoRoot, "Cadence.sln"), string.Empty);
        File.WriteAllText(Path.Combine(repoRoot, "Directory.Packages.props"), "<Project />\n");
        File.WriteAllText(
            Path.Combine(repoRoot, "package.json"),
            """
            {
              "name": "cadence-test",
              "private": true,
              "packageManager": "pnpm@10.30.3"
            }
            """);
        File.WriteAllText(Path.Combine(repoRoot, "pnpm-lock.yaml"), "lockfileVersion: '9.0'\n");
        File.WriteAllText(
            Path.Combine(repoRoot, ".config", "dotnet-tools.json"),
            """
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "dotnet-ef": {
                  "version": "10.0.15",
                  "commands": ["dotnet-ef"],
                  "rollForward": false
                }
              }
            }
            """);
        File.WriteAllText(Path.Combine(repoRoot, "packages", "ui", "package.json"), "{ \"name\": \"ui\" }\n");
        File.WriteAllText(Path.Combine(repoRoot, ".env.example"), "POSTGRES_PORT=5432\n");
        File.WriteAllText(Path.Combine(repoRoot, ".githooks", "pre-commit"), "#!/usr/bin/env sh\nexit 0\n");

        RunGit(repoRoot, ["init", "-q"]).ExitCode.Should().Be(0);
        return repoRoot;
    }

    private static (string ArchivePath, string Sha256) CreatePosixTarGzToolPayload(string dir, string binaryName, string scriptContent)
    {
        string stagingDir = Path.Combine(dir, $"stage-{binaryName}");
        Directory.CreateDirectory(stagingDir);
        string binaryPath = Path.Combine(stagingDir, binaryName);
        WritePosixExecutable(binaryPath, scriptContent);

        string archivePath = Path.Combine(dir, $"{binaryName}.tar.gz");
        using (var fileStream = File.Create(archivePath))
        using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(stagingDir, gzipStream, includeBaseDirectory: false);
        }

        return (archivePath, ComputeSha256Hex(archivePath));
    }

    private static (string BinaryPath, string Sha256) CreatePosixRawToolPayload(string dir, string fileName, string scriptContent)
    {
        string binaryPath = Path.Combine(dir, fileName);
        WritePosixExecutable(binaryPath, scriptContent);
        return (binaryPath, ComputeSha256Hex(binaryPath));
    }

    private static (string ArchivePath, string Sha256) CreatePosixShellcheckZipPayload(string dir, string scriptContent)
    {
        string stagingDir = Path.Combine(dir, "stage-shellcheck");
        string nestedDir = Path.Combine(stagingDir, "shellcheck-v0.11.0");
        Directory.CreateDirectory(nestedDir);
        WritePosixExecutable(Path.Combine(nestedDir, "shellcheck"), scriptContent);

        string archivePath = Path.Combine(dir, "shellcheck-v0.11.0.linux.x86_64.tar.xz");
        ZipFile.CreateFromDirectory(stagingDir, archivePath);
        return (archivePath, ComputeSha256Hex(archivePath));
    }

    private static (string ArchivePath, string Sha256) CreateZipToolPayload(string dir, string zipName, string exeName, string content)
    {
        string stagingDir = Path.Combine(dir, $"stage-{exeName}");
        Directory.CreateDirectory(stagingDir);
        File.WriteAllText(Path.Combine(stagingDir, exeName), content);
        string archivePath = Path.Combine(dir, zipName);
        ZipFile.CreateFromDirectory(stagingDir, archivePath);
        return (archivePath, ComputeSha256Hex(archivePath));
    }

    private static void WritePosixExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static string ComputeSha256Hex(string filePath) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(filePath)));

    private static ProcessExecutionResult RunGit(string workingDir, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd().Trim();
        string stderr = proc.StandardError.ReadToEnd().Trim();
        proc.WaitForExit();
        return new ProcessExecutionResult(proc.ExitCode, stdout, stderr);
    }

    private static ProcessExecutionResult RunPosixBinary(string fileName, IReadOnlyList<string> args, string workingDir)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd().Trim();
        string stderr = proc.StandardError.ReadToEnd().Trim();
        proc.WaitForExit();
        return new ProcessExecutionResult(proc.ExitCode, stdout, stderr);
    }

    private static string? ReadLocalHooksPath(string repoRoot)
    {
        var res = RunGit(repoRoot, ["config", "--local", "--get", "core.hooksPath"]);
        return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StdOut) ? res.StdOut.Trim() : null;
    }

    private static void SymlinkBasicPosixTools(string targetBinDir)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] searchDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string tool in new[] { "sh", "bash", "pwd", "dirname", "uname", "id", "env", "mkdir", "rm", "cp", "chmod", "cat", "grep", "awk", "tr", "head" })
        {
            string? found = searchDirs
                .Select(d => Path.Combine(d, tool))
                .FirstOrDefault(File.Exists);
            if (found is not null)
            {
                File.CreateSymbolicLink(Path.Combine(targetBinDir, tool), found);
            }
        }
    }

    private static (int ExitCode, string CombinedOutput) RunBashScript(
        string scriptPath,
        IReadOnlyList<string> args,
        string workingDir,
        string customPath)
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["PATH"] = customPath;
        psi.Environment["HOME"] = Path.Combine(workingDir, "isolated-home");
        Directory.CreateDirectory(psi.Environment["HOME"]!);
        psi.Environment.Remove("DOTNET_ROOT");

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout + stderr);
    }

    private sealed class FakeDoctorRuntime(
        ProvisioningPlatform platform,
        Architecture cpuArchitecture,
        IReadOnlyDictionary<string, string?> environment,
        Func<string, IReadOnlyList<string>, string, ProcessExecutionResult> processRunner,
        IReadOnlyCollection<string>? resolvableTools = null) : IDoctorProvisioningRuntime
    {
        private readonly HashSet<string> _resolvableTools = new(resolvableTools ?? [], StringComparer.OrdinalIgnoreCase);

        public List<string> Invocations { get; } = [];

        public ProvisioningPlatform Platform => platform;

        public Architecture CpuArchitecture => cpuArchitecture;

        public string? GetEnvironmentVariable(string name) =>
            environment.TryGetValue(name, out string? value) ? value : null;

        public string? ResolveExecutable(string toolName, string workingDirectory) =>
            _resolvableTools.Contains(toolName) ? $"/mock/{toolName}" : null;

        public ProcessExecutionResult RunProcess(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            int timeoutMs = 300_000)
        {
            Invocations.Add($"{fileName} {string.Join(' ', arguments)}");
            return processRunner(fileName, arguments, workingDirectory);
        }

        public void DownloadFile(string url, string destinationPath)
        {
            var uri = new Uri(url);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(uri.LocalPath, destinationPath, overwrite: true);
        }

        public void SetPosixExecutablePermissions(string filePath)
        {
            if (!OperatingSystem.IsWindows() && File.Exists(filePath))
            {
                WritePosixExecutable(filePath, File.ReadAllText(filePath));
            }
        }
    }
}

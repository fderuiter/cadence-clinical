using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Cadence.ToolingCore.Services;

public static partial class DoctorProvisioningService
{
    private static void ProvisionCliLinters(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        if (!TryGetLinterDescriptors(runtime.Platform, runtime.CpuArchitecture, out var descriptors, out string platformError))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "cli-linters",
                Succeeded: false,
                Blocking: true,
                platformError));
            return;
        }

        string cadenceBinDir = ResolveCadenceBinDirectory(repoRoot, runtime.GetEnvironmentVariable);
        Directory.CreateDirectory(cadenceBinDir);

        foreach (var descriptor in descriptors)
        {
            ProvisionSingleLinter(repoRoot, cadenceBinDir, descriptor, runtime, steps);
        }
    }

    private static void ProvisionSingleLinter(
        string repoRoot,
        string cadenceBinDir,
        LinterPinDescriptor descriptor,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        bool isWindows = runtime.Platform == ProvisioningPlatform.Windows;
        string exeName = isWindows ? $"{descriptor.ToolName}.exe" : descriptor.ToolName;
        string targetBinaryPath = Path.Combine(cadenceBinDir, exeName);

        (string urlEnvName, string shaEnvName) = descriptor.ToolName switch
        {
            "actionlint" => (ActionlintDownloadUrlEnvVar, ActionlintSha256EnvVar),
            "hadolint" => (HadolintDownloadUrlEnvVar, HadolintSha256EnvVar),
            _ => (ShellcheckDownloadUrlEnvVar, ShellcheckSha256EnvVar),
        };

        string? overrideUrl = runtime.GetEnvironmentVariable(urlEnvName);
        string? overrideSha = runtime.GetEnvironmentVariable(shaEnvName);
        bool hasOverride = !string.IsNullOrWhiteSpace(overrideUrl) || !string.IsNullOrWhiteSpace(overrideSha);

        if (!hasOverride && File.Exists(targetBinaryPath))
        {
            var probe = runtime.RunProcess(targetBinaryPath, [descriptor.VersionFlag], repoRoot, 15_000);
            if (probe.ExitCode == 0 && probe.CombinedOutput.Contains(descriptor.Version, StringComparison.Ordinal))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    descriptor.ToolName,
                    Succeeded: true,
                    Blocking: true,
                    $"{descriptor.ToolName} {descriptor.Version} already provisioned in .cadence/bin."));
                return;
            }
        }

        string downloadUrl = !string.IsNullOrWhiteSpace(overrideUrl) ? overrideUrl : descriptor.DefaultUrl;
        string expectedSha = (!string.IsNullOrWhiteSpace(overrideSha) ? overrideSha : descriptor.PinnedSha256).Trim().ToLowerInvariant();

        if (descriptor.AssetName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) &&
            runtime.ResolveExecutable("xz", repoRoot) is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                descriptor.ToolName,
                Succeeded: false,
                Blocking: true,
                "xz is required to extract ShellCheck (.tar.xz). Install xz-utils (for example 'sudo apt-get install -y xz-utils' on Debian/Ubuntu or 'brew install xz' on macOS) and re-run 'pnpm cadence doctor --auto-fix'."));
            return;
        }

        string tempWorkDir = Path.Combine(Path.GetTempPath(), $"cadence-{descriptor.ToolName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempWorkDir);
        string archiveOrBinaryPath = Path.Combine(tempWorkDir, descriptor.AssetName);

        try
        {
            try
            {
                runtime.DownloadFile(downloadUrl, archiveOrBinaryPath);
            }
            catch (Exception ex)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    descriptor.ToolName,
                    Succeeded: false,
                    Blocking: true,
                    $"Failed to download {descriptor.ToolName} v{descriptor.Version} from {downloadUrl}: {ex.Message}"));
                return;
            }

            string actualSha = ComputeSha256Lower(archiveOrBinaryPath);
            if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    descriptor.ToolName,
                    Succeeded: false,
                    Blocking: true,
                    $"Checksum verification failed for {descriptor.ToolName} ({descriptor.AssetName}) (expected {expectedSha}, got {actualSha})."));
                return;
            }

            string? extractedBinary = ExtractOrSelectLinterBinary(
                repoRoot,
                tempWorkDir,
                archiveOrBinaryPath,
                descriptor,
                exeName,
                runtime,
                out string extractError);
            if (extractedBinary is null)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    descriptor.ToolName,
                    Succeeded: false,
                    Blocking: true,
                    extractError));
                return;
            }

            File.Copy(extractedBinary, targetBinaryPath, overwrite: true);
            runtime.SetPosixExecutablePermissions(targetBinaryPath);

            var versionCheck = runtime.RunProcess(targetBinaryPath, [descriptor.VersionFlag], repoRoot, 15_000);
            if (versionCheck.ExitCode != 0)
            {
                TryDeleteFile(targetBinaryPath);
                string compatibilityNote = isWindows && runtime.CpuArchitecture == Architecture.Arm64 && descriptor.ToolName == "hadolint"
                    ? "hadolint.exe (x86_64) failed to run '--version' on Windows arm64; Windows x64 emulation is required."
                    : $"{exeName} failed post-install '{descriptor.VersionFlag}' verification ({versionCheck.CombinedOutput}).";
                steps.Add(new DoctorProvisioningStepResult(
                    descriptor.ToolName,
                    Succeeded: false,
                    Blocking: true,
                    compatibilityNote));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                descriptor.ToolName,
                Succeeded: true,
                Blocking: true,
                $"{descriptor.ToolName} {descriptor.Version} SHA-256 verified and installed in .cadence/bin."));
        }
        finally
        {
            TryDeleteDirectory(tempWorkDir);
        }
    }

    private static string? ExtractOrSelectLinterBinary(
        string repoRoot,
        string tempWorkDir,
        string archiveOrBinaryPath,
        LinterPinDescriptor descriptor,
        string exeName,
        IDoctorProvisioningRuntime runtime,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        string extractDir = Path.Combine(tempWorkDir, "extracted");
        Directory.CreateDirectory(extractDir);

        try
        {
            if (descriptor.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archiveOrBinaryPath, extractDir, overwriteFiles: true);
            }
            else if (descriptor.AssetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var fileStream = File.OpenRead(archiveOrBinaryPath);
                using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzipStream, extractDir, overwriteFiles: true);
            }
            else if (descriptor.AssetName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
            {
                var tarResult = runtime.RunProcess("tar", ["-xJf", archiveOrBinaryPath, "-C", extractDir], repoRoot, 60_000);
                if (tarResult.ExitCode != 0)
                {
                    errorMessage = $"Failed to extract {descriptor.AssetName}: {tarResult.CombinedOutput}";
                    return null;
                }
            }
            else
            {
                return archiveOrBinaryPath;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            errorMessage = $"Failed to extract {descriptor.AssetName}: {ex.Message}";
            return null;
        }

        string directCandidate = Path.Combine(extractDir, exeName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        string versionedSubdirCandidate = Path.Combine(extractDir, $"{descriptor.ToolName}-v{descriptor.Version}", exeName);
        if (File.Exists(versionedSubdirCandidate))
        {
            return versionedSubdirCandidate;
        }

        string? found = Directory.EnumerateFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
        if (found is not null)
        {
            return found;
        }

        errorMessage = $"Failed to locate {exeName} inside {descriptor.AssetName}.";
        return null;
    }

    private static void ProvisionGitHooks(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        string gitMarker = Path.Combine(repoRoot, ".git");
        string preCommitPath = Path.Combine(repoRoot, ".githooks", "pre-commit");
        if ((!Directory.Exists(gitMarker) && !File.Exists(gitMarker)) || !File.Exists(preCommitPath))
        {
            return;
        }

        var gitConfig = runtime.RunProcess("git", ["config", "core.hooksPath", ".githooks"], repoRoot, 15_000);
        if (gitConfig.ExitCode != 0)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "git-hooks",
                Succeeded: false,
                Blocking: true,
                $"Failed to set git config core.hooksPath .githooks: {gitConfig.CombinedOutput}"));
            return;
        }

        runtime.SetPosixExecutablePermissions(preCommitPath);
        steps.Add(new DoctorProvisioningStepResult(
            "git-hooks",
            Succeeded: true,
            Blocking: true,
            "Configured Git pre-commit hook (.githooks/pre-commit -> cadence check --pre-commit)."));
    }

    private static string? ReadGitHooksPath(string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        string gitMarker = Path.Combine(repoRoot, ".git");
        if (!Directory.Exists(gitMarker) && !File.Exists(gitMarker))
        {
            return null;
        }

        var probe = runtime.RunProcess("git", ["config", "--local", "--get", "core.hooksPath"], repoRoot, 10_000);
        return probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(probe.StdOut) ? probe.StdOut.Trim() : null;
    }

    private static void RestoreInitialGitHooksPathIfMutated(
        string repoRoot,
        string? initialHooksPath,
        IDoctorProvisioningRuntime runtime)
    {
        string? currentHooksPath = ReadGitHooksPath(repoRoot, runtime);
        if (string.Equals(currentHooksPath, initialHooksPath, StringComparison.Ordinal))
        {
            return;
        }

        if (initialHooksPath is null)
        {
            _ = runtime.RunProcess("git", ["config", "--local", "--unset", "core.hooksPath"], repoRoot, 10_000);
        }
        else
        {
            _ = runtime.RunProcess("git", ["config", "--local", "core.hooksPath", initialHooksPath], repoRoot, 10_000);
        }
    }

    private static string ComputeSha256Lower(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of failed binary; do not mask primary provisioning diagnostics.
        }
    }

    private static void TryDeleteDirectory(string dirPath)
    {
        try
        {
            if (Directory.Exists(dirPath))
            {
                Directory.Delete(dirPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of temporary extraction directory.
        }
    }
}

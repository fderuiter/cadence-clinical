using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Cadence.ToolingCore.Services;

public enum ProvisioningPlatform
{
    Linux,
    MacOS,
    Windows,
    Unsupported,
}

public sealed record DoctorProvisioningOptions(
    string RepoRoot,
    bool Minimal = false,
    bool LintersOnly = false);

public sealed record DoctorProvisioningStepResult(
    string Step,
    bool Succeeded,
    bool Blocking,
    string Message);

public sealed record DoctorProvisioningResult(
    bool Succeeded,
    IReadOnlyList<DoctorProvisioningStepResult> Steps);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StdOut,
    string StdErr)
{
    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StdOut))
            {
                return StdErr;
            }

            if (string.IsNullOrWhiteSpace(StdErr))
            {
                return StdOut;
            }

            return $"{StdOut}\n{StdErr}";
        }
    }
}

public interface IDoctorProvisioningRuntime
{
    ProvisioningPlatform Platform { get; }

    Architecture CpuArchitecture { get; }

    string? GetEnvironmentVariable(string name);

    string? ResolveExecutable(string toolName, string workingDirectory);

    ProcessExecutionResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs = 300_000);

    void DownloadFile(string url, string destinationPath);

    void SetPosixExecutablePermissions(string filePath);
}

public sealed class DefaultDoctorProvisioningRuntime : IDoctorProvisioningRuntime
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public ProvisioningPlatform Platform
    {
        get
        {
            if (OperatingSystem.IsLinux())
            {
                return ProvisioningPlatform.Linux;
            }

            if (OperatingSystem.IsMacOS())
            {
                return ProvisioningPlatform.MacOS;
            }

            if (OperatingSystem.IsWindows())
            {
                return ProvisioningPlatform.Windows;
            }

            return ProvisioningPlatform.Unsupported;
        }
    }

    public Architecture CpuArchitecture => RuntimeInformation.OSArchitecture;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? ResolveExecutable(string toolName, string workingDirectory) =>
        PosixProcessLauncher.ResolveExecutablePath(toolName, workingDirectory);

    public ProcessExecutionResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs = 300_000)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        ConfigureDotnetTelemetryOptOut(psi);

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return new ProcessExecutionResult(1, string.Empty, $"Failed to start '{fileName}'.");
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return new ProcessExecutionResult(124, string.Empty, $"Timed out running '{fileName}'.");
            }

            string stdOut = stdOutTask.GetAwaiter().GetResult().Trim();
            string stdErr = stdErrTask.GetAwaiter().GetResult().Trim();
            return new ProcessExecutionResult(process.ExitCode, stdOut, stdErr);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new ProcessExecutionResult(127, string.Empty, ex.Message);
        }
    }

    public void DownloadFile(string url, string destinationPath)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid download URL '{url}'.");
        }

        string? parentDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (uri.IsFile)
        {
            string localSource = uri.LocalPath;
            if (!File.Exists(localSource))
            {
                throw new FileNotFoundException($"Local download source '{localSource}' was not found.", localSource);
            }

            File.Copy(localSource, destinationPath, overwrite: true);
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported URI scheme '{uri.Scheme}' in '{url}'.");
        }

        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = SharedHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var networkStream = response.Content.ReadAsStream();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                networkStream.CopyTo(fileStream);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = ex;
                if (attempt < 3)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }

        throw new InvalidOperationException($"Failed to download '{url}' after 3 attempts: {lastError?.Message}", lastError);
    }

    public void SetPosixExecutablePermissions(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    internal static void ConfigureDotnetTelemetryOptOut(ProcessStartInfo psi)
    {
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
    }
}

/// <summary>
/// AOT-safe workspace provisioning backing <c>cadence doctor --auto-fix</c>.
/// Consolidates SHA-256 verified CLI linter downloads, <c>.cadence/venv</c> secret-scanner setup,
/// <c>dotnet tool restore</c>, <c>pnpm install --frozen-lockfile</c>, <c>.env</c> initialization,
/// and fail-closed Git pre-commit hook activation.
/// </summary>
public static class DoctorProvisioningService
{
    public const string CadenceBinDirEnvVar = "CADENCE_BIN_DIR";
    public const string ActionlintDownloadUrlEnvVar = "ACTIONLINT_DOWNLOAD_URL";
    public const string ActionlintSha256EnvVar = "ACTIONLINT_SHA256";
    public const string HadolintDownloadUrlEnvVar = "HADOLINT_DOWNLOAD_URL";
    public const string HadolintSha256EnvVar = "HADOLINT_SHA256";
    public const string ShellcheckDownloadUrlEnvVar = "SHELLCHECK_DOWNLOAD_URL";
    public const string ShellcheckSha256EnvVar = "SHELLCHECK_SHA256";

    public sealed record LinterPinDescriptor(
        string ToolName,
        string Version,
        string AssetName,
        string DefaultUrl,
        string PinnedSha256,
        string VersionFlag);

    private static readonly Dictionary<(ProvisioningPlatform Platform, Architecture Arch), (string ActionlintSha, string HadolintSha, string ShellcheckSha)> PinnedChecksums = new()
    {
        [(ProvisioningPlatform.Linux, Architecture.X64)] = (
            "8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8", // pragma: allowlist secret
            "c7187db94eeeeca956519a6af171adc31453941a1e777961f6e680f697c8c507", // pragma: allowlist secret
            "8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198"), // pragma: allowlist secret
        [(ProvisioningPlatform.Linux, Architecture.Arm64)] = (
            "325e971b6ba9bfa504672e29be93c24981eeb1c07576d730e9f7c8805afff0c6", // pragma: allowlist secret
            "f6198ef8090f404dbb771abfee086eb8c48ac177f30da7fd3510aca35b344b5d", // pragma: allowlist secret
            "12b331c1d2db6b9eb13cfca64306b1b157a86eb69db83023e261eaa7e7c14588"), // pragma: allowlist secret
        [(ProvisioningPlatform.MacOS, Architecture.X64)] = (
            "5b44c3bc2255115c9b69e30efc0fecdf498fdb63c5d58e17084fd5f16324c644", // pragma: allowlist secret
            "ffe9bb18b23d5ed1eae50237aecdbb523d016e96da0bd4e7aa432040acfc3fde", // pragma: allowlist secret
            "3c89db4edcab7cf1c27bff178882e0f6f27f7afdf54e859fa041fca10febe4c6"), // pragma: allowlist secret
        [(ProvisioningPlatform.MacOS, Architecture.Arm64)] = (
            "aba9ced2dee8d27fecca3dc7feb1a7f9a52caefa1eb46f3271ea66b6e0e6953f", // pragma: allowlist secret
            "5c09f3213f8e40406abe048233d985eebef336d4a6a20021be47fadb6cf480a2", // pragma: allowlist secret
            "56affdd8de5527894dca6dc3d7e0a99a873b0f004d7aabc30ae407d3f48b0a79"), // pragma: allowlist secret
        [(ProvisioningPlatform.Windows, Architecture.X64)] = (
            "6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9", // pragma: allowlist secret
            "01d927294962b5387f9ead4f18679158452be4f17c765ad0bdffe5264b9c7b0a", // pragma: allowlist secret
            "8a4e35ab0b331c85d73567b12f2a444df187f483e5079ceffa6bda1faa2e740e"), // pragma: allowlist secret
        [(ProvisioningPlatform.Windows, Architecture.Arm64)] = (
            "cadcf7ea4efe3a68728893813643cebe1185e5b1d4be5b96245f65c9a4d5ea41", // pragma: allowlist secret
            "01d927294962b5387f9ead4f18679158452be4f17c765ad0bdffe5264b9c7b0a", // pragma: allowlist secret
            "8a4e35ab0b331c85d73567b12f2a444df187f483e5079ceffa6bda1faa2e740e"), // pragma: allowlist secret
    };

    public static IReadOnlyDictionary<(ProvisioningPlatform Platform, Architecture Arch), (string ActionlintSha, string HadolintSha, string ShellcheckSha)> GetPinnedChecksumMatrix() =>
        PinnedChecksums;

    public static string ResolveCadenceBinDirectory(string repoRoot, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string? customBinDir = getEnvironmentVariable(CadenceBinDirEnvVar);
        return !string.IsNullOrWhiteSpace(customBinDir)
            ? customBinDir
            : Path.Combine(repoRoot, ".cadence", "bin");
    }

    public static string? ReadPinnedPnpmVersion(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        if (!document.RootElement.TryGetProperty("packageManager", out var field) || field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string value = field.GetString() ?? string.Empty;
        if (!value.StartsWith("pnpm@", StringComparison.Ordinal))
        {
            return null;
        }

        string version = value["pnpm@".Length..];
        int hash = version.IndexOf('+', StringComparison.Ordinal);
        return hash >= 0 ? version[..hash] : version;
    }

    public static IReadOnlyList<(string PackageId, string Version, string Command)> ReadLocalDotnetTools(string repoRoot)
    {
        string manifestPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var results = new List<(string PackageId, string Version, string Command)>();
        foreach (var prop in tools.EnumerateObject())
        {
            string version = prop.Value.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.String
                ? verProp.GetString() ?? string.Empty
                : string.Empty;
            string command = prop.Name;
            if (prop.Value.TryGetProperty("commands", out var cmdsProp) && cmdsProp.ValueKind == JsonValueKind.Array)
            {
                string? firstCommand = cmdsProp.EnumerateArray()
                    .FirstOrDefault(item => item.ValueKind == JsonValueKind.String)
                    .GetString();
                if (!string.IsNullOrWhiteSpace(firstCommand))
                {
                    command = firstCommand;
                }
            }

            results.Add((prop.Name, version, command));
        }

        return results;
    }

    public static DoctorProvisioningResult Provision(
        DoctorProvisioningOptions options,
        IDoctorProvisioningRuntime? runtime = null)
    {
        runtime ??= new DefaultDoctorProvisioningRuntime();
        var steps = new List<DoctorProvisioningStepResult>();

        if (options.LintersOnly)
        {
            ProvisionCliLinters(options.RepoRoot, runtime, steps);
            bool lintersOk = steps.All(s => s.Succeeded || !s.Blocking);
            return new DoctorProvisioningResult(lintersOk, steps);
        }

        string? initialHooksPath = ReadGitHooksPath(options.RepoRoot, runtime);

        if (!VerifyToolchainPrerequisites(options.RepoRoot, runtime, steps))
        {
            return new DoctorProvisioningResult(false, steps);
        }

        ProvisionLocalDotnetTools(options.RepoRoot, runtime, steps);
        ProvisionDetectSecretsVenv(options.RepoRoot, runtime, steps);
        ProvisionCliLinters(options.RepoRoot, runtime, steps);

        bool earlyBlockingFailure = steps.Any(s => !s.Succeeded && s.Blocking);
        if (!earlyBlockingFailure)
        {
            ProvisionFrontendWorkspace(options.RepoRoot, runtime, steps);
        }

        ProvisionEnvFile(options.RepoRoot, steps);

        bool anyBlockingFailure = steps.Any(s => !s.Succeeded && s.Blocking);
        if (anyBlockingFailure)
        {
            RestoreInitialGitHooksPathIfMutated(options.RepoRoot, initialHooksPath, runtime);
            return new DoctorProvisioningResult(false, steps);
        }

        if (!options.Minimal)
        {
            ProvisionOptionalPlaywrightChromium(options.RepoRoot, runtime, steps);
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "playwright",
                Succeeded: true,
                Blocking: false,
                "Skipped Playwright Chromium download (--minimal)."));
        }

        ProvisionGitHooks(options.RepoRoot, runtime, steps);
        bool succeeded = steps.All(s => s.Succeeded || !s.Blocking);
        return new DoctorProvisioningResult(succeeded, steps);
    }

    public static bool TryGetLinterDescriptors(
        ProvisioningPlatform platform,
        Architecture arch,
        out IReadOnlyList<LinterPinDescriptor> descriptors,
        out string errorMessage)
    {
        descriptors = [];
        errorMessage = string.Empty;

        if (!PinnedChecksums.TryGetValue((platform, arch), out var pins))
        {
            errorMessage = platform == ProvisioningPlatform.Unsupported
                ? "Unsupported operating system for actionlint/hadolint/shellcheck provisioning."
                : $"Unsupported CPU architecture '{arch}' on {platform} for actionlint/hadolint/shellcheck provisioning.";
            return false;
        }

        string actionlintArch = arch == Architecture.Arm64 ? "arm64" : "amd64";
        string actionlintVer = CliLinterGateService.ActionlintVersion;
        string hadolintVer = CliLinterGateService.HadolintVersion;
        string shellcheckVer = CliLinterGateService.ShellcheckVersion;

        string actionlintAsset;
        string hadolintAsset;
        string shellcheckAsset;

        switch (platform)
        {
            case ProvisioningPlatform.Linux:
                actionlintAsset = $"actionlint_{actionlintVer}_linux_{actionlintArch}.tar.gz";
                hadolintAsset = arch == Architecture.Arm64 ? "hadolint-linux-arm64" : "hadolint-linux-x86_64";
                shellcheckAsset = arch == Architecture.Arm64
                    ? $"shellcheck-v{shellcheckVer}.linux.aarch64.tar.xz"
                    : $"shellcheck-v{shellcheckVer}.linux.x86_64.tar.xz";
                break;

            case ProvisioningPlatform.MacOS:
                actionlintAsset = $"actionlint_{actionlintVer}_darwin_{actionlintArch}.tar.gz";
                hadolintAsset = arch == Architecture.Arm64 ? "hadolint-macos-arm64" : "hadolint-macos-x86_64";
                shellcheckAsset = arch == Architecture.Arm64
                    ? $"shellcheck-v{shellcheckVer}.darwin.aarch64.tar.xz"
                    : $"shellcheck-v{shellcheckVer}.darwin.x86_64.tar.xz";
                break;

            case ProvisioningPlatform.Windows:
                actionlintAsset = $"actionlint_{actionlintVer}_windows_{actionlintArch}.zip";
                hadolintAsset = "hadolint-windows-x86_64.exe";
                shellcheckAsset = $"shellcheck-v{shellcheckVer}.zip";
                break;

            default:
                errorMessage = "Unsupported operating system for CLI linter provisioning.";
                return false;
        }

        descriptors =
        [
            new LinterPinDescriptor(
                "actionlint",
                actionlintVer,
                actionlintAsset,
                $"https://github.com/rhysd/actionlint/releases/download/v{actionlintVer}/{actionlintAsset}",
                pins.ActionlintSha,
                "-version"),
            new LinterPinDescriptor(
                "hadolint",
                hadolintVer,
                hadolintAsset,
                $"https://github.com/hadolint/hadolint/releases/download/v{hadolintVer}/{hadolintAsset}",
                pins.HadolintSha,
                "--version"),
            new LinterPinDescriptor(
                "shellcheck",
                shellcheckVer,
                shellcheckAsset,
                $"https://github.com/koalaman/shellcheck/releases/download/v{shellcheckVer}/{shellcheckAsset}",
                pins.ShellcheckSha,
                "--version"),
        ];
        return true;
    }

    private static bool VerifyToolchainPrerequisites(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        bool ok = true;

        var dotnetProbe = runtime.RunProcess("dotnet", ["--version"], repoRoot, 15_000);
        if (dotnetProbe.ExitCode != 0 || string.IsNullOrWhiteSpace(dotnetProbe.StdOut))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: false,
                Blocking: true,
                ".NET 10 SDK was not found on PATH."));
            ok = false;
        }
        else if (!dotnetProbe.StdOut.StartsWith("10.", StringComparison.Ordinal))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: false,
                Blocking: true,
                $"Active .NET SDK ({dotnetProbe.StdOut}) does not satisfy the .NET 10 requirement in global.json."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: true,
                Blocking: true,
                $".NET 10 SDK {dotnetProbe.StdOut} active."));
        }

        var nodeProbe = runtime.RunProcess("node", ["--version"], repoRoot, 15_000);
        if (nodeProbe.ExitCode != 0 || string.IsNullOrWhiteSpace(nodeProbe.StdOut))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "node",
                Succeeded: false,
                Blocking: true,
                "Node.js was not found on PATH. Install Node.js 22.13+ or 24+ before running setup."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "node",
                Succeeded: true,
                Blocking: true,
                $"Node.js {nodeProbe.StdOut} active."));
        }

        string packageJsonPath = Path.Combine(repoRoot, "package.json");
        string? pinnedPnpm = File.Exists(packageJsonPath)
            ? ReadPinnedPnpmVersion(File.ReadAllText(packageJsonPath))
            : null;

        var pnpmProbe = runtime.RunProcess("pnpm", ["--version"], repoRoot, 15_000);
        string? installedPnpm = pnpmProbe.ExitCode == 0 && !string.IsNullOrWhiteSpace(pnpmProbe.StdOut)
            ? pnpmProbe.StdOut.Trim()
            : null;

        bool needsCorepack = installedPnpm is null ||
            (pinnedPnpm is not null && !string.Equals(installedPnpm, pinnedPnpm, StringComparison.Ordinal));
        if (needsCorepack && runtime.ResolveExecutable("corepack", repoRoot) is not null)
        {
            _ = runtime.RunProcess("corepack", ["enable"], repoRoot, 30_000);
            pnpmProbe = runtime.RunProcess("pnpm", ["--version"], repoRoot, 15_000);
            installedPnpm = pnpmProbe.ExitCode == 0 && !string.IsNullOrWhiteSpace(pnpmProbe.StdOut)
                ? pnpmProbe.StdOut.Trim()
                : null;
        }

        if (installedPnpm is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                "pnpm is missing or crashes. Install Corepack ('npm install --global corepack') and run 'corepack enable'."));
            ok = false;
        }
        else if (File.Exists(packageJsonPath) && pinnedPnpm is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                "package.json declares no pnpm packageManager pin."));
            ok = false;
        }
        else if (pinnedPnpm is not null && !string.Equals(installedPnpm, pinnedPnpm, StringComparison.Ordinal))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                $"pnpm {installedPnpm} does not match package.json pin {pinnedPnpm}. Run 'corepack enable'."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: true,
                Blocking: true,
                $"pnpm {installedPnpm} active."));
        }

        return ok;
    }

    private static void ProvisionLocalDotnetTools(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        string manifestPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        if (File.Exists(manifestPath))
        {
            var restoreResult = runtime.RunProcess("dotnet", ["tool", "restore"], repoRoot, 180_000);
            if (restoreResult.ExitCode != 0)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "dotnet-tools",
                    Succeeded: false,
                    Blocking: true,
                    $"dotnet tool restore failed: {restoreResult.CombinedOutput}"));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "dotnet-tools",
                Succeeded: true,
                Blocking: true,
                "Local .NET tools restored from .config/dotnet-tools.json."));
        }

        string solutionPath = Path.Combine(repoRoot, "Cadence.sln");
        if (File.Exists(solutionPath))
        {
            var slnRestore = runtime.RunProcess("dotnet", ["restore", solutionPath], repoRoot, 300_000);
            if (slnRestore.ExitCode != 0)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "dotnet-restore",
                    Succeeded: false,
                    Blocking: true,
                    $"dotnet restore Cadence.sln failed: {slnRestore.CombinedOutput}"));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "dotnet-restore",
                Succeeded: true,
                Blocking: true,
                "Restored NuGet packages for Cadence.sln."));
        }
    }

    private static void ProvisionFrontendWorkspace(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        string lockfilePath = Path.Combine(repoRoot, "pnpm-lock.yaml");
        if (!File.Exists(lockfilePath))
        {
            return;
        }

        var installResult = runtime.RunProcess("pnpm", ["install", "--frozen-lockfile"], repoRoot, 600_000);
        if (installResult.ExitCode != 0)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm-install",
                Succeeded: false,
                Blocking: true,
                $"pnpm install --frozen-lockfile failed: {installResult.CombinedOutput}"));
            return;
        }

        steps.Add(new DoctorProvisioningStepResult(
            "pnpm-install",
            Succeeded: true,
            Blocking: true,
            "pnpm workspace dependencies verified."));
    }

    private static void ProvisionDetectSecretsVenv(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        bool isWindows = runtime.Platform == ProvisioningPlatform.Windows;
        string venvDir = Path.Combine(repoRoot, ".cadence", "venv");
        string binSubdir = isWindows ? "Scripts" : "bin";
        string hookExeName = isWindows ? "detect-secrets-hook.exe" : "detect-secrets-hook";
        string pythonExeName = isWindows ? "python.exe" : "python";
        string hookPath = Path.Combine(venvDir, binSubdir, hookExeName);
        string venvPython = Path.Combine(venvDir, binSubdir, pythonExeName);
        string pinnedSpec = $"detect-secrets=={DetectSecretsGateService.PinnedVersion}";

        if (File.Exists(hookPath))
        {
            var existingProbe = runtime.RunProcess(hookPath, ["--version"], repoRoot, 15_000);
            if (existingProbe.ExitCode == 0 && existingProbe.CombinedOutput.Contains(DetectSecretsGateService.PinnedVersion, StringComparison.Ordinal))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "detect-secrets",
                    Succeeded: true,
                    Blocking: true,
                    $"detect-secrets {DetectSecretsGateService.PinnedVersion} already provisioned in .cadence/venv."));
                return;
            }
        }

        if (runtime.ResolveExecutable("uv", repoRoot) is not null)
        {
            var uvVenv = runtime.RunProcess("uv", ["venv", venvDir], repoRoot, 120_000);
            var uvInstall = uvVenv.ExitCode == 0
                ? runtime.RunProcess("uv", ["pip", "install", "--python", venvPython, "--quiet", pinnedSpec], repoRoot, 180_000)
                : uvVenv;

            if (uvInstall.ExitCode == 0 && VerifyDetectSecretsHook(hookPath, repoRoot, runtime))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "detect-secrets",
                    Succeeded: true,
                    Blocking: true,
                    $"detect-secrets {DetectSecretsGateService.PinnedVersion} provisioned in .cadence/venv via uv."));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: false,
                Blocking: true,
                $"Failed to provision {pinnedSpec} in .cadence/venv via uv: {uvInstall.CombinedOutput}"));
            return;
        }

        string? pythonCmd = isWindows
            ? runtime.ResolveExecutable("python", repoRoot) ?? runtime.ResolveExecutable("python3", repoRoot)
            : runtime.ResolveExecutable("python3", repoRoot) ?? runtime.ResolveExecutable("python", repoRoot);

        if (pythonCmd is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: false,
                Blocking: true,
                $"Neither 'uv' nor 'python3'/'python' was found on PATH to provision {pinnedSpec} in .cadence/venv."));
            return;
        }

        var venvCreate = runtime.RunProcess(pythonCmd, ["-m", "venv", venvDir], repoRoot, 120_000);
        var pipInstall = venvCreate.ExitCode == 0
            ? runtime.RunProcess(venvPython, ["-m", "pip", "install", "--quiet", "--upgrade", "pip", pinnedSpec], repoRoot, 240_000)
            : venvCreate;

        if (pipInstall.ExitCode == 0 && VerifyDetectSecretsHook(hookPath, repoRoot, runtime))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: true,
                Blocking: true,
                $"detect-secrets {DetectSecretsGateService.PinnedVersion} provisioned in .cadence/venv via python -m venv."));
            return;
        }

        steps.Add(new DoctorProvisioningStepResult(
            "detect-secrets",
            Succeeded: false,
            Blocking: true,
            $"Failed to provision {pinnedSpec} in .cadence/venv: {pipInstall.CombinedOutput}"));
    }

    private static bool VerifyDetectSecretsHook(string hookPath, string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        if (!File.Exists(hookPath))
        {
            return false;
        }

        var probe = runtime.RunProcess(hookPath, ["--version"], repoRoot, 15_000);
        return probe.ExitCode == 0 && probe.CombinedOutput.Contains(DetectSecretsGateService.PinnedVersion, StringComparison.Ordinal);
    }

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

    private static void ProvisionEnvFile(string repoRoot, List<DoctorProvisioningStepResult> steps)
    {
        string envPath = Path.Combine(repoRoot, ".env");
        string envExamplePath = Path.Combine(repoRoot, ".env.example");
        if (File.Exists(envPath))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "env-file",
                Succeeded: true,
                Blocking: false,
                ".env file is present."));
            return;
        }

        if (File.Exists(envExamplePath))
        {
            File.Copy(envExamplePath, envPath, overwrite: false);
            steps.Add(new DoctorProvisioningStepResult(
                "env-file",
                Succeeded: true,
                Blocking: false,
                "Initialized .env from .env.example."));
        }
    }

    private static void ProvisionOptionalPlaywrightChromium(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        if (!File.Exists(Path.Combine(repoRoot, "apps", "web", "package.json")))
        {
            return;
        }

        var pwResult = runtime.RunProcess("pnpm", ["--filter", "web", "exec", "playwright", "install", "chromium"], repoRoot, 600_000);
        if (pwResult.ExitCode == 0)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "playwright",
                Succeeded: true,
                Blocking: false,
                "Playwright Chromium browser installed for web E2E testing."));
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "playwright",
                Succeeded: false,
                Blocking: false,
                "Playwright browser install skipped (run 'pnpm --filter web exec playwright install chromium' later if needed)."));
        }
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

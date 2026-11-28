using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// AOT-safe workspace provisioning backing <c>cadence doctor --auto-fix</c>.
/// Consolidates SHA-256 verified CLI linter downloads, <c>.cadence/venv</c> secret-scanner setup,
/// <c>dotnet tool restore</c>, <c>pnpm install --frozen-lockfile</c>, <c>.env</c> initialization,
/// and fail-closed Git pre-commit hook activation.
/// </summary>
public static partial class DoctorProvisioningService
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
}

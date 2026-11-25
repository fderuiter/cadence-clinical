using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static partial class DoctorCommand
{
    [GeneratedRegex(@"^\s*Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""(?<path>[^""]+\.csproj)""", RegexOptions.CultureInvariant)]
    private static partial Regex SolutionCsprojEntryRegex();

    public static void Configure(Command command)
    {
        var autoFixOption = new Option<bool>("--auto-fix", "-f")
        {
            Description = "Provision pinned CLI linters, .cadence/venv, local .NET tools, pnpm workspace dependencies, and .githooks/pre-commit",
        };
        var minimalOption = new Option<bool>("--minimal")
        {
            Description = "Skip optional heavy provisioning steps (such as Playwright browser downloads) during --auto-fix",
        };
        var lintersOnlyOption = new Option<bool>("--linters-only")
        {
            Description = "Scope provisioning and diagnostics to SHA-256 verified actionlint, hadolint, and shellcheck binaries",
        };

        command.Options.Add(autoFixOption);
        command.Options.Add(minimalOption);
        command.Options.Add(lintersOnlyOption);

        command.SetAction(parseResult => Execute(
            autoFix: parseResult.GetValue(autoFixOption),
            minimal: parseResult.GetValue(minimalOption),
            lintersOnly: parseResult.GetValue(lintersOnlyOption)));
    }

    public static int Execute(
        bool autoFix = false,
        bool minimal = false,
        bool lintersOnly = false,
        string? repoRoot = null,
        IDoctorProvisioningRuntime? runtime = null)
    {
        string root = repoRoot ?? QualityGateOrchestrator.FindRepoRoot();
        runtime ??= new DefaultDoctorProvisioningRuntime();
        bool provisioningOk = true;

        if (autoFix)
        {
            AnsiConsole.MarkupLine("[cyan]=== Cadence Clinical Environment Provisioning (--auto-fix) ===[/]\n");
            var result = DoctorProvisioningService.Provision(
                new DoctorProvisioningOptions(root, Minimal: minimal, LintersOnly: lintersOnly),
                runtime);
            provisioningOk = result.Succeeded;

            foreach (var step in result.Steps)
            {
                if (step.Succeeded)
                {
                    AnsiConsole.MarkupLine($"  [green][[OK]][/] {Markup.Escape(step.Message)}");
                }
                else if (step.Blocking)
                {
                    AnsiConsole.MarkupLine($"  [red][[ERROR]][/] {Markup.Escape(step.Message)}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [yellow][[INFO]][/] {Markup.Escape(step.Message)}");
                }
            }

            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[cyan]=== Cadence Clinical System Diagnostics ===[/]\n");

        if (lintersOnly)
        {
            bool alOnlyOk = CheckActionlint(root, runtime.GetEnvironmentVariable);
            bool hlOnlyOk = CheckHadolint(root, runtime.GetEnvironmentVariable);
            bool scOnlyOk = CheckShellcheck(root, runtime.GetEnvironmentVariable);
            return provisioningOk && alOnlyOk && hlOnlyOk && scOnlyOk ? 0 : 1;
        }

        // 1. .NET SDK (read-only)
        bool dotnetOk = CheckDotnetSdk(root, runtime);

        // 2. CPM Check (read-only)
        bool cpm = File.Exists(Path.Combine(root, "Directory.Packages.props"));
        AnsiConsole.MarkupLine($"  Central Package Management: {(cpm ? "[green]Enforced[/]" : "[red]Missing[/]")}");

        // 3. Local .NET tools (.config/dotnet-tools.json, read-only)
        bool dotnetToolsOk = CheckLocalDotnetTools(root, runtime);

        // 4. NuGet restore assets (project.assets.json across obj and ArtifactsPath, read-only)
        bool dotnetRestoreOk = CheckDotnetRestoreAssets(root, runtime.GetEnvironmentVariable);

        // 5. pnpm against the packageManager pin (#5331, read-only)
        bool pnpmOk = CheckPnpm(root, runtime);

        // 6. Workspace node_modules (read-only)
        bool nodeModulesOk = CheckNodeModules(root);

        // 7. detect-secrets guardrail tool (.cadence/venv, PATH, or uvx, read-only)
        bool detectSecretsOk = CheckDetectSecrets(root, runtime.GetEnvironmentVariable);

        // 8. CLI linters (.cadence/bin or PATH, read-only)
        bool actionlintOk = CheckActionlint(root, runtime.GetEnvironmentVariable);
        bool hadolintOk = CheckHadolint(root, runtime.GetEnvironmentVariable);
        bool shellcheckOk = CheckShellcheck(root, runtime.GetEnvironmentVariable);

        // 9. Git pre-commit hook (.githooks/pre-commit, read-only)
        bool gitHooksOk = CheckGitHooks(root, runtime);

        // 10. PostgreSQL connectivity (informational, non-blocking)
        ProbePostgres();

        return provisioningOk &&
               dotnetOk &&
               cpm &&
               dotnetToolsOk &&
               dotnetRestoreOk &&
               pnpmOk &&
               nodeModulesOk &&
               detectSecretsOk &&
               actionlintOk &&
               hadolintOk &&
               shellcheckOk &&
               gitHooksOk
            ? 0
            : 1;
    }

    private static bool CheckDotnetRestoreAssets(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        string solutionPath = Path.Combine(repoRoot, "Cadence.sln");
        if (!File.Exists(solutionPath))
        {
            return true;
        }

        var csprojRelativePaths = ReadSolutionProjectPaths(solutionPath);
        if (csprojRelativePaths.Count == 0)
        {
            return true;
        }

        var candidateArtifactsPaths = ResolveCandidateArtifactsPaths(repoRoot, getEnvironmentVariable);
        var missingProjects = new List<string>();

        foreach (string rawRelPath in csprojRelativePaths)
        {
            if (!HasProjectAssetsJson(repoRoot, rawRelPath, candidateArtifactsPaths))
            {
                missingProjects.Add(rawRelPath.Replace('\\', '/'));
            }
        }

        if (missingProjects.Count == 0)
        {
            AnsiConsole.MarkupLine($"  NuGet Restore Assets (Cadence.sln): [green]Restored ({csprojRelativePaths.Count} projects)[/]");
            return true;
        }

        string sample = string.Join(", ", missingProjects.Take(3));
        if (missingProjects.Count > 3)
        {
            sample += $", +{missingProjects.Count - 3} more";
        }

        AnsiConsole.MarkupLine(
            $"  NuGet Restore Assets (Cadence.sln): [red]Missing project.assets.json for {missingProjects.Count}/{csprojRelativePaths.Count} project(s) ({Markup.Escape(sample)})[/] (run 'dotnet restore Cadence.sln' or 'pnpm cadence doctor --auto-fix')");
        return false;
    }

    private static List<string> ReadSolutionProjectPaths(string solutionPath)
    {
        var paths = new List<string>();
        foreach (string line in File.ReadLines(solutionPath))
        {
            var match = SolutionCsprojEntryRegex().Match(line);
            if (match.Success)
            {
                paths.Add(match.Groups["path"].Value);
            }
        }

        return paths;
    }

    private static bool HasProjectAssetsJson(
        string repoRoot,
        string rawRelativeCsprojPath,
        IReadOnlyList<string> candidateArtifactsPaths)
    {
        string normalizedRel = rawRelativeCsprojPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string fullCsprojPath = Path.GetFullPath(Path.Combine(repoRoot, normalizedRel));
        string projectDir = Path.GetDirectoryName(fullCsprojPath) ?? repoRoot;

        if (File.Exists(Path.Combine(projectDir, "obj", "project.assets.json")))
        {
            return true;
        }

        string projectName = Path.GetFileNameWithoutExtension(fullCsprojPath);
        return candidateArtifactsPaths.Any(artifactsDir =>
            File.Exists(Path.Combine(artifactsDir, "obj", projectName, "project.assets.json")));
    }

    private static List<string> ResolveCandidateArtifactsPaths(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        var candidates = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        AddNormalizedArtifactsPath(candidates, repoRoot, getEnvironmentVariable("ArtifactsPath"));
        if (IsMsBuildTrue(getEnvironmentVariable("UseArtifactsOutput")))
        {
            AddNormalizedArtifactsPath(candidates, repoRoot, "artifacts");
        }

        ReadDirectoryBuildPropsArtifactsPaths(repoRoot, candidates);
        return [.. candidates];
    }

    private static void ReadDirectoryBuildPropsArtifactsPaths(string repoRoot, HashSet<string> candidates)
    {
        string buildPropsPath = Path.Combine(repoRoot, "Directory.Build.props");
        if (!File.Exists(buildPropsPath))
        {
            return;
        }

        try
        {
            var document = XDocument.Load(buildPropsPath);
            foreach (var element in document.Descendants())
            {
                if (string.Equals(element.Name.LocalName, "ArtifactsPath", StringComparison.OrdinalIgnoreCase))
                {
                    AddNormalizedArtifactsPath(candidates, repoRoot, element.Value);
                }
                else if (string.Equals(element.Name.LocalName, "UseArtifactsOutput", StringComparison.OrdinalIgnoreCase) &&
                         IsMsBuildTrue(element.Value))
                {
                    AddNormalizedArtifactsPath(candidates, repoRoot, "artifacts");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // Keep read-only diagnostics resilient when Directory.Build.props cannot be parsed.
        }
    }

    private static void AddNormalizedArtifactsPath(HashSet<string> candidates, string repoRoot, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        string expanded = rawPath.Trim().Replace(
            "$(MSBuildThisFileDirectory)",
            repoRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        if (expanded.Contains("$(", StringComparison.Ordinal))
        {
            return;
        }

        string normalized = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(repoRoot, expanded));
        candidates.Add(normalized);
    }

    private static bool IsMsBuildTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    private static bool CheckNodeModules(string repoRoot)
    {
        if (!File.Exists(Path.Combine(repoRoot, "package.json")))
        {
            return true;
        }

        if (Directory.Exists(Path.Combine(repoRoot, "node_modules")))
        {
            AnsiConsole.MarkupLine("  Workspace node_modules: [green]Present[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            "  Workspace node_modules: [red]Missing[/] (run 'pnpm install --frozen-lockfile' or 'pnpm cadence doctor --auto-fix')");
        return false;
    }

    private static bool CheckDotnetSdk(string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        var probe = runtime.RunProcess("dotnet", ["--version"], repoRoot, 15_000);
        string output = probe.ExitCode == 0 ? probe.StdOut.Trim() : string.Empty;
        if (output.StartsWith("10.", StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"  .NET 10 SDK: [green]Active ({Markup.Escape(output)})[/]");
            return true;
        }

        if (!string.IsNullOrEmpty(output))
        {
            AnsiConsole.MarkupLine($"  .NET SDK: [red]Incompatible ({Markup.Escape(output)}) - .NET 10 SDK required by global.json[/]");
            return false;
        }

        AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        return false;
    }

    private static bool CheckLocalDotnetTools(string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        var tools = DoctorProvisioningService.ReadLocalDotnetTools(repoRoot);
        if (tools.Count == 0)
        {
            return true;
        }

        bool allOk = true;
        foreach (var (packageId, expectedVersion, command) in tools)
        {
            var probe = runtime.RunProcess("dotnet", ["tool", "run", command, "--version"], repoRoot, 15_000);
            if (probe.ExitCode == 0 &&
                (string.IsNullOrEmpty(expectedVersion) || probe.CombinedOutput.Contains(expectedVersion, StringComparison.Ordinal)))
            {
                AnsiConsole.MarkupLine($"  Local .NET Tool ({Markup.Escape(packageId)}): [green]{Markup.Escape(expectedVersion)} restored[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"  Local .NET Tool ({Markup.Escape(packageId)}): [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' or 'dotnet tool restore')");
                allOk = false;
            }
        }

        return allOk;
    }

    private static bool CheckGitHooks(string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        string gitMarker = Path.Combine(repoRoot, ".git");
        string preCommitPath = Path.Combine(repoRoot, ".githooks", "pre-commit");
        if ((!Directory.Exists(gitMarker) && !File.Exists(gitMarker)) || !File.Exists(preCommitPath))
        {
            return true;
        }

        var probe = runtime.RunProcess("git", ["config", "--get", "core.hooksPath"], repoRoot, 10_000);
        string configured = probe.ExitCode == 0 ? probe.StdOut.Trim() : string.Empty;
        if (string.Equals(configured, ".githooks", StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("  Git Pre-Commit Hooks: [green]Active (.githooks/pre-commit)[/]");
            return true;
        }

        AnsiConsole.MarkupLine("  Git Pre-Commit Hooks: [red]Inactive[/] (run 'pnpm cadence doctor --auto-fix')");
        return false;
    }

    private static bool CheckDetectSecrets(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        var tool = DetectSecretsGateService.ResolveTool(repoRoot, getEnvironmentVariable);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  detect-secrets: [green]{Markup.Escape(tool.Version)} ({Markup.Escape(tool.Source)})[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            $"  detect-secrets: [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' to provision detect-secrets=={DetectSecretsGateService.PinnedVersion} in .cadence/venv)");
        return false;
    }

    private static bool CheckActionlint(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        var tool = CliLinterGateService.ResolveActionlint(repoRoot, getEnvironmentVariable);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  actionlint: [green]{Markup.Escape(tool.VersionOutput)} ({Markup.Escape(tool.SourceLabel)})[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            $"  actionlint: [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' to provision actionlint v{CliLinterGateService.ActionlintVersion} in .cadence/bin)");
        return false;
    }

    private static bool CheckHadolint(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        var tool = CliLinterGateService.ResolveHadolint(repoRoot, getEnvironmentVariable);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  hadolint: [green]{Markup.Escape(tool.VersionOutput)} ({Markup.Escape(tool.SourceLabel)})[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            $"  hadolint: [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' to provision hadolint v{CliLinterGateService.HadolintVersion} in .cadence/bin)");
        return false;
    }

    private static bool CheckShellcheck(string repoRoot, Func<string, string?> getEnvironmentVariable)
    {
        var tool = CliLinterGateService.ResolveShellcheck(repoRoot, getEnvironmentVariable);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  shellcheck: [green]{Markup.Escape(tool.VersionOutput)} ({Markup.Escape(tool.SourceLabel)})[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            $"  shellcheck: [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' to provision shellcheck v{CliLinterGateService.ShellcheckVersion} in .cadence/bin)");
        return false;
    }

    private static bool CheckPnpm(string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        string packageJsonPath = Path.Combine(repoRoot, "package.json");
        string? pinned = File.Exists(packageJsonPath) ? PnpmPin.PinnedVersion(File.ReadAllText(packageJsonPath)) : null;
        var probe = runtime.RunProcess("pnpm", ["--version"], repoRoot, 15_000);
        string? installed = probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(probe.StdOut)
            ? probe.StdOut.Trim()
            : null;

        switch (PnpmPin.Compare(pinned, installed))
        {
            case PnpmPinStatus.Matches:
                AnsiConsole.MarkupLine($"  pnpm: [green]{Markup.Escape(installed!)} (matches packageManager)[/]");
                return true;
            case PnpmPinStatus.Mismatch:
                AnsiConsole.MarkupLine($"  pnpm: [red]{Markup.Escape(installed!)}, but package.json pins {Markup.Escape(pinned!)}[/] (run 'corepack enable' so pnpm follows the pin)");
                return false;
            case PnpmPinStatus.Unusable:
                AnsiConsole.MarkupLine($"  pnpm: [red]missing or crashes[/] (package.json pins {Markup.Escape(pinned ?? "unknown")}; run 'corepack enable')");
                return false;
            default:
                AnsiConsole.MarkupLine("  pnpm: [red]package.json declares no packageManager pin[/]");
                return false;
        }
    }

    private static void ProbePostgres()
    {
        try
        {
            using var client = new TcpClient();
            var asyncResult = client.BeginConnect("127.0.0.1", 5432, null, null);
            bool success = asyncResult.AsyncWaitHandle.WaitOne(500, false);
            if (success && client.Connected)
            {
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [green]Connected[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable (start via docker compose -f docker/docker-compose.yml up -d postgres)[/]");
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable[/]");
        }
    }
}

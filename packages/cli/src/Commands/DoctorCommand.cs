using System;
using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DoctorCommand
{
    public static void Configure(Command command)
    {
        var autoFixOption = new Option<bool>("--auto-fix", "-f")
        {
            Description = "Provision pinned CLI linters, .cadence/venv, local .NET tools, packages/ui/dist, and .githooks/pre-commit",
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

        // 4. pnpm against the packageManager pin (#5331, read-only)
        bool pnpmOk = CheckPnpm(root, runtime);

        // 5. Shared UI bundle (packages/ui/dist, read-only)
        bool uiDistOk = CheckUiBundle(root);

        // 6. detect-secrets guardrail tool (.cadence/venv, PATH, or uvx, read-only)
        bool detectSecretsOk = CheckDetectSecrets(root, runtime.GetEnvironmentVariable);

        // 7. CLI linters (.cadence/bin or PATH, read-only)
        bool actionlintOk = CheckActionlint(root, runtime.GetEnvironmentVariable);
        bool hadolintOk = CheckHadolint(root, runtime.GetEnvironmentVariable);
        bool shellcheckOk = CheckShellcheck(root, runtime.GetEnvironmentVariable);

        // 8. Git pre-commit hook (.githooks/pre-commit, read-only)
        bool gitHooksOk = CheckGitHooks(root, runtime);

        // 9. PostgreSQL connectivity (informational, non-blocking)
        ProbePostgres();

        return provisioningOk &&
               dotnetOk &&
               cpm &&
               dotnetToolsOk &&
               pnpmOk &&
               uiDistOk &&
               detectSecretsOk &&
               actionlintOk &&
               hadolintOk &&
               shellcheckOk &&
               gitHooksOk
            ? 0
            : 1;
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

    private static bool CheckUiBundle(string repoRoot)
    {
        string uiPackageJson = Path.Combine(repoRoot, "packages", "ui", "package.json");
        if (!File.Exists(uiPackageJson))
        {
            return true;
        }

        string distJs = Path.Combine(repoRoot, "packages", "ui", "dist", "index.js");
        string distCss = Path.Combine(repoRoot, "packages", "ui", "dist", "index.css");
        if (File.Exists(distJs) && File.Exists(distCss))
        {
            AnsiConsole.MarkupLine("  packages/ui/dist: [green]Ready (index.js & index.css)[/]");
            return true;
        }

        AnsiConsole.MarkupLine("  packages/ui/dist: [red]Missing[/] (run 'pnpm cadence doctor --auto-fix' or 'pnpm --filter ui build')");
        return false;
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

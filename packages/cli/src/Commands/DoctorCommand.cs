using System;
using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DoctorCommand
{
    public static void Configure(Command command)
    {
        var autoFix = new Option<bool>("--auto-fix", "-f")
        {
            Description = "Automatically remediate missing local tools and recoverable environment issues",
        };
        command.Options.Add(autoFix);
        command.SetAction(result => Execute(result.GetValue(autoFix)));
    }

    internal static int Execute(bool autoFix = false)
    {
        AnsiConsole.MarkupLine("[cyan]=== Cadence Clinical System Diagnostics ===[/]\n");

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();

        // 1. .NET SDK
        bool dotnetOk = false;
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd().Trim() ?? "";
            p?.WaitForExit();
            if (output.StartsWith("10.", StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"  .NET 10 SDK: [green]Active ({Markup.Escape(output)})[/]");
                dotnetOk = true;
            }
            else if (!string.IsNullOrEmpty(output))
            {
                AnsiConsole.MarkupLine($"  .NET SDK: [yellow]Active ({Markup.Escape(output)}) - .NET 10 preview recommended[/]");
                dotnetOk = true;
            }
            else
            {
                AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
            }
        }
        catch
        {
            AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        }

        // 2. CPM Check
        bool cpm = File.Exists(Path.Combine(repoRoot, "Directory.Packages.props"));
        AnsiConsole.MarkupLine($"  Central Package Management: {(cpm ? "[green]Enforced[/]" : "[red]Missing[/]")}");

        // 3. Local dotnet-ef against .config/dotnet-tools.json
        bool dotnetEfOk = CheckDotnetEf(repoRoot, autoFix);

        // 4. pnpm against the packageManager pin (#5331)
        bool pnpmOk = CheckPnpm(repoRoot);

        // 5. detect-secrets guardrail tool (.cadence/venv, PATH, or uvx)
        bool detectSecretsOk = CheckDetectSecrets(repoRoot);

        // 6. CLI linters (.cadence/bin or PATH)
        bool actionlintOk = CheckActionlint(repoRoot);
        bool hadolintOk = CheckHadolint(repoRoot);

        // 7. PostgreSQL connectivity (informational; local tasks may run offline)
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
        catch
        {
            AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable[/]");
        }

        return dotnetOk && cpm && dotnetEfOk && pnpmOk && detectSecretsOk && actionlintOk && hadolintOk ? 0 : 1;
    }

    internal static bool CheckDotnetEf(
        string repoRoot,
        bool autoFix,
        Func<string, string?>? probeVersion = null,
        Func<string, bool>? restoreTools = null)
    {
        string? pinned = ReadPinnedDotnetEfVersion(repoRoot);
        if (string.IsNullOrWhiteSpace(pinned))
        {
            AnsiConsole.MarkupLine("  dotnet-ef: [red].config/dotnet-tools.json declares no dotnet-ef pin[/]");
            return false;
        }

        var probe = probeVersion ?? TryGetRestoredDotnetEfVersion;
        var restore = restoreTools ?? TryRestoreLocalTools;

        string? installed = probe(repoRoot);
        if (!string.Equals(installed, pinned, StringComparison.Ordinal) && autoFix)
        {
            AnsiConsole.MarkupLine("  dotnet-ef: [yellow]Restoring local tools from .config/dotnet-tools.json...[/]");
            if (restore(repoRoot))
            {
                installed = probe(repoRoot);
            }
        }

        if (string.Equals(installed, pinned, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"  dotnet-ef: [green]{Markup.Escape(pinned)} (matches .config/dotnet-tools.json)[/]");
            return true;
        }

        string status = string.IsNullOrWhiteSpace(installed) ? "Missing" : installed;
        AnsiConsole.MarkupLine(
            $"  dotnet-ef: [red]{Markup.Escape(status)}[/] (.config/dotnet-tools.json pins {Markup.Escape(pinned)}; run 'dotnet tool restore' or 'pnpm cadence doctor --auto-fix')");
        return false;
    }

    internal static string? ReadPinnedDotnetEfVersion(string repoRoot)
    {
        string manifestPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("tools", out var tools) &&
                tools.TryGetProperty("dotnet-ef", out var ef) &&
                ef.TryGetProperty("version", out var version))
            {
                return version.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryGetRestoredDotnetEfVersion(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("tool");
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("dotnet-ef");
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            return stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryRestoreLocalTools(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("tool");
            psi.ArgumentList.Add("restore");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckDetectSecrets(string repoRoot)
    {
        var tool = DetectSecretsGateService.ResolveTool(repoRoot);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  detect-secrets: [green]{Markup.Escape(tool.Version)} ({Markup.Escape(tool.Source)})[/]");
            return true;
        }

        AnsiConsole.MarkupLine(
            $"  detect-secrets: [red]Missing[/] (run './scripts/setup_wizard.sh --minimal' or 'python3 -m venv .cadence/venv && .cadence/venv/bin/python -m pip install --upgrade pip detect-secrets=={DetectSecretsGateService.PinnedVersion}')");
        return false;
    }

    private static bool CheckActionlint(string repoRoot)
    {
        var tool = CliLinterGateService.ResolveActionlint(repoRoot);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  actionlint: [green]{Markup.Escape(tool.VersionOutput)} ({Markup.Escape(tool.SourceLabel)})[/]");
            return true;
        }

        string wizardHint = OperatingSystem.IsWindows()
            ? "pwsh scripts/setup_wizard.ps1 -NonInteractive -Minimal"
            : "./scripts/setup_wizard.sh --minimal";
        AnsiConsole.MarkupLine(
            $"  actionlint: [red]Missing[/] (run '{wizardHint}' to provision actionlint v{CliLinterGateService.ActionlintVersion} in .cadence/bin)");
        return false;
    }

    private static bool CheckHadolint(string repoRoot)
    {
        var tool = CliLinterGateService.ResolveHadolint(repoRoot);
        if (tool is not null)
        {
            AnsiConsole.MarkupLine($"  hadolint: [green]{Markup.Escape(tool.VersionOutput)} ({Markup.Escape(tool.SourceLabel)})[/]");
            return true;
        }

        string wizardHint = OperatingSystem.IsWindows()
            ? "pwsh scripts/setup_wizard.ps1 -NonInteractive -Minimal"
            : "./scripts/setup_wizard.sh --minimal";
        AnsiConsole.MarkupLine(
            $"  hadolint: [red]Missing[/] (run '{wizardHint}' to provision hadolint v{CliLinterGateService.HadolintVersion} in .cadence/bin)");
        return false;
    }

    private static bool CheckPnpm(string repoRoot)
    {
        string packageJsonPath = Path.Combine(repoRoot, "package.json");
        string? pinned = File.Exists(packageJsonPath) ? PnpmPin.PinnedVersion(File.ReadAllText(packageJsonPath)) : null;
        string? installed = null;
        try
        {
            var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--version");
            using var p = PosixProcessLauncher.Start(psi);
            if (p is not null)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                installed = p.ExitCode == 0 ? output : null;
            }
        }
        catch (Win32Exception)
        {
            // pnpm is not on the PATH; reported as unusable below.
        }

        switch (PnpmPin.Compare(pinned, installed))
        {
            case PnpmPinStatus.Matches:
                AnsiConsole.MarkupLine($"  pnpm: [green]{installed} (matches packageManager)[/]");
                return true;
            case PnpmPinStatus.Mismatch:
                AnsiConsole.MarkupLine($"  pnpm: [red]{installed}, but package.json pins {pinned}[/] (run 'corepack enable' so pnpm follows the pin)");
                return false;
            case PnpmPinStatus.Unusable:
                AnsiConsole.MarkupLine($"  pnpm: [red]missing or crashes[/] (package.json pins {pinned}; run 'corepack enable')");
                return false;
            default:
                AnsiConsole.MarkupLine("  pnpm: [red]package.json declares no packageManager pin[/]");
                return false;
        }
    }
}

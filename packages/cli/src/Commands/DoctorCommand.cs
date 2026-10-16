using System;
using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DoctorCommand
{
    public static void Configure(Command command)
    {
        // Accepted for compatibility with `make doctor`; the diagnostics do not remediate anything yet (docs/cli.md).
        command.Options.Add(new Option<bool>("--auto-fix", "-f")
        {
            Description = "Automatically remediate missing databases and recoverable environment issues",
        });
        command.SetAction(_ => Execute());
    }

    private static int Execute()
    {
        AnsiConsole.MarkupLine("[cyan]=== Cadence Clinical System Diagnostics ===[/]\n");

        // 1. .NET SDK
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd().Trim() ?? "";
            p?.WaitForExit();
            if (output.StartsWith("10."))
                AnsiConsole.MarkupLine($"  .NET 10 SDK: [green]Active ({output})[/]");
            else if (!string.IsNullOrEmpty(output))
                AnsiConsole.MarkupLine($"  .NET SDK: [yellow]Active ({output}) - .NET 10 preview recommended[/]");
            else
                AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        }

        // 2. CPM Check
        bool cpm = File.Exists("Directory.Packages.props");
        AnsiConsole.MarkupLine($"  Central Package Management: {(cpm ? "[green]Enforced[/]" : "[red]Missing[/]")}");

        // 3. pnpm against the packageManager pin (#5331)
        bool pnpmOk = CheckPnpm();

        // 4. PostgreSQL connectivity
        try
        {
            using var client = new TcpClient();
            var asyncResult = client.BeginConnect("127.0.0.1", 5432, null, null);
            bool success = asyncResult.AsyncWaitHandle.WaitOne(500, false);
            if (success && client.Connected)
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [green]Connected[/]");
            else
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable (start via docker compose -f docker/docker-compose.yml up -d postgres)[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable[/]");
        }

        return pnpmOk ? 0 : 1;
    }

    private static bool CheckPnpm()
    {
        string? pinned = File.Exists("package.json") ? PnpmPin.PinnedVersion(File.ReadAllText("package.json")) : null;
        string? installed = null;
        try
        {
            var psi = new ProcessStartInfo("pnpm", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
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

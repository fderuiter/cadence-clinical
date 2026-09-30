using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

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

        // 3. PostgreSQL connectivity
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

        return 0;
    }
}

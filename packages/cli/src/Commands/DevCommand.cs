using System.Diagnostics;

using Cadence.Cli.Services;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class DevCommand : Command
{
    private const string AppHostProject = "apps/apphost/src/Cadence.AppHost.csproj";

    public override int Execute(CommandContext context)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        if (!File.Exists(Path.Combine(repoRoot, AppHostProject)))
        {
            AnsiConsole.MarkupLine($"[red]Cannot find {AppHostProject} under {Markup.Escape(repoRoot)}. Run 'cadence dev' from the repository.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold cyan]Starting the .NET Aspire AppHost (services, datastores and dashboard)...[/]");
        var psi = new ProcessStartInfo("dotnet", $"run --project {AppHostProject}")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to start 'dotnet run' for the AppHost.[/]");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
}

using System.Diagnostics;

using Cadence.Cli.Services;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class FixCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Self-Healing Pipeline...[/]\n");

        AnsiConsole.MarkupLine("  1. Auto-formatting C# Codebase...");
        var psi = new ProcessStartInfo("dotnet", "format Cadence.sln") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();

        AnsiConsole.MarkupLine("\n  2. Validating ADRs...");
        AdrValidatorService.ValidateAdrs(checkGitDiff: false);

        AnsiConsole.MarkupLine("\n  3. Synchronizing OpenAPI Specifications & Documentation...");
        var psiSpec = new ProcessStartInfo("uv", "run python scripts/sync_openapi_spec.py") { UseShellExecute = false };
        using var pSpec = Process.Start(psiSpec);
        pSpec?.WaitForExit();

        var psiDocs = new ProcessStartInfo("uv", "run python scripts/generate_api_docs.py") { UseShellExecute = false };
        using var pDocs = Process.Start(psiDocs);
        pDocs?.WaitForExit();

        AnsiConsole.MarkupLine("\n[bold green]Self-Healing Pipeline Complete.[/]");
        return 0;
    }
}

using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;
using Cadence.ToolingCore.OpenApi;

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
        string openApiDir = Path.Combine(QualityGateOrchestrator.FindRepoRoot(), "docs", "openapi");
        OpenApiSpecExporter.ExportOpenApiSpecsAsync(openApiDir).GetAwaiter().GetResult();

        AnsiConsole.MarkupLine("\n[bold green]Self-Healing Pipeline Complete.[/]");
        return 0;
    }
}


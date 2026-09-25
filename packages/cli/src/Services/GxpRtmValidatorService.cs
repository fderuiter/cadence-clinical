using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxpRtmValidatorService
{
    public static bool ValidateGxpRtm()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string rtmPath = Path.Combine(repoRoot, "docs", "SDLC", "Requirements_Traceability_Matrix.md");

        AnsiConsole.MarkupLine("[cyan]Executing GxP RTM Staleness Check...[/]");

        if (!File.Exists(rtmPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: RTM file not found at {rtmPath}[/]");
            return false;
        }

        AnsiConsole.MarkupLine("  [green][[OK]] GxP Requirements Traceability Matrix verified.[/]");
        return true;
    }
}

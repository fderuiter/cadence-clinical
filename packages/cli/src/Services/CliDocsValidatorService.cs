using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class CliDocsValidatorService
{
    public static bool ValidateCliDocs()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string cliDocPath = Path.Combine(repoRoot, "docs", "cli.md");

        AnsiConsole.MarkupLine("[cyan]Validating CLI Documentation Parity...[/]");

        if (!File.Exists(cliDocPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: CLI documentation not found at {cliDocPath}[/]");
            return false;
        }

        string content = File.ReadAllText(cliDocPath);
        string[] requiredSections = new[]
        {
            "# Cadence Unified Developer CLI Reference",
            "cadence doctor",
            "cadence check",
            "cadence fix",
            "cadence setup",
            "cadence gxp"
        };

        bool passed = true;
        foreach (var sec in requiredSections)
        {
            if (!content.Contains(sec, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[red]Error: CLI docs missing required section or command reference: '{sec}'[/]");
                passed = false;
            }
        }

        if (passed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] CLI documentation parity verified.[/]");
        }

        return passed;
    }
}

using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class PathPatternsValidatorService
{
    public static bool ValidatePathPatterns(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Executing Path-Pattern Boundary Linter...[/]");

        string[] checkFiles = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(repoRoot, "*.*", SearchOption.TopDirectoryOnly);

        bool valid = true;

        foreach (var file in checkFiles)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.EndsWith(".vue") && !rel.StartsWith("apps/") && !rel.StartsWith("packages/"))
            {
                AnsiConsole.MarkupLine($"[red]Error: Vue file outside frontend boundary: {rel}[/]");
                valid = false;
            }
            if (rel.EndsWith(".sh") && !rel.StartsWith("scripts/"))
            {
                AnsiConsole.MarkupLine($"[red]Error: Shell script outside scripts/ boundary: {rel}[/]");
                valid = false;
            }
        }

        if (valid)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Path pattern boundaries compliant.[/]");
        }

        return valid;
    }
}

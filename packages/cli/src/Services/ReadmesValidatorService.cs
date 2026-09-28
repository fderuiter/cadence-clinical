using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class ReadmesValidatorService
{
    public static bool ValidateReadmes()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Validating README Documentation Coverage & Headers...[/]");

        string[] targets = new[] { Path.Combine(repoRoot, "apps"), Path.Combine(repoRoot, "packages") };
        bool allPassed = true;
        int checkedCount = 0;

        foreach (var parent in targets)
        {
            if (!Directory.Exists(parent)) continue;

            foreach (var subDir in Directory.GetDirectories(parent))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.') || dirName == "__pycache__" || dirName == "bin" || dirName == "obj" || dirName == "node_modules")
                {
                    continue;
                }

                checkedCount++;
                string readmePath = Path.Combine(subDir, "README.md");
                if (!File.Exists(readmePath))
                {
                    AnsiConsole.MarkupLine($"[red]Error: Missing README.md in {Path.GetRelativePath(repoRoot, subDir)}[/]");
                    allPassed = false;
                }
                else
                {
                    string content = File.ReadAllText(readmePath);
                    if (string.IsNullOrWhiteSpace(content) || !content.Contains("# "))
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning: README.md in {Path.GetRelativePath(repoRoot, subDir)} is empty or lacks title header[/]");
                    }
                }
            }
        }

        if (allPassed)
        {
            AnsiConsole.MarkupLine($"  [green][[OK]] README coverage verified across {checkedCount} packages/apps.[/]");
        }

        return allPassed;
    }
}

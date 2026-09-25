using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class QueryStyleLinterService
{
    public static bool LintQueryStyle(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Executing Query Style Linter...[/]");

        string[] files = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories);

        bool passed = true;

        foreach (var file in files)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("tests/") || rel.Contains("Services/QueryStyleLinterService.cs")) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string text = File.ReadAllText(file);
                if (text.Contains("FromSql" + "Raw($") || text.Contains("ExecuteSql" + "Raw($"))
                {
                    AnsiConsole.MarkupLine($"[red]Query Style Violation: Unparameterized raw SQL interpolation in '{rel}'[/]");
                    passed = false;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning reading '{file}': {ex.Message}[/]");
            }
        }

        if (passed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Query style linter passed.[/]");
        }

        return passed;
    }
}

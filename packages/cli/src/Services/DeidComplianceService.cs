using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class DeidComplianceService
{
    private static readonly Regex SsnPattern = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);

    public static bool ScanDeidCompliance(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Executing DEID Compliance Scan...[/]");

        var files = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories);

        bool passed = true;

        foreach (var file in files)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/node_modules/") || rel.Contains("tests/")) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string text = File.ReadAllText(file);
                if (SsnPattern.IsMatch(text))
                {
                    AnsiConsole.MarkupLine($"[red]DEID Violation: Raw SSN pattern detected in '{rel}'[/]");
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
            AnsiConsole.MarkupLine("  [green][[OK]] DEID compliance scan passed.[/]");
        }

        return passed;
    }
}

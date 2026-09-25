using System;
using System.IO;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class ComplianceSanitizerService
{
    private static readonly Regex ReqRegex = new(@"@req:(?:prd-|PRD-)?([a-zA-Z0-9]+)-([a-zA-Z0-9]+)", RegexOptions.Compiled);

    public static bool SanitizeComplianceTags(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");

        AnsiConsole.MarkupLine("[cyan]Sanitizing Requirement Tag Prefixes in Documentation...[/]");

        if (!Directory.Exists(docsDir)) return true;

        var files = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);

        int updatedCount = 0;
        foreach (var file in files)
        {
            if (file.Contains("docs/SDLC/runs/")) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string content = File.ReadAllText(file);
                string sanitized = ReqRegex.Replace(content, m => $"@req:PRD-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}");

                if (sanitized != content)
                {
                    File.WriteAllText(file, sanitized);
                    updatedCount++;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning processing {file}: {ex.Message}[/]");
            }
        }

        AnsiConsole.MarkupLine($"  [green][[OK]] Requirement tag sanitizer verified ({updatedCount} files updated).[/]");
        return true;
    }
}

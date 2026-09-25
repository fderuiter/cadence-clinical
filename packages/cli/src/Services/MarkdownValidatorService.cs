using System;
using System.IO;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class MarkdownValidatorService
{
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static bool ValidateMarkdown(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");

        AnsiConsole.MarkupLine("[cyan]Validating Markdown Files and Workspace Links...[/]");

        if (!Directory.Exists(docsDir)) return true;

        var files = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);

        bool allValid = true;

        foreach (var file in files)
        {
            if (file.Contains("node_modules") || file.Contains(".venv")) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string content = File.ReadAllText(file);
                var matches = MarkdownLinkRegex.Matches(content);
                foreach (Match m in matches)
                {
                    string target = m.Groups[2].Value.Trim();
                    if (target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal) || target.StartsWith('#') || target.StartsWith("mailto:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (target.Contains('#'))
                    {
                        target = target.Split('#')[0];
                    }

                    if (string.IsNullOrEmpty(target)) continue;

                    string fileDir = Path.GetDirectoryName(file) ?? repoRoot;
                    string targetPath = Path.GetFullPath(Path.Combine(fileDir, target));

                    if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                    {
                        AnsiConsole.MarkupLine($"[yellow]Broken Markdown link in '{Path.GetRelativePath(repoRoot, file)}': '{target}'[/]");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading '{file}': {ex.Message}[/]");
                allValid = false;
            }
        }

        if (allValid)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Markdown workspace link validation passed.[/]");
        }

        return allValid;
    }
}

using System;
using System.IO;

using Cadence.ToolingCore.Markdown;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class MarkdownValidatorService
{
    public static bool ValidateMarkdown(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");

        AnsiConsole.MarkupLine("[cyan]Validating Markdown Files and Workspace Links...[/]");

        if (!Directory.Exists(docsDir)) return true;

        var files = targetFiles != null && targetFiles.Length > 0
            ? targetFiles
            : Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);

        var issues = MarkdownParser.ValidateWorkspaceLinks(repoRoot, files);

        foreach (var issue in issues)
        {
            AnsiConsole.MarkupLine($"[yellow]{issue.Message}[/]");
        }

        bool allValid = issues.Count == 0;

        if (allValid)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Markdown workspace link validation passed.[/]");
        }

        return allValid;
    }
}


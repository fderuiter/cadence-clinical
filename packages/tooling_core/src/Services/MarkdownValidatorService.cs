using System;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Markdown;

namespace Cadence.ToolingCore.Services;

public static class MarkdownValidatorService
{
    public static int ValidateMarkdown(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating Markdown documentation formatting and workspace links...");

        string docsDir = Path.Combine(repoRoot, "docs");

        string[] excludedDirs = [
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.system_generated{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}"
        ];

        string[] mdFiles;
        if (files != null && files.Length > 0)
        {
            mdFiles = files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToArray();
        }
        else if (Directory.Exists(docsDir))
        {
            mdFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);
        }
        else
        {
            mdFiles = Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
                .Where(f => !excludedDirs.Any(d => f.Contains(d)))
                .ToArray();
        }

        int issuesCount = 0;

        // Rule 1: Workspace Link Validation via MarkdownParser
        var linkIssues = MarkdownParser.ValidateWorkspaceLinks(repoRoot, mdFiles);
        foreach (var issue in linkIssues)
        {
            Console.WriteLine($"[MARKDOWN LINK] {issue.Message}");
            issuesCount++;
        }

        // Rule 2: Paths into .NET test project folders must exist
        foreach (var issue in MarkdownParser.ValidateTestProjectPaths(repoRoot, mdFiles))
        {
            Console.WriteLine($"[MARKDOWN TEST PATH] {issue.Message}");
            issuesCount++;
        }

        // Rule 3: Heading space validation
        foreach (var file in mdFiles)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }

            bool inFencedBlock = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inFencedBlock = !inFencedBlock;
                    continue;
                }

                if (inFencedBlock)
                {
                    continue;
                }

                if (lines[i].StartsWith('#') && !lines[i].Contains(' '))
                {
                    string relPath = Path.GetRelativePath(repoRoot, file);
                    Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Heading missing space after '#': '{lines[i]}'");
                    issuesCount++;
                }
            }
        }

        if (issuesCount > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {issuesCount} markdown issues.");
            return 1;
        }

        Console.WriteLine("✔ Markdown files and workspace links validated cleanly.");
        return 0;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}

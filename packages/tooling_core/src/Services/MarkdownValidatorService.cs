using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class MarkdownValidatorService
{
    public static int ValidateMarkdown(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating Markdown documentation formatting...");

        var mdFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists)
            : Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"));

        int issues = 0;
        foreach (var file in mdFiles)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith('#') && !lines[i].Contains(' '))
                {
                    string relPath = Path.GetRelativePath(repoRoot, file);
                    Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Heading missing space after '#': '{lines[i]}'");
                    issues++;
                }
            }
        }

        if (issues > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {issues} markdown formatting issues.");
            return 1;
        }

        Console.WriteLine("✔ Markdown files validated cleanly.");
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

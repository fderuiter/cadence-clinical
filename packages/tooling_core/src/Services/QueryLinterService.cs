using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class QueryLinterService
{
    public static int LintQueries(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Linting database and ORM query statements...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ Query style linting passed cleanly.");
            return 0;
        }

        var rawCandidates = files is not null
            // The pre-commit gate passes every staged file; only C# source is linted, so a Markdown table that
            // documents these rules is not reported as a violation of them.
            ? files.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.IsPathRooted(f) ? Path.GetFullPath(f) : Path.GetFullPath(Path.Combine(repoRoot, f)))
                .Where(File.Exists)
            : Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories);

        var csFiles = rawCandidates
            .Where(f => !IsExcludedCSharpPath(Path.GetRelativePath(repoRoot, f)))
            .ToList();

        int issues = 0;
        foreach (var file in csFiles)
        {
            string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relPath.Contains("Services/QueryLinterService.cs")) continue;

            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith('*') || trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (line.Contains("FromSql" + "Raw($") || line.Contains("ExecuteSql" + "Raw($"))
                {
                    Console.WriteLine($"[QUERY STYLE VIOLATION] {relPath}:{i + 1} - Unparameterized raw SQL interpolation: '{line.Trim()}'");
                    issues++;
                }

                if (line.Contains("== true") || line.Contains("== false"))
                {
                    Console.WriteLine($"[QUERY STYLE] {relPath}:{i + 1} - Avoid explicit '== true' or '== false' in boolean comparisons: '{line.Trim()}'");
                    issues++;
                }
            }
        }

        if (issues > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {issues} query style issues.");
            return 1;
        }

        Console.WriteLine("✔ Query style linting passed cleanly.");
        return 0;
    }

    private static bool IsExcludedCSharpPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}

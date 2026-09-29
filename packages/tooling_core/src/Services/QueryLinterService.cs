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

        var csFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToList()
            : Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
                .ToList();

        int issues = 0;
        foreach (var file in csFiles)
        {
            string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relPath.Contains("Services/QueryLinterService.cs") || relPath.Contains("Services/QueryStyleLinterService.cs")) continue;

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

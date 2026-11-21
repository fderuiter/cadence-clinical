using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class QueryLinterService
{
    public static int LintQueries(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Linting database and ORM query statements...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ Query style linting passed cleanly (no target files).");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            System.Collections.Generic.List<string> csRelativeFiles;
            if (files is not null)
            {
                // The pre-commit gate passes every staged file; only C# source is linted, so a Markdown table that
                // documents these rules is not reported as a violation of them.
                csRelativeFiles = files
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                    .Where(rel => !string.IsNullOrEmpty(rel) &&
                                  (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                    .Select(rel => rel!)
                    .ToList();
            }
            else
            {
                csRelativeFiles = Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                    .ToList();
            }

            int issues = 0;
            foreach (string relPath in csRelativeFiles)
            {
                if (relPath.Contains("Services/QueryLinterService.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] lines;
                if (reader is not null)
                {
                    if (!reader.TryReadLines(relPath, out lines))
                    {
                        continue;
                    }
                }
                else
                {
                    try
                    {
                        lines = File.ReadAllLines(Path.Combine(repoRoot, relPath));
                    }
                    catch
                    {
                        continue;
                    }
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                        trimmed.StartsWith('*') ||
                        trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    if (line.Contains("FromSql" + "Raw($", StringComparison.Ordinal) ||
                        line.Contains("ExecuteSql" + "Raw($", StringComparison.Ordinal))
                    {
                        Console.WriteLine($"[QUERY STYLE VIOLATION] {relPath}:{i + 1} - Unparameterized raw SQL interpolation: '{line.Trim()}'");
                        issues++;
                    }

                    if (line.Contains("== true", StringComparison.Ordinal) ||
                        line.Contains("== false", StringComparison.Ordinal))
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

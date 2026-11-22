using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class PathPatternValidatorService
{
    public static int ValidatePathPatterns(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating file path patterns and directory conventions...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ Path patterns validated cleanly (no target files).");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            int violations = 0;
            string[] forbiddenSubdirs = ["tmp", "temp", "scratch", "old_src"];

            System.Collections.Generic.List<string> relativeFiles;

            if (files is not null)
            {
                relativeFiles = files
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                    .Where(rel => !string.IsNullOrEmpty(rel) &&
                                  (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                    .Select(rel => rel!)
                    .ToList();
            }
            else if (reader is { HasAuthoritativeIndex: true })
            {
                relativeFiles = [.. reader.Paths];
            }
            else if (GitRevisionScopeService.TryGetTrackedFiles(repoRoot) is { } trackedFiles)
            {
                relativeFiles = trackedFiles
                    .Select(rel => rel.Replace('\\', '/'))
                    .Where(rel => File.Exists(Path.Combine(repoRoot, rel)))
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                relativeFiles = Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                    .Where(rel => !IsIgnoredFallbackPath(rel))
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }

            // Rule 1: Check forbidden subdirectories across all files
            foreach (string rel in relativeFiles)
            {
                var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts.Where(p => forbiddenSubdirs.Contains(p.ToLowerInvariant())))
                {
                    Console.WriteLine($"[PATH VIOLATION] Invalid directory name '{part}' in path: {rel}");
                    violations++;
                }
            }

            // Rule 2: Check Vue and Shell script boundaries across all tracked paths
            foreach (string rel in relativeFiles)
            {
                if (rel.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) &&
                    !rel.StartsWith("apps/", StringComparison.Ordinal) &&
                    !rel.StartsWith("packages/", StringComparison.Ordinal) &&
                    !rel.StartsWith("docs/.vitepress/", StringComparison.Ordinal))
                {
                    Console.WriteLine($"[PATH VIOLATION] Vue file outside frontend boundary: {rel}");
                    violations++;
                }

                if (rel.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) &&
                    !rel.StartsWith("scripts/", StringComparison.Ordinal))
                {
                    Console.WriteLine($"[PATH VIOLATION] Shell script outside scripts/ boundary: {rel}");
                    violations++;
                }
            }

            if (violations > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {violations} path pattern violations.");
                return 1;
            }

            Console.WriteLine("✔ Path patterns validated cleanly.");
            return 0;
        }
    }

    private static bool IsIgnoredFallbackPath(string relativePath)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] is ".git" or "node_modules" or ".venv" or "venv" or ".cadence" or "bin" or "obj" or "dist" or "coverage" or "playwright-report" or "test-results" or "TestResults")
            {
                return true;
            }
        }

        return false;
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}

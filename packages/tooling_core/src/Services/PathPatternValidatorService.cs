using System;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static class PathPatternValidatorService
{
    public static int ValidatePathPatterns(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
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

            System.Collections.Generic.List<string> relativeAllFiles;

            if (files is not null)
            {
                relativeAllFiles = files
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                    .Where(rel => !string.IsNullOrEmpty(rel) &&
                                  (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                    .Select(rel => rel!)
                    .ToList();
            }
            else if (reader is { HasAuthoritativeIndex: true })
            {
                relativeAllFiles = reader.Paths
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                var tracked = GitRevisionScopeService.TryGetTrackedFiles(repoRoot);
                if (tracked is { Count: > 0 })
                {
                    relativeAllFiles = tracked
                        .Where(rel => File.Exists(Path.Combine(repoRoot, rel)))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList();
                }
                else
                {
                    relativeAllFiles = Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
                        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}.cadence{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                        .Order(StringComparer.Ordinal)
                        .ToList();
                }
            }

            // Rule 1: Check forbidden subdirectories across all files
            foreach (string rel in relativeAllFiles)
            {
                var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts.Where(p => forbiddenSubdirs.Contains(p.ToLowerInvariant())))
                {
                    Console.WriteLine($"[PATH VIOLATION] Invalid directory name '{part}' in path: {rel}");
                    violations++;
                }
            }

            // Rule 2: Check Vue, Shell script, and retired Python boundaries across all repository paths
            foreach (string rel in relativeAllFiles)
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

                if (rel.EndsWith(".py", StringComparison.OrdinalIgnoreCase) &&
                    (rel.StartsWith("apps/", StringComparison.Ordinal) ||
                     rel.StartsWith("packages/", StringComparison.Ordinal)))
                {
                    Console.WriteLine($"[PATH VIOLATION] Retired Python file under apps/ or packages/: {rel}");
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
}

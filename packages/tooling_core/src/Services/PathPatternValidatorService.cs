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

            System.Collections.Generic.List<string> relativeAllFiles;
            System.Collections.Generic.List<string> relativeBoundaryFiles;

            if (files is not null)
            {
                relativeAllFiles = files
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                    .Where(rel => !string.IsNullOrEmpty(rel) &&
                                  (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                    .Select(rel => rel!)
                    .ToList();
                relativeBoundaryFiles = relativeAllFiles;
            }
            else
            {
                relativeAllFiles = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                    .ToList();
                relativeBoundaryFiles = Directory.GetFiles(repoRoot, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                    .ToList();
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

            // Rule 2: Check Vue and Shell script boundaries
            foreach (string rel in relativeBoundaryFiles)
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

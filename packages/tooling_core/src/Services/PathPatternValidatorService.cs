using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class PathPatternValidatorService
{
    public static int ValidatePathPatterns(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating file path patterns and directory conventions...");

        int violations = 0;
        string[] forbiddenSubdirs = ["tmp", "temp", "scratch", "old_src"];

        // Rule 1: Check forbidden subdirectories across all files
        var allFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToList()
            : Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();

        foreach (var file in allFiles)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts.Where(p => forbiddenSubdirs.Contains(p.ToLowerInvariant())))
            {
                Console.WriteLine($"[PATH VIOLATION] Invalid directory name '{part}' in path: {rel}");
                violations++;
            }
        }

        // Rule 2: Check Vue and Shell script boundaries
        string[] boundaryCheckFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToArray()
            : Directory.GetFiles(repoRoot, "*.*", SearchOption.TopDirectoryOnly);

        foreach (var file in boundaryCheckFiles)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("apps/", StringComparison.Ordinal) && !rel.StartsWith("packages/", StringComparison.Ordinal) && !rel.StartsWith("docs/.vitepress/", StringComparison.Ordinal))
            {
                Console.WriteLine($"[PATH VIOLATION] Vue file outside frontend boundary: {rel}");
                violations++;
            }
            if (rel.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("scripts/", StringComparison.Ordinal))
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

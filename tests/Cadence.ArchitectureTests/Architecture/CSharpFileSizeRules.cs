using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces the 500 non-empty line ceiling across hand-written C# source files in <c>packages/kernel/src/</c>,
/// <c>packages/ddf/src/</c>, <c>apps/intelligence/</c>, and <c>apps/*/module/</c> so capabilities stay decomposed
/// into single-responsibility vertical classes that avoid parallel <c>git merge-file</c> tail collisions.
/// </summary>
public static class CSharpFileSizeRules
{
    public const int MaxNonEmptyLines = 500;

    public const string BaselineRelativePath =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/csharp-file-size.baseline.txt";

    public static readonly IReadOnlyList<string> BaselineHeader =
    [
        "# Shrink-only baseline for CSharpFileSizeRules (> 500 non-empty lines across kernel, DDF, and module C# sources).",
        "# Do NOT hand-add entries; split oversized C# files into focused vertical single-responsibility classes instead.",
        "# Format: <repo-relative-path> (<non-empty-lines> non-empty lines > 500)",
    ];

    public const string RemediationGuidance =
        "Split C# files exceeding 500 non-empty lines into focused vertical single-responsibility classes " +
        "(per-format writers, route slices, query readers, snapshot records, or evaluators) rather than appending " +
        "unrelated types or endpoints to a shared file.";

    /// <summary>
    /// Discovers governed source roots under <paramref name="repositoryRoot"/>: <c>packages/kernel/src</c>,
    /// <c>packages/ddf/src</c>, <c>apps/intelligence</c>, and every consolidated <c>apps/*/module</c> directory.
    /// </summary>
    public static IReadOnlyList<string> DiscoverTargetDirectories(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var roots = new List<string>();
        string[] fixedRelativeRoots =
        [
            Path.Combine("packages", "kernel", "src"),
            Path.Combine("packages", "ddf", "src"),
            Path.Combine("apps", "intelligence"),
        ];

        foreach (var relative in fixedRelativeRoots)
        {
            var candidate = Path.Combine(repositoryRoot, relative);
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }

        var appsRoot = Path.Combine(repositoryRoot, "apps");
        if (Directory.Exists(appsRoot))
        {
            foreach (var appDir in Directory.EnumerateDirectories(appsRoot).OrderBy(p => p, StringComparer.Ordinal))
            {
                var moduleDir = Path.Combine(appDir, "module");
                if (Directory.Exists(moduleDir))
                {
                    roots.Add(moduleDir);
                }
            }
        }

        return roots;
    }

    /// <summary>
    /// Evaluates all hand-written C# source files under <see cref="DiscoverTargetDirectories"/> and returns the number
    /// of inspected files together with formatted violation entries for files exceeding <paramref name="maxNonEmptyLines"/>.
    /// </summary>
    public static (int InspectedFileCount, IReadOnlyList<string> Violations) EvaluateRepository(
        string repositoryRoot,
        int maxNonEmptyLines = MaxNonEmptyLines)
    {
        var targetDirectories = DiscoverTargetDirectories(repositoryRoot);
        return EvaluateDirectories(repositoryRoot, targetDirectories, maxNonEmptyLines);
    }

    /// <summary>
    /// Evaluates all hand-written C# source files under <paramref name="targetDirectories"/> relative to
    /// <paramref name="repositoryRoot"/>, excluding build artifacts and EF Core generated migration/designer files.
    /// </summary>
    public static (int InspectedFileCount, IReadOnlyList<string> Violations) EvaluateDirectories(
        string repositoryRoot,
        IEnumerable<string> targetDirectories,
        int maxNonEmptyLines = MaxNonEmptyLines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(targetDirectories);

        var violations = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var inspectedCount = 0;

        foreach (var directory in targetDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(filePath);
                if (!seenFiles.Add(fullPath) || IsExcludedFile(repositoryRoot, fullPath))
                {
                    continue;
                }

                inspectedCount++;
                var nonEmptyLines = CountNonEmptyLines(fullPath);
                if (nonEmptyLines > maxNonEmptyLines)
                {
                    var relativePath = NormalizeRelativePath(repositoryRoot, fullPath);
                    violations.Add($"{relativePath} ({nonEmptyLines} non-empty lines > {maxNonEmptyLines})");
                }
            }
        }

        violations.Sort(StringComparer.Ordinal);
        return (inspectedCount, violations);
    }

    public static int CountNonEmptyLines(string filePath) =>
        File.ReadLines(filePath).Count(static line => !string.IsNullOrWhiteSpace(line));

    private static bool IsExcludedFile(string repositoryRoot, string fullPath)
    {
        var relative = NormalizeRelativePath(repositoryRoot, fullPath);
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "Migrations", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var fileName = Path.GetFileName(fullPath);
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string repositoryRoot, string fullPath) =>
        Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
}

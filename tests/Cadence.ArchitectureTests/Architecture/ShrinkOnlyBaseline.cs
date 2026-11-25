using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Shared shrink-only ratchet baseline evaluator for leaf-sharded architecture sentinels
/// (<c>baselines/direct-state-writes</c>, <c>baselines/permission-matrix</c>,
/// <c>baselines/cross-service-isolation</c>). Uses <see cref="KernelBoundaryBaseline.UpdateEnvironmentVariable"/>
/// (<c>CADENCE_UPDATE_BASELINE=1</c>) strictly to subtract resolved entries and delete empty shards/directories—never
/// adding new violations.
/// </summary>
public static class ShrinkOnlyBaseline
{
    private const string ShardSuffix = ".baseline.txt";
    private static readonly object Gate = new();

    /// <summary>Result of comparing actual entries against a recorded shrink-only baseline.</summary>
    public sealed record Comparison(
        string RelativePath,
        IReadOnlyList<string> NewViolations,
        IReadOnlyList<string> StaleEntries,
        IReadOnlyList<string>? StructureErrors = null)
    {
        public IReadOnlyList<string> ValidationErrors => StructureErrors ?? [];

        public bool IsClean => NewViolations.Count == 0 && StaleEntries.Count == 0 && ValidationErrors.Count == 0;

        public string Describe(string ruleName, string filterHint, string? remediationHint = null)
        {
            var lines = new List<string>();
            if (ValidationErrors.Count > 0)
            {
                lines.Add($"{ruleName}: {ValidationErrors.Count} baseline shard structure error(s) under {RelativePath}:");
                lines.AddRange(ValidationErrors.Select(v => "  ! " + v));
            }

            if (NewViolations.Count > 0)
            {
                lines.Add($"{ruleName}: {NewViolations.Count} NEW violation(s) not in {RelativePath}:");
                lines.AddRange(NewViolations.Select(v => "  + " + v));
                if (!string.IsNullOrWhiteSpace(remediationHint))
                {
                    lines.Add($"Remediation: {remediationHint}");
                }
            }

            if (StaleEntries.Count > 0)
            {
                lines.Add($"{ruleName}: {StaleEntries.Count} baseline entr(y/ies) no longer occur. Delete these lines from {RelativePath}:");
                lines.AddRange(StaleEntries.Select(v => "  - " + v));
            }

            lines.Add(
                $"To drop paid-down baseline entries run: {KernelBoundaryBaseline.UpdateEnvironmentVariable}=1 " +
                $"dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter \"FullyQualifiedName~{filterHint}\" " +
                $"(then review the diff of {RelativePath}).");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Pure comparison between actual entries and recorded baseline entries.</summary>
    public static Comparison Compare(string relativePath, IEnumerable<string> actual, IEnumerable<string> baseline)
    {
        var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
        var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return new Comparison(
            relativePath,
            [.. actualSet.Except(baselineSet, StringComparer.Ordinal)],
            [.. baselineSet.Except(actualSet, StringComparer.Ordinal)]);
    }

    /// <summary>Reads non-comment, non-empty lines from <paramref name="relativePath"/> under <paramref name="repoRoot"/>.</summary>
    public static IReadOnlyList<string> Read(string relativePath, string? repoRoot = null)
    {
        string root = repoRoot ?? KernelBoundaryBaseline.RepositoryRoot();
        string path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    /// <summary>Reads all entries across all <c>*.baseline.txt</c> shards under <paramref name="relativeDirectory"/>.</summary>
    public static IReadOnlyList<string> ReadSharded(string relativeDirectory, string? repoRoot = null)
    {
        string root = repoRoot ?? KernelBoundaryBaseline.RepositoryRoot();
        string fullDirectory = Path.Combine(root, relativeDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(fullDirectory, "*" + ShardSuffix, SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    /// <summary>
    /// Verifies <paramref name="actual"/> against leaf-owner shards under <paramref name="relativeDirectory"/>,
    /// rejecting legacy monolithic baseline files, empty shards, unsorted/duplicate entries, and misplaced entries.
    /// When <see cref="KernelBoundaryBaseline.UpdateRequested"/> (or <paramref name="updateOverride"/>) is enabled,
    /// subtracts only resolved entries from existing shards and deletes empty shards and empty parent directories
    /// without ever adding new violations.
    /// </summary>
    public static Comparison VerifySharded(
        string relativeDirectory,
        string legacyRelativePath,
        IEnumerable<string> actual,
        Func<string, string> getShardRelativePathForEntry,
        Func<string, IReadOnlyList<string>> buildHeaderForShardRelativePath,
        string? repoRoot = null,
        bool? updateOverride = null)
    {
        string root = repoRoot ?? KernelBoundaryBaseline.RepositoryRoot();
        bool update = updateOverride ?? KernelBoundaryBaseline.UpdateRequested;

        var sortedActual = actual
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        lock (Gate)
        {
            var structureErrors = ValidateAndReadShards(
                root,
                relativeDirectory,
                legacyRelativePath,
                getShardRelativePathForEntry,
                out var entriesByShard);

            if (structureErrors.Count == 0 && update)
            {
                PruneStaleEntriesOnly(
                    root,
                    relativeDirectory,
                    entriesByShard,
                    sortedActual,
                    buildHeaderForShardRelativePath);

                structureErrors = ValidateAndReadShards(
                    root,
                    relativeDirectory,
                    legacyRelativePath,
                    getShardRelativePathForEntry,
                    out entriesByShard);
            }

            var recorded = entriesByShard
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .SelectMany(kv => kv.Value)
                .ToList();

            var comparison = Compare(relativeDirectory, sortedActual, recorded);
            return structureErrors.Count == 0
                ? comparison
                : comparison with { StructureErrors = structureErrors };
        }
    }

    /// <summary>
    /// Verifies <paramref name="actual"/> against a single baseline file at <paramref name="relativePath"/>,
    /// strictly subtracting resolved entries when <see cref="KernelBoundaryBaseline.UpdateRequested"/> is enabled.
    /// </summary>
    public static Comparison Verify(
        string relativePath,
        IReadOnlyList<string> headerLines,
        IEnumerable<string> actual,
        string? repoRoot = null,
        bool? updateOverride = null)
    {
        string root = repoRoot ?? KernelBoundaryBaseline.RepositoryRoot();
        bool update = updateOverride ?? KernelBoundaryBaseline.UpdateRequested;

        var sortedActual = actual
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (!update)
        {
            return Compare(relativePath, sortedActual, Read(relativePath, root));
        }

        lock (Gate)
        {
            string fullPath = Path.Combine(root, relativePath);
            if (File.Exists(fullPath))
            {
                var existing = Read(relativePath, root);
                var actualSet = sortedActual.ToHashSet(StringComparer.Ordinal);
                var nextEntries = existing
                    .Where(actualSet.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (nextEntries.Count == 0)
                {
                    File.Delete(fullPath);
                }
                else
                {
                    File.WriteAllText(fullPath, string.Join("\n", headerLines.Concat(nextEntries)) + "\n");
                }
            }

            return Compare(relativePath, sortedActual, Read(relativePath, root));
        }
    }

    private static List<string> ValidateAndReadShards(
        string repoRoot,
        string relativeDirectory,
        string legacyRelativePath,
        Func<string, string> getShardRelativePathForEntry,
        out Dictionary<string, List<string>> entriesByShard)
    {
        entriesByShard = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(legacyRelativePath) &&
            File.Exists(Path.Combine(repoRoot, legacyRelativePath)))
        {
            errors.Add($"Legacy monolithic baseline '{legacyRelativePath}' must be removed; use leaf shards under '{relativeDirectory}/'.");
        }

        string fullDirectory = Path.Combine(repoRoot, relativeDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return errors;
        }

        var files = Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (string relPath in files)
        {
            if (!relPath.EndsWith(ShardSuffix, StringComparison.Ordinal))
            {
                errors.Add($"Unexpected file '{relPath}' under '{relativeDirectory}/'; expected '*{ShardSuffix}'.");
                continue;
            }

            var lines = File.ReadAllLines(Path.Combine(repoRoot, relPath))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();

            if (lines.Count == 0)
            {
                errors.Add($"Empty baseline shard '{relPath}' must be deleted.");
                continue;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                string expectedRelPath;
                try
                {
                    expectedRelPath = getShardRelativePathForEntry(lines[i]);
                }
                catch (Exception ex)
                {
                    errors.Add($"Invalid entry '{lines[i]}' in '{relPath}': {ex.Message}");
                    continue;
                }

                if (!string.Equals(relPath, expectedRelPath, StringComparison.Ordinal))
                {
                    errors.Add($"Entry '{lines[i]}' in '{relPath}' belongs in '{expectedRelPath}'.");
                }

                if (i > 0 && StringComparer.Ordinal.Compare(lines[i - 1], lines[i]) >= 0)
                {
                    errors.Add($"Shard '{relPath}' must be strictly sorted ordinally with no duplicate entries (at '{lines[i]}').");
                }
            }

            entriesByShard[relPath] = lines;
        }

        return errors;
    }

    private static void PruneStaleEntriesOnly(
        string repoRoot,
        string relativeDirectory,
        Dictionary<string, List<string>> entriesByShard,
        IReadOnlyList<string> sortedActual,
        Func<string, IReadOnlyList<string>> buildHeaderForShardRelativePath)
    {
        var actualSet = sortedActual.ToHashSet(StringComparer.Ordinal);
        string fullDirectory = Path.Combine(repoRoot, relativeDirectory);

        foreach (var (shardRelPath, existingEntries) in entriesByShard.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var kept = existingEntries
                .Where(actualSet.Contains)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (kept.SequenceEqual(existingEntries, StringComparer.Ordinal))
            {
                continue;
            }

            string shardFullPath = Path.Combine(repoRoot, shardRelPath);
            if (kept.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, fullDirectory);
                }
            }
            else
            {
                var header = buildHeaderForShardRelativePath(shardRelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
                File.WriteAllText(shardFullPath, string.Join("\n", header.Concat(kept)) + "\n");
            }
        }
    }
}

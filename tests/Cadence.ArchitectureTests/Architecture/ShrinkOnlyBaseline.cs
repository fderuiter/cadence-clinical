using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Shared shrink-only ratchet baseline evaluator for leaf-sharded architecture sentinels
/// (<c>baselines/direct-state-writes</c>, <c>baselines/permission-matrix</c>,
/// <c>baselines/cross-service-isolation</c>) and standalone shrink-only baselines
/// (<c>prd-requirement-coverage.baseline.txt</c>). Uses <see cref="KernelBoundaryBaseline.UpdateEnvironmentVariable"/>
/// (<c>CADENCE_UPDATE_BASELINE=1</c>) strictly to subtract resolved entries and delete empty shards/directories—never
/// adding new violations.
/// </summary>
public static class ShrinkOnlyBaseline
{
    private static readonly Lock Gate = new();

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

        return TextBaselineShardStore.ParseEntries(File.ReadAllLines(path));
    }

    /// <summary>Reads all entries across all <c>*.baseline.txt</c> shards under <paramref name="relativeDirectory"/>.</summary>
    public static IReadOnlyList<string> ReadSharded(string relativeDirectory, string? repoRoot = null)
    {
        string root = repoRoot ?? KernelBoundaryBaseline.RepositoryRoot();
        return TextBaselineShardStore.ReadAllEntries(root, relativeDirectory);
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

        static bool HasShardExtension(string relPath) =>
            relPath.EndsWith(TextBaselineShardStore.ShardSuffix, StringComparison.Ordinal);

        string? TryResolveShardPath(string entry)
        {
            try
            {
                return getShardRelativePathForEntry(entry);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return null;
            }
        }

        lock (Gate)
        {
            var structureErrors = TextBaselineShardStore.ValidateAndReadShards(
                root,
                relativeDirectory,
                legacyRelativePath,
                HasShardExtension,
                TryResolveShardPath,
                out var entriesByShard,
                reader: null,
                requireNonEmptyDirectory: true,
                allowDuplicateEntries: false);

            if (structureErrors.Count == 0 && update)
            {
                var actualSet = sortedActual.ToHashSet(StringComparer.Ordinal);
                var staleEntries = entriesByShard.Values
                    .SelectMany(entries => entries)
                    .Where(entry => !actualSet.Contains(entry))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (staleEntries.Count > 0)
                {
                    TextBaselineShardStore.SubtractStaleEntries(
                        root,
                        relativeDirectory,
                        entriesByShard,
                        staleEntries,
                        buildHeaderForShardRelativePath,
                        multiset: false);

                    structureErrors = TextBaselineShardStore.ValidateAndReadShards(
                        root,
                        relativeDirectory,
                        legacyRelativePath,
                        HasShardExtension,
                        TryResolveShardPath,
                        out entriesByShard,
                        reader: null,
                        requireNonEmptyDirectory: false,
                        allowDuplicateEntries: false);
                }
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
}

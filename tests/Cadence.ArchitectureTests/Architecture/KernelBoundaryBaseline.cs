using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Ratchet baseline for the kernel boundary rules. Pre-existing violations are recorded, one
/// <c>RULE|Assembly|Type[.Member]</c> per line (ordinal sort, LF line endings), in per-assembly shards under
/// <c>tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/&lt;Assembly&gt;.baseline.txt</c>.
/// A rule check fails when it finds a violation not in the baseline (new debt) and when a baseline line no longer
/// occurs (debt paid down but not removed), so shards can only shrink and empty shards are deleted.
///
/// Prune resolved entries: <c>CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter "FullyQualifiedName~KernelBoundaryTests"</c>
/// subtracts paid-down lines from touched assembly shards and never adds new violations; accepting new debt requires a
/// reviewed edit and a <c>Baseline-Growth: &lt;path&gt; - &lt;reason&gt;</c> commit trailer (docs/TESTING.md).
/// </summary>
public static class KernelBoundaryBaseline
{
    public const string UpdateEnvironmentVariable = "CADENCE_UPDATE_BASELINE";

    public const string RelativeDirectory = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries";

    private const string ShardExtension = ".baseline.txt";

    private const string RegenerateHint =
        $"To prune paid-down baseline entries run: {UpdateEnvironmentVariable}=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj " +
        "--filter \"FullyQualifiedName~KernelBoundaryTests\" (then review the diff under " + RelativeDirectory + "/).";

    private static readonly string[] Header =
    [
        "# Kernel boundary ratchet baseline shard (K-XS-1, K-MOD-1) - docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md §2, §9.",
        "# One pre-existing violation per line: RULE|Assembly|Type[.Member]. Sorted ordinally. Shards may only shrink.",
        $"# Prune resolved entries with {UpdateEnvironmentVariable}=1 (see tests/Cadence.ArchitectureTests/Architecture/KernelBoundaryBaseline.cs).",
    ];

    private static readonly object Gate = new();

    public static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal);

    public static string ShardRelativePath(string assembly) => $"{RelativeDirectory}/{assembly}{ShardExtension}";

    public static string ExtractAssembly(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        int firstPipe = entry.IndexOf('|', StringComparison.Ordinal);
        int secondPipe = firstPipe >= 0 ? entry.IndexOf('|', firstPipe + 1) : -1;
        if (firstPipe <= 0 || secondPipe <= firstPipe + 1)
        {
            throw new FormatException($"Expected 'RULE|Assembly|Type[.Member]' entry, got '{entry}'.");
        }

        return entry[(firstPipe + 1)..secondPipe];
    }

    /// <summary>Result of comparing actual violations to the recorded baseline for one rule.</summary>
    public sealed record Comparison(IReadOnlyList<string> NewViolations, IReadOnlyList<string> StaleEntries)
    {
        public bool IsClean => NewViolations.Count == 0 && StaleEntries.Count == 0;

        public string Describe(string rule)
        {
            var lines = new List<string>();
            if (NewViolations.Count > 0)
            {
                lines.Add($"{rule}: {NewViolations.Count} NEW violation(s) not in {RelativeDirectory}/. Use kernel ID types / kernel concepts instead:");
                lines.AddRange(NewViolations.Select(v => "  + " + v));
            }

            if (StaleEntries.Count > 0)
            {
                lines.Add($"{rule}: {StaleEntries.Count} baseline entr(y/ies) no longer occur. Delete these lines from {RelativeDirectory}/:");
                lines.AddRange(StaleEntries.Select(v => "  - " + v));
            }

            lines.Add(RegenerateHint);
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Pure ratchet comparison: what is new, and what in the baseline has disappeared.</summary>
    public static Comparison Compare(IEnumerable<string> actual, IEnumerable<string> baseline)
    {
        var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
        var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return new Comparison(
            [.. actualSet.Except(baselineSet, StringComparer.Ordinal)],
            [.. baselineSet.Except(actualSet, StringComparer.Ordinal)]);
    }

    /// <summary>Baseline entries recorded across all assembly shards for <paramref name="rule"/>.</summary>
    public static IReadOnlyList<string> Read(string rule, string? baselineDirectory = null)
    {
        string dir = baselineDirectory ?? FullDirectoryPath();
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir, "*" + ShardExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .SelectMany(ReadEntriesFromShard)
            .Where(line => line.StartsWith(rule + "|", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>All baseline entries across every assembly shard.</summary>
    public static IReadOnlyList<string> ReadAll(string? baselineDirectory = null)
    {
        string dir = baselineDirectory ?? FullDirectoryPath();
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir, "*" + ShardExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .SelectMany(ReadEntriesFromShard)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Writes shard files grouped by assembly name and deletes empty shards.</summary>
    public static void WriteShards(IEnumerable<string> entries, string? baselineDirectory = null)
    {
        string dir = baselineDirectory ?? FullDirectoryPath();
        var grouped = entries
            .Distinct(StringComparer.Ordinal)
            .GroupBy(ExtractAssembly, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        if (Directory.Exists(dir))
        {
            foreach (string existingShard in Directory.GetFiles(dir, "*" + ShardExtension, SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(existingShard);
                string assembly = fileName[..^ShardExtension.Length];
                if (!grouped.TryGetValue(assembly, out var shardEntries) || shardEntries.Count == 0)
                {
                    File.Delete(existingShard);
                }
            }
        }

        foreach (var (assembly, shardEntries) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (shardEntries.Count == 0)
            {
                continue;
            }

            Directory.CreateDirectory(dir);
            string shardPath = Path.Combine(dir, assembly + ShardExtension);
            File.WriteAllText(shardPath, string.Join("\n", Header.Concat(shardEntries)) + "\n");
        }
    }

    /// <summary>
    /// Compares <paramref name="actual"/> with the baseline shards for <paramref name="rule"/>; when
    /// <see cref="UpdateRequested"/> (or <paramref name="updateOverride"/>) is set, subtracts only stale entries from
    /// touched shards (deleting any shard that reaches zero entries) and never adds new violations.
    /// </summary>
    public static Comparison Verify(
        string rule,
        IReadOnlyList<string> actual,
        string? baselineDirectory = null,
        bool? updateOverride = null)
    {
        bool shouldUpdate = updateOverride ?? UpdateRequested;
        if (!shouldUpdate)
        {
            return Compare(actual, Read(rule, baselineDirectory));
        }

        lock (Gate)
        {
            string dir = baselineDirectory ?? FullDirectoryPath();
            var comparison = Compare(actual, Read(rule, dir));
            if (Directory.Exists(dir))
            {
                var staleByAssembly = comparison.StaleEntries
                    .GroupBy(ExtractAssembly, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

                foreach (string shardPath in Directory.GetFiles(dir, "*" + ShardExtension, SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(shardPath);
                    string assembly = fileName[..^ShardExtension.Length];
                    var existing = ReadEntriesFromShard(shardPath);
                    if (existing.Count == 0)
                    {
                        File.Delete(shardPath);
                        continue;
                    }

                    if (!staleByAssembly.TryGetValue(assembly, out var staleSet) || staleSet.Count == 0)
                    {
                        continue;
                    }

                    var kept = existing
                        .Except(staleSet, StringComparer.Ordinal)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList();

                    if (kept.Count == 0)
                    {
                        File.Delete(shardPath);
                    }
                    else if (!existing.SequenceEqual(kept, StringComparer.Ordinal))
                    {
                        File.WriteAllText(shardPath, string.Join("\n", Header.Concat(kept)) + "\n");
                    }
                }
            }

            return Compare(actual, Read(rule, dir));
        }
    }

    /// <summary>Walks up from the test output directory to the directory containing Cadence.slnx.</summary>
    public static string RepositoryRoot() => Cadence.Topology.RepositoryRoot.Find(AppContext.BaseDirectory);

    private static List<string> ReadEntriesFromShard(string shardPath) =>
        File.ReadAllLines(shardPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static string FullDirectoryPath() => Path.Combine(RepositoryRoot(), RelativeDirectory);
}

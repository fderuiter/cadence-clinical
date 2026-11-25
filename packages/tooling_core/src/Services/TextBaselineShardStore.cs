using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Shared Native-AOT-safe reader, fail-closed validator, writer, and shrink-only pruner for <c>*.baseline.txt</c>
/// leaf-owner shards across sentinel and compliance gates.
/// </summary>
public static class TextBaselineShardStore
{
    public const string ShardSuffix = ".baseline.txt";

    /// <summary>Extracts non-empty, non-comment lines from raw baseline text lines.</summary>
    public static List<string> ParseEntries(IEnumerable<string> rawLines) =>
        [.. rawLines.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    /// <summary>Reads all entries across <c>*.baseline.txt</c> shards under <paramref name="gateDirectory"/> in ordinal shard order.</summary>
    public static IReadOnlyList<string> ReadAllEntries(string repoRoot, string gateDirectory)
    {
        string rootFullPath = Path.Combine(repoRoot, gateDirectory);
        if (!Directory.Exists(rootFullPath))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(rootFullPath, "*" + ShardSuffix, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];
    }

    /// <summary>
    /// Reads and validates all files under <paramref name="gateDirectory"/> (from <paramref name="reader"/> when
    /// authoritative in <c>--pre-commit</c> mode, otherwise from disk under <paramref name="repoRoot"/>).
    /// </summary>
    public static List<string> ValidateAndReadShards(
        string repoRoot,
        string gateDirectory,
        string? legacyRelativePath,
        Func<string, bool> isValidShardPath,
        Func<string, string?> tryResolveExpectedShardPath,
        out Dictionary<string, List<string>> entriesByShard,
        GitIndexReader? reader = null,
        bool requireNonEmptyDirectory = false,
        bool allowDuplicateEntries = false,
        string? expectedShardPatternHint = null)
    {
        entriesByShard = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        bool useIndex = reader is { HasAuthoritativeIndex: true };

        if (!string.IsNullOrWhiteSpace(legacyRelativePath))
        {
            bool hasLegacy = useIndex
                ? reader!.FileExists(legacyRelativePath)
                : File.Exists(Path.Combine(repoRoot, legacyRelativePath));
            if (hasLegacy)
            {
                errors.Add(
                    $"Legacy monolithic baseline '{legacyRelativePath}' must be removed; use leaf shards under '{gateDirectory}/'.");
            }
        }

        List<string> candidateFiles;
        if (useIndex)
        {
            candidateFiles = [.. reader!.Paths
                .Where(p => p.StartsWith(gateDirectory + "/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
        }
        else
        {
            string fullDirectory = Path.Combine(repoRoot, gateDirectory);
            if (!Directory.Exists(fullDirectory))
            {
                if (requireNonEmptyDirectory)
                {
                    errors.Add($"Required baseline directory '{gateDirectory}' was not found.");
                }

                return errors;
            }

            candidateFiles = [.. Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)];
        }

        if (candidateFiles.Count == 0)
        {
            if (requireNonEmptyDirectory)
            {
                errors.Add($"Required baseline directory '{gateDirectory}' contained no shards.");
            }

            return errors;
        }

        string patternHint = expectedShardPatternHint ?? $"{gateDirectory}/*{ShardSuffix}";
        foreach (string relPath in candidateFiles)
        {
            if (!isValidShardPath(relPath))
            {
                errors.Add($"Invalid or misplaced baseline shard '{relPath}'; expected '{patternHint}'.");
                continue;
            }

            string[] rawLines;
            if (useIndex)
            {
                if (!reader!.TryReadLines(relPath, out rawLines))
                {
                    errors.Add($"Baseline shard '{relPath}' could not be read from the Git index.");
                    continue;
                }
            }
            else
            {
                rawLines = File.ReadAllLines(Path.Combine(repoRoot, relPath));
            }

            var entries = ParseEntries(rawLines);
            if (entries.Count == 0)
            {
                errors.Add($"Empty baseline shard '{relPath}' must be deleted.");
                continue;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                string? expectedRelPath = tryResolveExpectedShardPath(entries[i]);
                if (expectedRelPath is null)
                {
                    errors.Add($"Invalid baseline entry '{entries[i]}' in '{relPath}'.");
                }
                else if (!string.Equals(relPath, expectedRelPath, StringComparison.Ordinal))
                {
                    errors.Add($"Entry '{entries[i]}' in '{relPath}' belongs in '{expectedRelPath}'.");
                }

                if (i > 0)
                {
                    int cmp = StringComparer.Ordinal.Compare(entries[i - 1], entries[i]);
                    if (allowDuplicateEntries ? cmp > 0 : cmp >= 0)
                    {
                        errors.Add(allowDuplicateEntries
                            ? $"Baseline shard '{relPath}' must be sorted ordinally (at '{entries[i]}')."
                            : $"Baseline shard '{relPath}' must be strictly sorted ordinally with no duplicate entries (at '{entries[i]}').");
                    }
                }
            }

            entriesByShard[relPath] = entries;
        }

        return errors;
    }

    /// <summary>Writes leaf-owner <c>*.baseline.txt</c> shards under <paramref name="gateDirectory"/> and deletes stale/empty shards.</summary>
    public static void WriteShards(
        string repoRoot,
        string gateDirectory,
        IEnumerable<string> entries,
        Func<string, string> getShardRelativePathForEntry,
        Func<string, IReadOnlyList<string>> buildHeaderForShardRelativePath,
        bool allowDuplicateEntries = false)
    {
        string rootFullPath = Path.Combine(repoRoot, gateDirectory);
        var grouped = entries
            .GroupBy(getShardRelativePathForEntry, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (allowDuplicateEntries ? g : g.Distinct(StringComparer.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        if (Directory.Exists(rootFullPath))
        {
            foreach (string existingFull in Directory.GetFiles(rootFullPath, "*" + ShardSuffix, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, existingFull).Replace('\\', '/');
                if (!grouped.TryGetValue(rel, out var shardEntries) || shardEntries.Count == 0)
                {
                    File.Delete(existingFull);
                    BaselineUnitPath.DeleteEmptyParentDirectories(existingFull, rootFullPath);
                }
            }
        }

        foreach (var (shardRel, shardEntries) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (shardEntries.Count == 0)
            {
                continue;
            }

            WriteSingleShardFile(
                Path.Combine(repoRoot, shardRel),
                buildHeaderForShardRelativePath(shardRel),
                shardEntries);
        }
    }

    /// <summary>
    /// Subtracts <paramref name="staleEntries"/> from <paramref name="entriesByShard"/>, rewriting modified non-empty
    /// shards and deleting shards (plus empty parent directories up to <paramref name="gateDirectory"/>) that reach zero
    /// entries. Never adds new entries or creates new shards.
    /// </summary>
    public static int SubtractStaleEntries(
        string repoRoot,
        string gateDirectory,
        IReadOnlyDictionary<string, List<string>> entriesByShard,
        IEnumerable<string> staleEntries,
        Func<string, IReadOnlyList<string>> buildHeaderForShardRelativePath,
        bool multiset = false)
    {
        var remainingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string stale in staleEntries)
        {
            remainingCounts[stale] = multiset ? remainingCounts.GetValueOrDefault(stale) + 1 : int.MaxValue;
        }

        if (remainingCounts.Count == 0)
        {
            return 0;
        }

        string gateRootFull = Path.Combine(repoRoot, gateDirectory);
        int pruned = 0;

        foreach (var (shardRelPath, existingEntries) in entriesByShard.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var kept = new List<string>(existingEntries.Count);
            foreach (string entry in existingEntries)
            {
                if (remainingCounts.TryGetValue(entry, out int count) && count > 0)
                {
                    if (multiset)
                    {
                        remainingCounts[entry] = count - 1;
                    }

                    pruned++;
                }
                else
                {
                    kept.Add(entry);
                }
            }

            if (kept.Count == existingEntries.Count)
            {
                continue;
            }

            string shardFullPath = Path.Combine(repoRoot, shardRelPath);
            if (kept.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, gateRootFull);
                }
            }
            else
            {
                kept.Sort(StringComparer.Ordinal);
                WriteSingleShardFile(shardFullPath, buildHeaderForShardRelativePath(shardRelPath), kept);
            }
        }

        return pruned;
    }

    private static void WriteSingleShardFile(
        string shardFullPath,
        IReadOnlyList<string> headerLines,
        IReadOnlyList<string> sortedEntries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
        File.WriteAllText(shardFullPath, string.Join('\n', headerLines.Concat(sortedEntries)) + "\n");
    }
}

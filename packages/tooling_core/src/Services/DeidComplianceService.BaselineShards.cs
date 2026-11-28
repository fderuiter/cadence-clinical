using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.ToolingCore.Services;

public static partial class DeidComplianceService
{
    /// <summary>Directory containing per-unit ratchet baseline shards, relative to the repository root.</summary>
    public const string BaselineDirectory = "baselines/deid";

    private const string ShardSuffix = ".baseline.txt";

    private static readonly string[] BaselineShardHeader =
    [
        "# DEID scan ratchet baseline shard (pnpm cadence check deid). Findings that predate the full detector and are not PHI.",
        "# One per line: path|category|first 16 hex digits of SHA-256(trimmed matched value). Sorted ordinally. Shards may only shrink.",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid [files...] (docs/cli.md).",
    ];

    public static string GetShardRelativePath(string filePath) =>
        $"{BaselineDirectory}/{BaselineUnitPath.ResolveUnit(filePath)}{ShardSuffix}";

    /// <summary>
    /// Splits findings into those not covered by the baseline and baseline entries no finding uses. Entries are
    /// counted, so a matched token that appears twice in the same file needs two entries.
    /// </summary>
    public static (IReadOnlyList<DeidFinding> NewFindings, IReadOnlyList<string> StaleEntries) CompareWithBaseline(
        IReadOnlyList<(DeidFinding Finding, string Key)> findings,
        IReadOnlyList<string> baseline)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(baseline);
        var remaining = baseline.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newFindings = new List<DeidFinding>();
        foreach (var (finding, key) in findings)
        {
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
            }
            else
            {
                newFindings.Add(finding);
            }
        }

        var stale = remaining.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).Order(StringComparer.Ordinal).ToList();
        return (newFindings, stale);
    }

    /// <summary>
    /// A finding's baseline entry: <c>path|category|hash</c>, where the hash is the first 16 lowercase hex digits of the
    /// SHA-256 of the trimmed matched substring. Non-PHI edits elsewhere on the line leave the digest unchanged, and no raw
    /// PHI is stored or printed.
    /// </summary>
    public static string BaselineKey(DeidFinding finding, string matchedValue)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(matchedValue);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(matchedValue.Trim()));
        return $"{finding.File}|{finding.Category}|{Convert.ToHexStringLower(hash)[..16]}";
    }

    /// <summary>Writes per-unit DEID baseline shards under <see cref="BaselineDirectory"/> and deletes empty shards.</summary>
    public static void WriteBaselineShards(string repoRoot, IEnumerable<string> keys)
    {
        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        var grouped = keys
            .GroupBy(key => GetShardRelativePath(ExtractFileFromBaselineEntry(key)), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        if (Directory.Exists(deidRootFull))
        {
            foreach (string existingFull in Directory.GetFiles(deidRootFull, "*" + ShardSuffix, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, existingFull).Replace('\\', '/');
                if (!grouped.TryGetValue(rel, out var shardKeys) || shardKeys.Count == 0)
                {
                    File.Delete(existingFull);
                    BaselineUnitPath.DeleteEmptyParentDirectories(existingFull, deidRootFull);
                }
            }
        }

        foreach (var (shardRel, shardKeys) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (shardKeys.Count == 0)
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, shardRel);
            WriteShardFile(fullPath, shardKeys);
        }
    }

    private static Dictionary<string, List<string>> ReadBaselineShards(string repoRoot, GitIndexReader? reader)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (reader is { HasAuthoritativeIndex: true })
        {
            foreach (string relPath in reader.Paths
                .Where(p => p.StartsWith(BaselineDirectory + "/", StringComparison.Ordinal) &&
                            p.EndsWith(ShardSuffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            {
                if (reader.TryReadLines(relPath, out string[] indexedLines))
                {
                    result[relPath] = [.. indexedLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
                }
            }

            return result;
        }

        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        if (!Directory.Exists(deidRootFull))
        {
            return result;
        }

        foreach (string fullPath in Directory.EnumerateFiles(deidRootFull, "*" + ShardSuffix, SearchOption.AllDirectories))
        {
            string relPath = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            result[relPath] = [.. File.ReadAllLines(fullPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        }

        return result;
    }

    private static int SubtractStaleEntriesFromShards(
        string repoRoot,
        Dictionary<string, List<string>> entriesByShard,
        IReadOnlyList<string> staleEntries)
    {
        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        var staleCountsByShard = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (string staleKey in staleEntries)
        {
            string shardRel = GetShardRelativePath(ExtractFileFromBaselineEntry(staleKey));
            if (!staleCountsByShard.TryGetValue(shardRel, out var counts))
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                staleCountsByShard[shardRel] = counts;
            }

            counts[staleKey] = counts.GetValueOrDefault(staleKey) + 1;
        }

        int totalPruned = 0;
        foreach (var (shardRel, existingEntries) in entriesByShard)
        {
            string shardFullPath = Path.Combine(repoRoot, shardRel);
            if (existingEntries.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, deidRootFull);
                }

                continue;
            }

            if (!staleCountsByShard.TryGetValue(shardRel, out var toSubtract) || toSubtract.Count == 0)
            {
                continue;
            }

            var kept = new List<string>(existingEntries.Count);
            foreach (string entry in existingEntries)
            {
                if (toSubtract.TryGetValue(entry, out int remainingToDrop) && remainingToDrop > 0)
                {
                    toSubtract[entry] = remainingToDrop - 1;
                    totalPruned++;
                }
                else
                {
                    kept.Add(entry);
                }
            }

            kept.Sort(StringComparer.Ordinal);
            if (kept.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, deidRootFull);
                }
            }
            else if (!existingEntries.SequenceEqual(kept, StringComparer.Ordinal))
            {
                WriteShardFile(shardFullPath, kept);
            }
        }

        return totalPruned;
    }

    private static string ExtractFileFromBaselineEntry(string entry)
    {
        int pipeIdx = entry.IndexOf('|', StringComparison.Ordinal);
        return pipeIdx > 0 ? entry[..pipeIdx] : entry;
    }

    private static void WriteShardFile(string fullPath, IReadOnlyList<string> sortedKeys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, string.Join('\n', BaselineShardHeader.Concat(sortedKeys)) + "\n");
    }
}

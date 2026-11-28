using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DuplicationDetectorService
{
    private static readonly string[] BaselineHeader =
    [
        "# Code duplication ratchet baseline (.cs, .js, .jsx, .mjs, .cjs, .ts, .tsx, .mts, .cts, .vue, .css clones >15 normalized lines).",
        "# One pre-existing clone window per line: <fileA>|<fileB>|<sha256_16>. Sorted ordinally. May only shrink.",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check duplication [files...] (docs/cli.md).",
    ];

    private static readonly string[] IgnoredPathKeywords =
    [
        "node_modules", "tests", ".venv", ".cadence", "__pycache__", "dist", "build", "bin", "obj", "coverage", "Migrations", "migrations", ".git", "mockServiceWorker.js"
    ];

    private static readonly HashSet<string> SupportedDuplicationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".vue", ".css",
    };

    private static readonly HashSet<string> IntraFileDuplicationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".vue",
    };

    public static string BaselineKey(string fileA, string fileB, string blockSha256Hex)
    {
        ArgumentNullException.ThrowIfNull(fileA);
        ArgumentNullException.ThrowIfNull(fileB);
        ArgumentNullException.ThrowIfNull(blockSha256Hex);
        (string first, string second) = string.CompareOrdinal(fileA, fileB) <= 0 ? (fileA, fileB) : (fileB, fileA);
        string hash16 = blockSha256Hex.Length > 16 ? blockSha256Hex[..16].ToLowerInvariant() : blockSha256Hex.ToLowerInvariant();
        return $"{first}|{second}|{hash16}";
    }

    public static (IReadOnlyList<DuplicateBlock> NewDuplicates, IReadOnlyList<string> StaleEntries) CompareWithBaseline(
        IReadOnlyList<DuplicateBlock> duplicates,
        IReadOnlyList<string> baseline)
    {
        ArgumentNullException.ThrowIfNull(duplicates);
        ArgumentNullException.ThrowIfNull(baseline);
        var remaining = baseline
            .GroupBy(k => k, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newDuplicates = new List<DuplicateBlock>();
        foreach (var dup in duplicates)
        {
            if (remaining.TryGetValue(dup.Key, out int count) && count > 0)
            {
                remaining[dup.Key] = count - 1;
            }
            else
            {
                newDuplicates.Add(dup);
            }
        }

        var stale = remaining
            .Where(kv => kv.Value > 0)
            .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
            .Order(StringComparer.Ordinal)
            .ToList();
        return (newDuplicates, stale);
    }

    public static List<string> ReadBaselineEntries(string repoRoot, GitIndexReader? reader = null)
    {
        if (reader is { HasAuthoritativeIndex: true })
        {
            if (reader.TryReadLines(BaselineFileName, out string[] indexedLines))
            {
                return [.. indexedLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
            }

            return [];
        }

        string fullPath = Path.Combine(repoRoot, BaselineFileName);
        if (!File.Exists(fullPath))
        {
            return [];
        }

        return [.. File.ReadAllLines(fullPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
    }

    public static void WriteBaseline(string repoRoot, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(keys);
        string fullPath = Path.Combine(repoRoot, BaselineFileName);
        var sorted = keys.Order(StringComparer.Ordinal).ToList();
        if (sorted.Count == 0)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return;
        }

        var sb = new StringBuilder();
        foreach (string headerLine in BaselineHeader)
        {
            sb.Append(headerLine).Append('\n');
        }

        foreach (string key in sorted)
        {
            sb.Append(key).Append('\n');
        }

        File.WriteAllText(fullPath, sb.ToString(), WorkspacePrimitives.Utf8NoBom);
    }

    private static bool IsEntryInScope(string entry, HashSet<string> targetSet)
    {
        int firstPipe = entry.IndexOf('|', StringComparison.Ordinal);
        if (firstPipe <= 0)
        {
            return false;
        }

        int secondPipe = entry.IndexOf('|', firstPipe + 1);
        if (secondPipe <= firstPipe + 1)
        {
            return false;
        }

        string fileA = entry[..firstPipe];
        string fileB = entry[(firstPipe + 1)..secondPipe];
        return targetSet.Contains(fileA) || targetSet.Contains(fileB);
    }

    private static int SubtractStaleEntries(
        string repoRoot,
        List<string> existingEntries,
        IReadOnlyList<string> staleEntries)
    {
        var staleCounts = staleEntries
            .GroupBy(k => k, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var kept = new List<string>(existingEntries.Count);
        int pruned = 0;
        foreach (string entry in existingEntries)
        {
            if (staleCounts.TryGetValue(entry, out int count) && count > 0)
            {
                staleCounts[entry] = count - 1;
                pruned++;
            }
            else
            {
                kept.Add(entry);
            }
        }

        if (pruned > 0)
        {
            WriteBaseline(repoRoot, kept);
        }

        return pruned;
    }

    private static bool IsIntraFileDuplicationEnforced(string relativePath) =>
        IntraFileDuplicationExtensions.Contains(Path.GetExtension(relativePath));

    private static bool IsAmbientDeclarationOrTestPath(string normalizedSlashPath)
    {
        if (normalizedSlashPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
            normalizedSlashPath.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase) ||
            normalizedSlashPath.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase) ||
            normalizedSlashPath.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int lastSlash = normalizedSlashPath.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? normalizedSlashPath[(lastSlash + 1)..] : normalizedSlashPath;
        return fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnforcedDuplicationFile(string relativePath)
    {
        string norm = "/" + relativePath.Replace('\\', '/').TrimStart('/');
        if (IsAmbientDeclarationOrTestPath(norm))
        {
            return false;
        }

        return SupportedDuplicationExtensions.Contains(Path.GetExtension(norm));
    }

    private static bool IsScannableDuplicationPath(string relativePath)
    {
        string norm = "/" + relativePath.Replace('\\', '/').TrimStart('/');
        bool isRootFile = norm.IndexOf('/', 1) < 0;
        bool isInScannableDir =
            norm.StartsWith("/apps/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("/packages/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("/scripts/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("/docs/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("/.claude/", StringComparison.OrdinalIgnoreCase);

        if (!isRootFile && !isInScannableDir)
        {
            return false;
        }

        if (IgnoredPathKeywords.Any(k => norm.Contains($"/{k}/", StringComparison.Ordinal) || norm.EndsWith($"/{k}", StringComparison.Ordinal)))
        {
            return false;
        }

        if (norm.Contains("/generated/", StringComparison.OrdinalIgnoreCase) ||
            IsAmbientDeclarationOrTestPath(norm) ||
            norm.Equals("/apps/web/src/api/types.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SupportedDuplicationExtensions.Contains(Path.GetExtension(norm));
    }
}

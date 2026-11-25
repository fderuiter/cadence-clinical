using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static class DuplicationDetectorService
{
    public const string BaselineFileName = ".duplication-baseline.txt";

    private static readonly string[] BaselineHeader =
    [
        "# Code duplication ratchet baseline (.cs, .js, .mjs, .cjs, .ts, .mts, .cts, .tsx, .vue, .css clones >15 normalized lines).",
        "# One pre-existing clone window per line: <fileA>|<fileB>|<sha256_16>. Sorted ordinally. May only shrink.",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check duplication [files...] (docs/cli.md).",
    ];

    public sealed record DuplicateBlock(
        (string File, int Start, int End, string Preview) Loc1,
        (string File, int Start, int End, string Preview) Loc2,
        string Key);

    private static readonly string[] ScannedRootDirectories =
    [
        "apps", "packages", "scripts", "docs", ".claude",
    ];

    private static readonly string[] ExcludedPathPrefixes =
    [
        "/.claude/worktrees/",
        "/docs/.vitepress/cache/",
        "/docs/.vitepress/.temp/",
    ];

    private static readonly string[] AmbientDeclarationSuffixes =
    [
        ".d.ts", ".d.mts", ".d.cts",
    ];

    private static readonly HashSet<string> RootModuleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mjs", ".cjs",
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts", ".tsx", ".vue", ".css",
    };

    public static IReadOnlyCollection<string> SupportedDuplicationExtensions => SupportedExtensions;

    private static readonly string[] IgnoredPathKeywords =
    [
        "node_modules", "tests", ".venv", ".cadence", "__pycache__", "dist", "build", "bin", "obj", "coverage", "Migrations", "migrations", ".git", "mockServiceWorker.js"
    ];

    public static int DetectDuplication(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Scanning codebase for duplicated code blocks (>15 lines)...");

        if (targetFiles is { Length: 0 })
        {
            Console.WriteLine("✔ Code duplication check passed cleanly (no target files).");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            bool updateBaseline = !preCommit &&
                                  Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE") == "1";

            int windowSize = 15;
            var allRelativeFiles = new List<string>();

            HashSet<string>? targetSet = null;
            if (targetFiles is not null)
            {
                targetSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (string raw in targetFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (!string.IsNullOrEmpty(rel) && IsScannableDuplicationPath(rel))
                    {
                        targetSet.Add(rel);
                    }
                }

                if (targetSet.Count == 0)
                {
                    Console.WriteLine("✔ Code duplication check passed cleanly (no matching source files in target list).");
                    return 0;
                }
            }

            if (reader is { HasAuthoritativeIndex: true })
            {
                allRelativeFiles.AddRange(reader.Paths.Where(IsScannableDuplicationPath));
            }
            else
            {
                foreach (string dirName in ScannedRootDirectories)
                {
                    string dir = Path.Combine(repoRoot, dirName);
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        if (IsScannableDuplicationPath(rel))
                        {
                            allRelativeFiles.Add(rel);
                        }
                    }
                }

                if (Directory.Exists(repoRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(repoRoot, "*", SearchOption.TopDirectoryOnly))
                    {
                        string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        if (IsScannableDuplicationPath(rel))
                        {
                            allRelativeFiles.Add(rel);
                        }
                    }
                }
            }

            var seenBlocks = new Dictionary<string, List<(string RelPath, int StartLine, int EndLine, string Preview)>>();

            foreach (string relPath in allRelativeFiles)
            {
                List<(string Norm, int LineNum, string Raw)> linesMeta = new();
                try
                {
                    IEnumerable<string> rawLines;
                    if (reader is not null)
                    {
                        if (!reader.TryReadLines(relPath, out string[] indexedLines))
                        {
                            continue;
                        }

                        rawLines = indexedLines;
                    }
                    else
                    {
                        rawLines = File.ReadLines(Path.Combine(repoRoot, relPath));
                    }

                    int lineIdx = 1;
                    foreach (var rawLine in rawLines)
                    {
                        string norm = NormalizeLine(rawLine);
                        if (!string.IsNullOrEmpty(norm))
                        {
                            linesMeta.Add((norm, lineIdx, rawLine.TrimEnd()));
                        }

                        lineIdx++;
                    }
                }
                catch
                {
                    continue;
                }

                if (linesMeta.Count < windowSize)
                {
                    continue;
                }

                for (int i = 0; i <= linesMeta.Count - windowSize; i++)
                {
                    var window = linesMeta.Skip(i).Take(windowSize).ToList();
                    string blockText = string.Join("\n", window.Select(w => w.Norm));
                    string blockHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(blockText)));

                    int startLine = window[0].LineNum;
                    int endLine = window[^1].LineNum;
                    string preview = string.Join("\n", window.Take(3).Select(w => w.Raw)) + "\n...";

                    if (!seenBlocks.TryGetValue(blockHash, out var list))
                    {
                        list = new List<(string, int, int, string)>();
                        seenBlocks[blockHash] = list;
                    }

                    list.Add((relPath, startLine, endLine, preview));
                }
            }

            var duplicatesFound = new List<DuplicateBlock>();
            var uniqueDupsReported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (blockHash, locs) in seenBlocks)
            {
                if (locs.Count > 1)
                {
                    var filteredLocs = new List<(string RelPath, int StartLine, int EndLine, string Preview)>();
                    foreach (var loc in locs)
                    {
                        bool overlap = filteredLocs.Any(e => e.RelPath == loc.RelPath && Math.Abs(e.StartLine - loc.StartLine) < windowSize);
                        if (!overlap)
                        {
                            filteredLocs.Add(loc);
                        }
                    }

                    if (filteredLocs.Count > 1)
                    {
                        for (int i = 0; i < filteredLocs.Count; i++)
                        {
                            for (int j = i + 1; j < filteredLocs.Count; j++)
                            {
                                var l1 = filteredLocs[i];
                                var l2 = filteredLocs[j];

                                if (string.CompareOrdinal(l1.RelPath, l2.RelPath) > 0)
                                {
                                    (l1, l2) = (l2, l1);
                                }

                                string rel1 = l1.RelPath;
                                string rel2 = l2.RelPath;

                                if (rel1 == rel2)
                                {
                                    continue;
                                }

                                if (targetSet != null && !targetSet.Contains(rel1) && !targetSet.Contains(rel2))
                                {
                                    continue;
                                }

                                string dupKey = $"{rel1}:{l1.StartLine}|{rel2}:{l2.StartLine}";
                                if (uniqueDupsReported.Add(dupKey))
                                {
                                    string cloneKey = BaselineKey(rel1, rel2, blockHash);
                                    duplicatesFound.Add(new DuplicateBlock(
                                        (rel1, l1.StartLine, l1.EndLine, l1.Preview),
                                        (rel2, l2.StartLine, l2.EndLine, l2.Preview),
                                        cloneKey));
                                }
                            }
                        }
                    }
                }
            }

            if (!preCommit)
            {
                var dupsArray = new JsonArray();
                foreach (var d in duplicatesFound)
                {
                    var item = new JsonObject
                    {
                        ["loc1"] = new JsonObject { ["file"] = d.Loc1.File, ["start"] = d.Loc1.Start, ["end"] = d.Loc1.End },
                        ["loc2"] = new JsonObject { ["file"] = d.Loc2.File, ["start"] = d.Loc2.Start, ["end"] = d.Loc2.End },
                        ["key"] = d.Key,
                        ["preview"] = d.Loc1.Preview
                    };
                    dupsArray.Add((JsonNode)item);
                }

                var summaryObj = new JsonObject
                {
                    ["duplicates"] = dupsArray
                };

                try
                {
                    string summaryPath = Path.Combine(repoRoot, "duplication_summary.json");
                    using var stream = File.Create(summaryPath);
                    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                    {
                        summaryObj.WriteTo(writer);
                    }

                    stream.WriteByte((byte)'\n');
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Could not write duplication summary: {ex.Message}");
                }
            }

            // The pre-commit gate passes every staged file; like the full scan, unbaselined duplicates across C#, JavaScript, TypeScript, Vue, and CSS files fail.
            var allBaselineEntries = ReadBaselineEntries(repoRoot, reader);
            List<string> scopedBaseline = targetSet is null
                ? allBaselineEntries
                : allBaselineEntries.Where(entry => IsEntryInScope(entry, targetSet)).ToList();

            var enforcedDuplicates = duplicatesFound
                .Where(d => IsEnforcedDuplicationFile(d.Loc1.File) || IsEnforcedDuplicationFile(d.Loc2.File))
                .ToList();

            var (newDuplicates, staleEntries) = CompareWithBaseline(enforcedDuplicates, scopedBaseline);

            int prunedCount = 0;
            if (updateBaseline && staleEntries.Count > 0)
            {
                prunedCount = SubtractStaleEntries(repoRoot, allBaselineEntries, staleEntries);
                if (prunedCount > 0)
                {
                    Console.WriteLine($"✔ Pruned {prunedCount} stale duplication baseline entr{(prunedCount == 1 ? "y" : "ies")} from '{BaselineFileName}'.");
                }

                staleEntries = [];
            }

            foreach (var dup in newDuplicates)
            {
                Console.WriteLine($"[DUPLICATION VIOLATION] {dup.Loc1.File}:{dup.Loc1.Start}-{dup.Loc1.End} <-> {dup.Loc2.File}:{dup.Loc2.Start}-{dup.Loc2.End} ({dup.Key})");
            }

            foreach (string stale in staleEntries)
            {
                Console.WriteLine($"[DUPLICATION STALE BASELINE] {stale}");
            }

            if (newDuplicates.Count > 0 || staleEntries.Count > 0)
            {
                if (newDuplicates.Count > 0)
                {
                    Console.WriteLine($"[ERROR] Code duplication detected above threshold ({newDuplicates.Count} unbaselined duplicate(s) found).");
                }

                if (staleEntries.Count > 0)
                {
                    Console.WriteLine($"[ERROR] {staleEntries.Count} '{BaselineFileName}' baseline entr{(staleEntries.Count == 1 ? "y" : "ies")} no longer match; baseline may only shrink. Run `CADENCE_UPDATE_BASELINE=1 pnpm cadence check duplication` to drop them.");
                }

                return 1;
            }

            int remainingScopedBaseline = scopedBaseline.Count - prunedCount;
            Console.WriteLine($"✔ Code duplication check passed cleanly ({remainingScopedBaseline} baselined clone window(s)).");
            return 0;
        }
    }

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

        File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

    private static bool IsAmbientTypeScriptDeclaration(string path) =>
        AmbientDeclarationSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool HasSupportedDuplicationExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    private static bool IsEnforcedDuplicationFile(string relativePath) =>
        !IsAmbientTypeScriptDeclaration(relativePath) && HasSupportedDuplicationExtension(relativePath);

    private static bool IsScannableDuplicationPath(string relativePath)
    {
        string norm = "/" + relativePath.Replace('\\', '/').TrimStart('/');
        bool inScannedDirectory = ScannedRootDirectories.Any(
            dir => norm.StartsWith($"/{dir}/", StringComparison.OrdinalIgnoreCase));
        bool isRootModule =
            !norm.AsSpan(1).Contains('/') &&
            RootModuleExtensions.Contains(Path.GetExtension(norm));

        if (!inScannedDirectory && !isRootModule)
        {
            return false;
        }

        if (ExcludedPathPrefixes.Any(prefix => norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (IgnoredPathKeywords.Any(k => norm.Contains($"/{k}/", StringComparison.Ordinal) || norm.EndsWith($"/{k}", StringComparison.Ordinal)))
        {
            return false;
        }

        if (norm.Contains("/generated/", StringComparison.OrdinalIgnoreCase) ||
            IsAmbientTypeScriptDeclaration(norm) ||
            norm.Equals("/apps/web/src/api/types.ts", StringComparison.OrdinalIgnoreCase) ||
            norm.Equals("/packages/ui/src/types/cdash.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Python files are porting references, not live code (#4789), so they are not scanned.
        return HasSupportedDuplicationExtension(norm);
    }

    private static string NormalizeLine(string line)
    {
        string cleaned = line.Trim();

        cleaned = Regex.Replace(cleaned, @"https?://[^\s""';`()\[\]{}]+", "http-url-placeholder");
        cleaned = Regex.Replace(cleaned, @"/\*.*?\*/", "").Trim();

        if (cleaned.StartsWith("/*", StringComparison.Ordinal) || cleaned.StartsWith("*/", StringComparison.Ordinal) || cleaned.StartsWith("* ", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (cleaned.StartsWith('#') || cleaned.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (cleaned.Contains('#'))
        {
            cleaned = cleaned.Split('#', 2)[0].Trim();
        }
        if (cleaned.Contains("//", StringComparison.Ordinal))
        {
            cleaned = cleaned.Split("//", 2)[0].Trim();
        }

        cleaned = cleaned.Replace('\'', '"').Replace('`', '"');

        if (cleaned.StartsWith("import ", StringComparison.Ordinal) ||
            cleaned.StartsWith("from ", StringComparison.Ordinal) ||
            cleaned.StartsWith("export ", StringComparison.Ordinal) ||
            cleaned.StartsWith("using ", StringComparison.Ordinal) ||
            cleaned.StartsWith("global using ", StringComparison.Ordinal) ||
            cleaned.StartsWith("namespace ", StringComparison.Ordinal) ||
            cleaned.StartsWith("const {", StringComparison.Ordinal) ||
            cleaned == "}" ||
            cleaned == "{" ||
            cleaned == "};" ||
            cleaned == "]," ||
            cleaned == "[" ||
            cleaned == "]" ||
            string.IsNullOrEmpty(cleaned))
        {
            return string.Empty;
        }

        return cleaned;
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

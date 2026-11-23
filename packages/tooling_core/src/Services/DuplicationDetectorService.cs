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
    public const string BaselineFile = "code-duplication.baseline.txt";

    public sealed record DuplicateBlockPair(
        (string File, int Start, int End, string Preview) Loc1,
        (string File, int Start, int End, string Preview) Loc2,
        string Key);

    private static readonly string[] IgnoredPathKeywords =
    [
        "node_modules", "tests", ".venv", ".cadence", "__pycache__", "dist", "build", "bin", "obj", "Migrations", "migrations", ".git", "mockServiceWorker.js"
    ];

    // The service hosts share their startup wiring by design until they merge into the kernel host (ADR-2244),
    // and clinician/participant stylesheets standardize identical :root semantic token aliases by design (ADR-2635).
    private static readonly HashSet<string>[] ExemptedPairs =
    [
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apps/execution/src/Program.cs", "apps/designer/src/Program.cs", "apps/gateway/src/Program.cs" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apps/web/src/style.css", "apps/subject-portal/src/style.css" }
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

        bool fullScan = targetFiles is null && !preCommit;
        bool updateBaseline = fullScan &&
                              string.Equals(Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            int windowSize = 15;
            var allRelativeSet = new SortedSet<string>(StringComparer.Ordinal);

            HashSet<string>? targetSet = null;
            if (targetFiles is not null)
            {
                targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in targetFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (!string.IsNullOrEmpty(rel) &&
                        IsScannableDuplicationPath(rel) &&
                        (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
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
                foreach (string rel in reader.Paths.Where(IsScannableDuplicationPath))
                {
                    allRelativeSet.Add(rel);
                }
            }
            else if (GitRevisionScopeService.TryGetTrackedFiles(repoRoot) is { } trackedFiles)
            {
                foreach (string raw in trackedFiles)
                {
                    string rel = raw.Replace('\\', '/');
                    if (IsScannableDuplicationPath(rel) && File.Exists(Path.Combine(repoRoot, rel)))
                    {
                        allRelativeSet.Add(rel);
                    }
                }

                if (targetSet is not null)
                {
                    foreach (string rel in targetSet)
                    {
                        allRelativeSet.Add(rel);
                    }
                }
            }
            else
            {
                var scanDirs = new[] { Path.Combine(repoRoot, "apps"), Path.Combine(repoRoot, "packages") };
                foreach (var dir in scanDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        if (IsScannableDuplicationPath(rel))
                        {
                            allRelativeSet.Add(rel);
                        }
                    }
                }
            }

            var allRelativeFiles = allRelativeSet.ToList();

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
                    string blockHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blockText)));

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

            var duplicatesFound = new List<DuplicateBlockPair>();
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

                                var pairSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rel1, rel2 };
                                if (ExemptedPairs.Any(ex => pairSet.IsSubsetOf(ex)))
                                {
                                    continue;
                                }

                                string dupKey = string.Join("|", new[] { $"{rel1}:{l1.StartLine}", $"{rel2}:{l2.StartLine}" }.OrderBy(x => x, StringComparer.Ordinal));
                                if (uniqueDupsReported.Add(dupKey))
                                {
                                    duplicatesFound.Add(new DuplicateBlockPair(
                                        (rel1, l1.StartLine, l1.EndLine, l1.Preview),
                                        (rel2, l2.StartLine, l2.EndLine, l2.Preview),
                                        BaselineKey(rel1, rel2, blockHash)));
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

            string baselinePath = Path.Combine(repoRoot, BaselineFile);
            if (updateBaseline)
            {
                WriteBaseline(baselinePath, duplicatesFound.Select(d => d.Key));
                Console.WriteLine($"✔ Wrote {duplicatesFound.Count} entries to {BaselineFile} ({allRelativeFiles.Count} files scanned).");
                return 0;
            }

            var allBaseline = ReadBaseline(baselinePath, reader);
            List<string> scopedBaseline = targetSet is null
                ? allBaseline
                : allBaseline.Where(entry => EntryMatchesTargetSet(entry, targetSet)).ToList();

            var (newDuplicates, staleEntries) = CompareWithBaseline(duplicatesFound, scopedBaseline);

            foreach (var dup in newDuplicates.Take(25))
            {
                Console.WriteLine($"[DUPLICATION] {dup.Loc1.File}:{dup.Loc1.Start}-{dup.Loc1.End} <-> {dup.Loc2.File}:{dup.Loc2.Start}-{dup.Loc2.End}");
            }

            if (newDuplicates.Count > 25)
            {
                Console.WriteLine($"[DUPLICATION] ... and {newDuplicates.Count - 25} more unbaselined duplicate block(s).");
            }

            foreach (string stale in staleEntries.Take(25))
            {
                Console.WriteLine($"[DUPLICATION STALE BASELINE] {stale}");
            }

            if (staleEntries.Count > 25)
            {
                Console.WriteLine($"[DUPLICATION STALE BASELINE] ... and {staleEntries.Count - 25} more stale entries.");
            }

            if (newDuplicates.Count > 0 || staleEntries.Count > 0)
            {
                if (newDuplicates.Count > 0)
                {
                    Console.WriteLine($"[ERROR] Code duplication detected above baseline ({newDuplicates.Count} unbaselined duplicate block(s) found; {BaselineFile} may only shrink).");
                }

                if (staleEntries.Count > 0)
                {
                    Console.WriteLine($"[ERROR] {staleEntries.Count} {BaselineFile} entr(y/ies) no longer match; the baseline may only shrink. Run 'CADENCE_UPDATE_BASELINE=1 pnpm cadence check duplication' to drop them.");
                }

                return 1;
            }

            Console.WriteLine($"✔ Code duplication check passed cleanly ({allRelativeFiles.Count} files scanned; {scopedBaseline.Count} baselined blocks).");
            return 0;
        }
    }

    public static string BaselineKey(string file1, string file2, string blockHash)
    {
        (string minFile, string maxFile) = string.CompareOrdinal(file1, file2) <= 0
            ? (file1, file2)
            : (file2, file1);
        return $"{minFile}|{maxFile}|{blockHash.ToLowerInvariant()[..16]}";
    }

    public static (IReadOnlyList<DuplicateBlockPair> NewDuplicates, IReadOnlyList<string> StaleEntries) CompareWithBaseline(
        IReadOnlyList<DuplicateBlockPair> duplicates,
        IReadOnlyList<string> baseline)
    {
        ArgumentNullException.ThrowIfNull(duplicates);
        ArgumentNullException.ThrowIfNull(baseline);

        var remaining = baseline
            .GroupBy(k => k, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newDuplicates = new List<DuplicateBlockPair>();

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

    public static bool IsScannableDuplicationPath(string relativePath)
    {
        string norm = "/" + relativePath.Replace('\\', '/').TrimStart('/');
        if (!norm.StartsWith("/apps/", StringComparison.OrdinalIgnoreCase) &&
            !norm.StartsWith("/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (norm.StartsWith("/apps/web/src/api/generated/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("/apps/web/src/canvas/generated/", StringComparison.OrdinalIgnoreCase) ||
            norm.Equals("/packages/demo-fixtures/src/manifest.d.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IgnoredPathKeywords.Any(k => norm.Contains($"/{k}/", StringComparison.Ordinal) || norm.EndsWith($"/{k}", StringComparison.Ordinal)))
        {
            return false;
        }

        // Python files are porting references, not live code (#4789), so they are not scanned.
        return norm.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EntryMatchesTargetSet(string entry, HashSet<string> targetSet)
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

        string file1 = entry[..firstPipe];
        string file2 = entry[(firstPipe + 1)..secondPipe];
        return targetSet.Contains(file1) || targetSet.Contains(file2);
    }

    private static List<string> ReadBaseline(string baselinePath, GitIndexReader? reader)
    {
        if (reader is not null)
        {
            return reader.TryReadLines(BaselineFile, out string[] indexedLines)
                ? [.. indexedLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))]
                : [];
        }

        return File.Exists(baselinePath)
            ? [.. File.ReadAllLines(baselinePath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))]
            : [];
    }

    private static void WriteBaseline(string baselinePath, IEnumerable<string> keys)
    {
        var lines = new List<string>
        {
            "# Code duplication ratchet baseline (pnpm cadence check duplication). Pre-existing frontend and TypeScript clones frozen behind a shrink-only baseline.",
            "# One per line: minFile|maxFile|first 16 hex digits of SHA-256(normalized 15-line block). Sorted ordinally. The list may only shrink.",
            "# Regenerate with CADENCE_UPDATE_BASELINE=1 pnpm cadence check duplication (docs/cli.md).",
        };
        lines.AddRange(keys.Order(StringComparer.Ordinal));
        File.WriteAllText(baselinePath, string.Join('\n', lines) + "\n");
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

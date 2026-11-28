using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DuplicationDetectorService
{
    public const string BaselineFileName = ".duplication-baseline.txt";

    public sealed record DuplicateBlock(
        (string File, int Start, int End, string Preview) Loc1,
        (string File, int Start, int End, string Preview) Loc2,
        string Key);

    [GeneratedRegex(@"https?://[^\s""';`()\[\]{}]+")]
    private static partial Regex HttpUrlRegex();

    [GeneratedRegex(@"/\*.*?\*/")]
    private static partial Regex InlineBlockCommentRegex();

    public static int DetectDuplication(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
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
                var scanDirs = new[]
                {
                    Path.Combine(repoRoot, "apps"),
                    Path.Combine(repoRoot, "packages"),
                    Path.Combine(repoRoot, "scripts"),
                    Path.Combine(repoRoot, "docs"),
                    Path.Combine(repoRoot, ".claude"),
                };
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
                            allRelativeFiles.Add(rel);
                        }
                    }
                }

                if (Directory.Exists(repoRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.TopDirectoryOnly))
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

                                int pathCmp = string.CompareOrdinal(l1.RelPath, l2.RelPath);
                                if (pathCmp > 0 || (pathCmp == 0 && l1.StartLine > l2.StartLine))
                                {
                                    (l1, l2) = (l2, l1);
                                }

                                string rel1 = l1.RelPath;
                                string rel2 = l2.RelPath;

                                if (rel1 == rel2 && !IsIntraFileDuplicationEnforced(rel1))
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

    private static string NormalizeLine(string line)
    {
        string cleaned = line.Trim();

        cleaned = HttpUrlRegex().Replace(cleaned, "http-url-placeholder");
        cleaned = InlineBlockCommentRegex().Replace(cleaned, "").Trim();

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
}

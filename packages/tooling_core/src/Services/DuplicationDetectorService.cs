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
    private static readonly string[] IgnoredPathKeywords =
    [
        "node_modules", "tests", ".venv", "__pycache__", "dist", "build", "bin", "obj", "Migrations", "migrations", ".git"
    ];

    // The service hosts share their startup wiring by design until they merge into the kernel host (ADR-2244).
    private static readonly HashSet<string>[] ExemptedPairs =
    [
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apps/execution/src/Program.cs", "apps/designer/src/Program.cs", "apps/gateway/src/Program.cs" }
    ];

    public static int DetectDuplication(string[]? targetFiles = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Scanning codebase for duplicated code blocks (>15 lines)...");

        int windowSize = 15;
        var scanDirs = new[] { Path.Combine(repoRoot, "apps"), Path.Combine(repoRoot, "packages") };
        var allFiles = new List<string>();

        HashSet<string>? targetSet = targetFiles != null && targetFiles.Length > 0
            ? new HashSet<string>(targetFiles.Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/')), StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var dir in scanDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string norm = file.Replace('\\', '/');
                if (IgnoredPathKeywords.Any(k => norm.Contains($"/{k}/", StringComparison.Ordinal) || norm.Contains($"/{k}", StringComparison.Ordinal))) continue;

                // Python files are porting references, not live code (#4789), so they are not scanned.
                if (norm.EndsWith(".js", StringComparison.Ordinal) || norm.EndsWith(".vue", StringComparison.Ordinal) || norm.EndsWith(".css", StringComparison.Ordinal) || norm.EndsWith(".cs", StringComparison.Ordinal))
                {
                    allFiles.Add(file);
                }
            }
        }

        var seenBlocks = new Dictionary<string, List<(string FilePath, int StartLine, int EndLine, string Preview)>>();

        foreach (var filePath in allFiles)
        {
            List<(string Norm, int LineNum, string Raw)> linesMeta = new();
            try
            {
                int lineIdx = 1;
                foreach (var rawLine in File.ReadLines(filePath))
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

            if (linesMeta.Count < windowSize) continue;

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
                list.Add((filePath, startLine, endLine, preview));
            }
        }

        var duplicatesFound = new List<((string File, int Start, int End, string Preview) Loc1, (string File, int Start, int End, string Preview) Loc2)>();
        var uniqueDupsReported = new HashSet<string>();

        foreach (var locs in seenBlocks.Values)
        {
            if (locs.Count > 1)
            {
                var filteredLocs = new List<(string FilePath, int StartLine, int EndLine, string Preview)>();
                foreach (var loc in locs)
                {
                    bool overlap = filteredLocs.Any(e => e.FilePath == loc.FilePath && Math.Abs(e.StartLine - loc.StartLine) < windowSize);
                    if (!overlap) filteredLocs.Add(loc);
                }

                if (filteredLocs.Count > 1)
                {
                    for (int i = 0; i < filteredLocs.Count; i++)
                    {
                        for (int j = i + 1; j < filteredLocs.Count; j++)
                        {
                            var l1 = filteredLocs[i];
                            var l2 = filteredLocs[j];

                            string rel1 = Path.GetRelativePath(repoRoot, l1.FilePath).Replace('\\', '/');
                            string rel2 = Path.GetRelativePath(repoRoot, l2.FilePath).Replace('\\', '/');

                            if (rel1 == rel2) continue;

                            if (targetSet != null && !targetSet.Contains(rel1) && !targetSet.Contains(rel2)) continue;

                            var pairSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rel1, rel2 };
                            if (ExemptedPairs.Any(ex => pairSet.IsSubsetOf(ex))) continue;

                            string dupKey = string.Join("|", new[] { $"{rel1}:{l1.StartLine}", $"{rel2}:{l2.StartLine}" }.OrderBy(x => x));
                            if (uniqueDupsReported.Add(dupKey))
                            {
                                duplicatesFound.Add((
                                    (rel1, l1.StartLine, l1.EndLine, l1.Preview),
                                    (rel2, l2.StartLine, l2.EndLine, l2.Preview)
                                ));
                            }
                        }
                    }
                }
            }
        }

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

        // The pre-commit gate passes every staged file; like the full scan, only duplicates involving a C# file fail.
        bool csDuplicatesFound = duplicatesFound.Any(d => d.Loc1.File.EndsWith(".cs", StringComparison.Ordinal) || d.Loc2.File.EndsWith(".cs", StringComparison.Ordinal));

        if (csDuplicatesFound)
        {
            Console.WriteLine($"[ERROR] Code duplication detected above threshold ({duplicatesFound.Count} duplicates found).");
            return 1;
        }

        Console.WriteLine("✔ Code duplication check passed cleanly.");
        return 0;
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

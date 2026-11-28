using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        ".vitepress",
        "bin",
        "obj",
        ".git",
        ".vs",
        ".venv",
        ".cadence",
        "runs",
        "adr",
    };

    // Case-sensitive: uppercase shard index filenames are excluded from document scanning, while
    // authored lowercase index.md pages inside subdirectories (e.g. blog/index.md) are indexed.
    private static readonly HashSet<string> ExcludedIndexFiles = new(StringComparer.Ordinal)
    {
        "DOCUMENTATION_INDEX.md",
        "ROOT_INDEX.md",
        "INDEX.md",
        "DOC_INDEX.md",
    };

    // VitePress stages these copies from root documents when serving or building the site.
    private static readonly HashSet<string> StagedSitePages = new(StringComparer.OrdinalIgnoreCase)
    {
        "AGENTS.md",
        "ARCHITECTURE.md",
        "CONTEXT.md",
        "CONTRIBUTING.md",
        "LICENSE.md",
        "README.md",
        "SECURITY.md",
        "SUPPORT.md",
        "index.md",
    };

    [GeneratedRegex(@"\]\(([^)\r\n]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();

    /// <summary>
    /// Compares two relative file paths or link targets alphabetically in case-insensitive ASCII order
    /// (folding to lowercase, with exact ordinal tie-breaking).
    /// </summary>
    public static int CompareSortKeys(string a, string b)
    {
        int cmp = string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant());
        return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
    }

    public static int SyncDocIndex(bool check = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");
        string hubPath = Path.Combine(docsDir, "DOCUMENTATION_INDEX.md");

        if (!Directory.Exists(docsDir) || !File.Exists(hubPath))
        {
            Console.WriteLine($"[ERROR] Documentation directory or index file not found at {hubPath}");
            return 1;
        }

        string hubOriginal = File.ReadAllText(hubPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] hubLines = hubOriginal.Split('\n');

        // 1. Scan filesystem for all valid non-ADR, non-draft, non-index markdown files in docs/
        var diskFiles = ScanMarkdownFiles(docsDir);

        // 2. Group disk files by owning category shard
        var docsByShard = new Dictionary<string, List<MarkdownDocInfo>>(StringComparer.Ordinal);
        foreach (var doc in diskFiles)
        {
            string shardRelPath = GetOwningShardRelativePath(docsDir, doc.RelativePath);
            if (!docsByShard.TryGetValue(shardRelPath, out var shardDocs))
            {
                shardDocs = [];
                docsByShard[shardRelPath] = shardDocs;
            }

            shardDocs.Add(doc);
        }

        // 3. Union existing category shard files on disk with shards required by disk documents
        var existingShards = DiscoverExistingShardFiles(docsDir);
        var allShardRelPaths = existingShards
            .Union(docsByShard.Keys, StringComparer.Ordinal)
            .Order(Comparer<string>.Create(CompareSortKeys))
            .ToList();

        // 4. Validate static hub docs/DOCUMENTATION_INDEX.md
        var tableErrors = ValidateMarkdownTableLines(hubLines, "docs/DOCUMENTATION_INDEX.md");
        var sortErrors = ValidateTableRowSorting(hubLines, "docs/DOCUMENTATION_INDEX.md");
        var forbiddenHubAdrRows = FindForbiddenPerAdrRowsInHub(hubLines);
        var hubIndexedDocsTargets = ExtractIndexedDocsRelativeTargets(hubOriginal, docsDir, "DOCUMENTATION_INDEX.md");
        var unlinkedShardsInHub = allShardRelPaths
            .Where(s => !hubIndexedDocsTargets.Contains(s))
            .ToList();

        // 5. Inspect every category shard
        var missingShardFiles = new List<string>();
        var unindexedFiles = new List<string>();
        var driftedShards = new List<string>();
        var shardOriginals = new Dictionary<string, string>(StringComparer.Ordinal);
        var shardMissingDocs = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string shardRelPath in allShardRelPaths)
        {
            string shardFullPath = Path.Combine(docsDir, shardRelPath.Replace('/', Path.DirectorySeparatorChar));
            string shardDisplayPath = $"docs/{shardRelPath}";
            var shardDocs = docsByShard.TryGetValue(shardRelPath, out var list) ? list : [];

            if (!File.Exists(shardFullPath))
            {
                missingShardFiles.Add(shardRelPath);
                var missingPaths = shardDocs.Select(d => d.RelativePath).ToList();
                shardMissingDocs[shardRelPath] = missingPaths;
                unindexedFiles.AddRange(missingPaths);
                continue;
            }

            string shardOriginal = File.ReadAllText(shardFullPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            shardOriginals[shardRelPath] = shardOriginal;
            string[] shardLines = shardOriginal.Split('\n');

            tableErrors.AddRange(ValidateMarkdownTableLines(shardLines, shardDisplayPath));
            sortErrors.AddRange(ValidateTableRowSorting(shardLines, shardDisplayPath));

            var indexedInShard = ExtractIndexedDocsRelativeTargets(shardOriginal, docsDir, shardRelPath);
            var missingInShard = shardDocs
                .Where(d => !indexedInShard.Contains(d.RelativePath))
                .Select(d => d.RelativePath)
                .ToList();

            shardMissingDocs[shardRelPath] = missingInShard;
            unindexedFiles.AddRange(missingInShard);

            string canonicalWithoutNew = GenerateUpdatedShardContent(shardOriginal, shardDocs, [], docsDir, shardRelPath);
            string normalizedShardOriginal = shardOriginal.TrimEnd() + "\n";
            if (!string.Equals(normalizedShardOriginal, canonicalWithoutNew, StringComparison.Ordinal))
            {
                driftedShards.Add(shardRelPath);
            }
        }

        if (check)
        {
            bool failed = false;

            if (tableErrors.Count > 0)
            {
                failed = true;
                Console.WriteLine($"[ERROR] Documentation index shards contain {tableErrors.Count} malformed Markdown table row(s):");
                foreach (var err in tableErrors.Take(20))
                {
                    Console.WriteLine($"  - {err}");
                }

                if (tableErrors.Count > 20)
                {
                    Console.WriteLine($"  ... and {tableErrors.Count - 20} more table error(s).");
                }
            }

            if (forbiddenHubAdrRows.Count > 0)
            {
                failed = true;
                Console.WriteLine($"[ERROR] 'docs/DOCUMENTATION_INDEX.md' must omit per-ADR rows and link only to ADR hub/domain shards ({forbiddenHubAdrRows.Count} violation(s)):");
                foreach (var err in forbiddenHubAdrRows.Take(20))
                {
                    Console.WriteLine($"  - {err}");
                }
            }

            if (sortErrors.Count > 0)
            {
                failed = true;
                Console.WriteLine($"[ERROR] Documentation index shards contain {sortErrors.Count} out-of-order table row(s) (must be sorted alphabetically by file path):");
                foreach (var err in sortErrors.Take(20))
                {
                    Console.WriteLine($"  - {err}");
                }

                if (sortErrors.Count > 20)
                {
                    Console.WriteLine($"  ... and {sortErrors.Count - 20} more sorting error(s).");
                }
            }

            if (missingShardFiles.Count > 0 || unlinkedShardsInHub.Count > 0)
            {
                failed = true;
                foreach (var missingShard in missingShardFiles)
                {
                    Console.WriteLine($"[ERROR] Missing category documentation shard 'docs/{missingShard}'.");
                }

                foreach (var unlinkedShard in unlinkedShardsInHub)
                {
                    Console.WriteLine($"[ERROR] Category shard 'docs/{unlinkedShard}' is not linked in 'docs/DOCUMENTATION_INDEX.md'.");
                }
            }

            if (unindexedFiles.Count > 0)
            {
                failed = true;
                Console.WriteLine($"[ERROR] Category documentation shards are missing entries for {unindexedFiles.Count} markdown file(s):");
                foreach (var unindexed in unindexedFiles.Take(20))
                {
                    Console.WriteLine($"  - docs/{unindexed}");
                }

                if (unindexedFiles.Count > 20)
                {
                    Console.WriteLine($"  ... and {unindexedFiles.Count - 20} more file(s).");
                }
            }

            if (driftedShards.Count > 0)
            {
                failed = true;
                Console.WriteLine($"[ERROR] Category documentation shard(s) have stale metadata, draft entries, or formatting drift: {string.Join(", ", driftedShards.Select(s => $"docs/{s}"))}");
            }

            if (failed)
            {
                Console.WriteLine("\nRun 'pnpm cadence generate-doc-index' to synchronize category documentation shards.");
                return 1;
            }

            Console.WriteLine($"✔ Documentation index parity check passed cleanly ({diskFiles.Count} documents indexed across {allShardRelPaths.Count} category shard(s)).");
            return 0;
        }

        // Write mode: update only shards that need changes, leaving static DOCUMENTATION_INDEX.md untouched
        // whenever all category shards are already linked and valid.
        int updatedShardCount = 0;
        foreach (string shardRelPath in allShardRelPaths)
        {
            string shardFullPath = Path.Combine(docsDir, shardRelPath.Replace('/', Path.DirectorySeparatorChar));
            var shardDocs = docsByShard.TryGetValue(shardRelPath, out var list) ? list : [];
            var missingPaths = shardMissingDocs.TryGetValue(shardRelPath, out var mp) ? mp : [];

            bool existed = shardOriginals.TryGetValue(shardRelPath, out string? existingContent);
            string baseContent = existed ? existingContent! : BuildInitialShardHeader(shardRelPath);
            string updatedShard = GenerateUpdatedShardContent(baseContent, shardDocs, missingPaths, docsDir, shardRelPath);

            if (!existed || !string.Equals(existingContent!.TrimEnd() + "\n", updatedShard, StringComparison.Ordinal) || !string.Equals(existingContent, updatedShard, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
                File.WriteAllText(shardFullPath, updatedShard);
                updatedShardCount++;
            }
        }

        bool hubNeedsRepair =
            unlinkedShardsInHub.Count > 0 ||
            forbiddenHubAdrRows.Count > 0 ||
            ValidateMarkdownTableLines(hubLines, "docs/DOCUMENTATION_INDEX.md").Count > 0 ||
            ValidateTableRowSorting(hubLines, "docs/DOCUMENTATION_INDEX.md").Count > 0;

        bool hubUpdated = false;
        if (hubNeedsRepair)
        {
            string updatedHub = RepairAndSyncHubContent(hubOriginal, unlinkedShardsInHub);
            if (!string.Equals(hubOriginal, updatedHub, StringComparison.Ordinal))
            {
                File.WriteAllText(hubPath, updatedHub);
                hubUpdated = true;
            }
        }

        if (updatedShardCount == 0 && !hubUpdated)
        {
            Console.WriteLine("✔ Documentation index shards are already up to date.");
            return 0;
        }

        Console.WriteLine($"✔ Synchronized {updatedShardCount} category documentation shard(s) ({unindexedFiles.Count} new document(s) indexed).");
        return 0;
    }

    public record MarkdownDocInfo(string RelativePath, string FullPath);

    /// <summary>
    /// Maps a <c>docs/</c>-relative Markdown path (e.g. <c>LOCAL_DEV_ENVIRONMENT.md</c>,
    /// <c>kernel/CLINICAL_KERNEL_SPECIFICATION.md</c>, <c>blog/YYYY-MM-DD-post.md</c>) to its owning
    /// category shard path relative to <c>docs/</c>.
    /// </summary>
    public static string GetOwningShardRelativePath(string docsDir, string docRelPath)
    {
        string normalized = NormalizeRelativePath(docRelPath);
        int slashIdx = normalized.IndexOf('/', StringComparison.Ordinal);
        if (slashIdx < 0)
        {
            return "ROOT_INDEX.md";
        }

        string topDir = normalized[..slashIdx];
        string topDirFull = Path.Combine(docsDir, topDir);
        if (Directory.Exists(topDirFull))
        {
            bool hasLowercasePortalIndex = Directory.EnumerateFiles(topDirFull, "*.md", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Any(name => string.Equals(name, "index.md", StringComparison.Ordinal) ||
                             string.Equals(name, "DOC_INDEX.md", StringComparison.Ordinal));
            if (hasLowercasePortalIndex)
            {
                return $"{topDir}/DOC_INDEX.md";
            }
        }

        return $"{topDir}/INDEX.md";
    }

    private static List<string> DiscoverExistingShardFiles(string docsDir)
    {
        var shards = new List<string>();
        if (!Directory.Exists(docsDir))
        {
            return shards;
        }

        foreach (string rootFile in Directory.EnumerateFiles(docsDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(rootFile), "ROOT_INDEX.md", StringComparison.Ordinal))
            {
                shards.Add("ROOT_INDEX.md");
            }
        }

        foreach (string subDir in Directory.GetDirectories(docsDir))
        {
            string subName = Path.GetFileName(subDir);
            if (ExcludedDirectories.Contains(subName))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(subDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "INDEX.md", StringComparison.Ordinal) ||
                    string.Equals(fileName, "DOC_INDEX.md", StringComparison.Ordinal))
                {
                    shards.Add($"{subName}/{fileName}");
                }
            }
        }

        return shards;
    }

    public static List<MarkdownDocInfo> ScanMarkdownFiles(string docsDir)
    {
        var result = new List<MarkdownDocInfo>();
        ScanDirRecursive(docsDir, docsDir, result);
        return [.. result.OrderBy(f => f.RelativePath, Comparer<string>.Create(CompareSortKeys))];
    }

    private static void ScanDirRecursive(string currentDir, string docsDir, List<MarkdownDocInfo> result)
    {
        var dirName = Path.GetFileName(currentDir);
        if (!string.Equals(currentDir, docsDir, StringComparison.OrdinalIgnoreCase) &&
            ExcludedDirectories.Contains(dirName))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(currentDir, "*.md"))
        {
            string fileName = Path.GetFileName(file);
            string relPath = Path.GetRelativePath(docsDir, file).Replace('\\', '/');
            if (ExcludedIndexFiles.Contains(fileName) || StagedSitePages.Contains(relPath))
            {
                continue;
            }

            var fi = new FileInfo(file);
            if (fi.Length == 0)
            {
                continue;
            }

            if (IsDatedBlogPostPath(relPath) && IsDraftMarkdownFile(file))
            {
                continue;
            }

            result.Add(new MarkdownDocInfo(relPath, file));
        }

        foreach (var subDir in Directory.GetDirectories(currentDir))
        {
            string subName = Path.GetFileName(subDir);
            if (!ExcludedDirectories.Contains(subName))
            {
                ScanDirRecursive(subDir, docsDir, result);
            }
        }
    }

    public static HashSet<string> ExtractIndexedTargets(string content)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            string target = match.Groups[1].Value;
            int anchorIdx = target.IndexOf('#');
            if (anchorIdx >= 0)
            {
                target = target[..anchorIdx];
            }

            target = target.Trim();
            if (string.IsNullOrEmpty(target) || target.StartsWith("http://") || target.StartsWith("https://") || target.StartsWith("mailto:"))
            {
                continue;
            }

            string norm = NormalizePath(target);
            targets.Add(norm);
            targets.Add(target);
        }

        return targets;
    }

    private static HashSet<string> ExtractIndexedDocsRelativeTargets(string content, string docsDir, string shardRelPath)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string shardFullPath = Path.Combine(docsDir, shardRelPath.Replace('/', Path.DirectorySeparatorChar));
        string shardDirFull = Path.GetDirectoryName(shardFullPath) ?? docsDir;
        string normalizedDocsRoot = Path.GetFullPath(docsDir);

        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            string target = match.Groups[1].Value;
            int anchorIdx = target.IndexOf('#');
            if (anchorIdx >= 0)
            {
                target = target[..anchorIdx];
            }

            target = target.Trim();
            if (string.IsNullOrEmpty(target) ||
                target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string resolvedFull = Path.GetFullPath(Path.Combine(shardDirFull, target.Replace('/', Path.DirectorySeparatorChar)));
            string relToDocs = Path.GetRelativePath(normalizedDocsRoot, resolvedFull).Replace('\\', '/');
            if (!relToDocs.StartsWith("..", StringComparison.Ordinal))
            {
                targets.Add(relToDocs);
            }
        }

        return targets;
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (path.StartsWith("./"))
        {
            path = path[2..];
        }

        while (path.StartsWith("../"))
        {
            path = path[3..];
        }

        return path;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Markdown;

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

    // Inline links are reduced to their text in descriptions: their targets are relative to the source
    // document, not to the index, and truncation can cut a link in half.
    [GeneratedRegex(@"\[([^\]\r\n]*)\]\([^)\r\n]*\)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})")]
    private static partial Regex IsoDatePrefixRegex();

    [GeneratedRegex(@"^\s*(?:[-*]\s*)?(?:\*\*)?Status:?(?:\*\*)?:?\s*(?:\*\*)?Superseded\b|^\s*>\s*(?:\[!(?:WARNING|IMPORTANT|NOTE)\]\s*)?(?:\*\*)?Superseded\b|^\s*Superseded\s+by\b|^\s*##\s+Status\s*\r?\n+\s*Superseded\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AdrSupersededRegex();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-")]
    private static partial Regex AdrDatePrefixRegex();

    [GeneratedRegex(@"^#\s+.+$", RegexOptions.Multiline)]
    private static partial Regex FirstMarkdownHeadingRegex();

    [GeneratedRegex(@"\b(?:decommission(?:ing|ed)?|retire(?:d|ment)?|migration\s+to\s+\.?net\s*10)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetMigrationOrRetirementHeadingRegex();

    [GeneratedRegex(@"\b(?:python|fastapi|pydantic|sqlalchemy|sqlmodel|alembic|pytest|uvicorn|starlette|asgi|ruff|uv|mypy|poetry|hatch|celery|neo4j|cypher|apoc)\b|\.py\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreKernelPythonOrNeo4jRegex();

    private const string ClinicalKernelInceptionDate = "2026-09-29"; // deid-ignore: architectural milestone date, not PHI

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();

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

    public static bool IsDraftMarkdownFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            string[] lines = File.ReadAllLines(fullPath);
            var parsed = YamlFrontmatterParser.Parse(lines);
            return parsed.HasFrontmatter && parsed.IsDraft;
        }
        catch (IOException)
        {
            return false;
        }
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

    private static bool IsDatedBlogPostPath(string relPath)
    {
        string normalized = NormalizePath(relPath);
        if (normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..];
        }

        return normalized.StartsWith("blog/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("blog/DOC_INDEX.md", StringComparison.Ordinal);
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

    public static string DetermineCategory(string relPath)
    {
        if (relPath.StartsWith("blog/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("docs/blog/", StringComparison.OrdinalIgnoreCase)) return "## Engineering blog";
        if (relPath.StartsWith("architecture/repository-migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("architecture/", StringComparison.OrdinalIgnoreCase) && relPath.Contains("repository-migration", StringComparison.OrdinalIgnoreCase)) return "## Former repository migration program";
        if (relPath.StartsWith("kernel/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("architecture/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("ai/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("ux/", StringComparison.OrdinalIgnoreCase)) return "## Kernel and roadmap";
        if (relPath.StartsWith("adr/", StringComparison.OrdinalIgnoreCase)) return "## Architecture decision records";
        if (relPath.StartsWith("SDLC/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("SRS.md", StringComparison.OrdinalIgnoreCase)) return "## Requirements, validation and GxP";
        if (relPath.StartsWith("migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("PYTHON_REFERENCE_INVENTORY.md", StringComparison.OrdinalIgnoreCase)) return "## Python-to-.NET port (completed)";
        if (relPath.StartsWith("specs/", StringComparison.OrdinalIgnoreCase)) return "## Design proposals";
        if (relPath.StartsWith("CDISC/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("NCI/", StringComparison.OrdinalIgnoreCase)) return "## Standards reference material";
        if (relPath.StartsWith("agents/", StringComparison.OrdinalIgnoreCase)) return "## Repository policies";
        if (relPath.StartsWith("strategy/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("FUNCTIONAL_PRODUCT_SPECIFICATION.md", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("FEATURE_MATRIX.md", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md", StringComparison.OrdinalIgnoreCase)) return "## Product";

        return "## Developing and running";
    }

    public static string DetermineStatus(string relPath, string fileContent)
    {
        if (relPath.StartsWith("adr/", StringComparison.OrdinalIgnoreCase))
        {
            return IsHistoricalAdr(relPath, fileContent) ? "Historical" : "Current";
        }
        if (relPath.StartsWith("migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("architecture/repository-migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("PYTHON_REFERENCE_INVENTORY.md", StringComparison.OrdinalIgnoreCase)) return "Historical";
        if (relPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase) || relPath.StartsWith("schema/", StringComparison.OrdinalIgnoreCase)) return "Generated";
        if (relPath.StartsWith("SDLC/", StringComparison.OrdinalIgnoreCase))
        {
            if (fileContent.Contains("Baseline", StringComparison.OrdinalIgnoreCase) || fileContent.Contains("Superseded", StringComparison.OrdinalIgnoreCase)) return "Baseline";
            return "Current";
        }
        return "Current";
    }

    public static string ExtractBlogPostDate(string fullPath, string relPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                string[] lines = File.ReadAllLines(fullPath);
                var parsed = YamlFrontmatterParser.Parse(lines);
                if (parsed.HasFrontmatter &&
                    parsed.Scalars.TryGetValue("date", out string? rawDate) &&
                    !string.IsNullOrWhiteSpace(rawDate))
                {
                    Match dm = IsoDatePrefixRegex().Match(rawDate.Trim());
                    if (dm.Success)
                    {
                        return dm.Groups[1].Value;
                    }
                }
            }
        }
        catch (IOException)
        {
            // Fall through to filename fallback
        }

        string fileName = Path.GetFileNameWithoutExtension(relPath);
        Match slugMatch = IsoDatePrefixRegex().Match(fileName);
        return slugMatch.Success ? slugMatch.Groups[1].Value : string.Empty;
    }

    private static bool IsHistoricalAdr(string relPath, string fileContent)
    {
        string normalizedPath = relPath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalizedPath);
        if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("legacy-index.md", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        if (AdrSupersededRegex().IsMatch(fileContent))
        {
            return true;
        }

        Match dateMatch = AdrDatePrefixRegex().Match(fileName);
        if (dateMatch.Success &&
            string.CompareOrdinal(dateMatch.Groups[1].Value, ClinicalKernelInceptionDate) >= 0)
        {
            return false;
        }

        Match headingMatch = FirstMarkdownHeadingRegex().Match(fileContent);
        if (headingMatch.Success &&
            DotNetMigrationOrRetirementHeadingRegex().IsMatch(headingMatch.Value))
        {
            return false;
        }

        return PreKernelPythonOrNeo4jRegex().IsMatch(fileContent);
    }

    public static (string Title, string Description) ExtractDocMetadata(string fullPath, string relPath)
    {
        string title = string.Empty;
        string description = string.Empty;
        string frontmatterTitle = string.Empty;
        string frontmatterSummary = string.Empty;

        try
        {
            string[] lines = File.ReadAllLines(fullPath);
            var parsedFm = YamlFrontmatterParser.Parse(lines);
            int startIndex = parsedFm.HasFrontmatter ? parsedFm.BodyStartLineIndex : 0;

            if (parsedFm.HasFrontmatter)
            {
                if (parsedFm.Scalars.TryGetValue("title", out string? rawTitle) && !string.IsNullOrWhiteSpace(rawTitle))
                {
                    frontmatterTitle = CollapseWhitespace(rawTitle);
                }

                if (parsedFm.Scalars.TryGetValue("summary", out string? rawSummary) && !string.IsNullOrWhiteSpace(rawSummary))
                {
                    frontmatterSummary = CollapseWhitespace(rawSummary);
                }
                else if (parsedFm.Scalars.TryGetValue("description", out string? rawDesc) && !string.IsNullOrWhiteSpace(rawDesc))
                {
                    frontmatterSummary = CollapseWhitespace(rawDesc);
                }
            }

            if (!string.IsNullOrWhiteSpace(frontmatterTitle))
            {
                title = frontmatterTitle;
            }

            if (!string.IsNullOrWhiteSpace(frontmatterSummary))
            {
                string cleanedFm = SanitizeDescriptionCandidate(frontmatterSummary);
                if (!string.IsNullOrWhiteSpace(cleanedFm))
                {
                    description = TruncateTableCellDescription(cleanedFm);
                }
            }

            bool inHtmlComment = false;
            for (int i = startIndex; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (inHtmlComment)
                {
                    if (trimmed.Contains("-->", StringComparison.Ordinal))
                    {
                        inHtmlComment = false;
                    }
                    continue;
                }

                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    if (!trimmed.Contains("-->", StringComparison.Ordinal))
                    {
                        inHtmlComment = true;
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(title) && trimmed.StartsWith("# "))
                {
                    title = trimmed[2..].Trim();
                }
                else if (!string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description) &&
                         !string.IsNullOrWhiteSpace(trimmed) &&
                         !trimmed.StartsWith('#') &&
                         !trimmed.StartsWith("---") &&
                         !trimmed.StartsWith('|') &&
                         !trimmed.StartsWith('<') &&
                         !trimmed.StartsWith("```"))
                {
                    string cleaned = SanitizeDescriptionCandidate(trimmed);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        description = TruncateTableCellDescription(cleaned);
                        break;
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        if (string.IsNullOrEmpty(title))
        {
            title = Path.GetFileNameWithoutExtension(fullPath).Replace('-', ' ').Replace('_', ' ');
            title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
        }

        if (string.IsNullOrEmpty(description))
        {
            description = title;
        }

        title = title.Replace("`", "").Trim();
        return (title, description);
    }

    private static string CollapseWhitespace(string input) =>
        WhitespaceRegex().Replace(input, " ").Trim();

    private static string SanitizeDescriptionCandidate(string raw)
    {
        string cleaned = HtmlCommentRegex().Replace(raw, " ");
        int danglingCommentIdx = cleaned.IndexOf("<!--", StringComparison.Ordinal);
        if (danglingCommentIdx >= 0)
        {
            cleaned = cleaned[..danglingCommentIdx];
        }

        cleaned = cleaned
            .Replace("> [!WARNING]", "")
            .Replace("> [!NOTE]", "")
            .Replace("> [!IMPORTANT]", "")
            .Replace("> [!TIP]", "")
            .Replace(">", "")
            .Replace("**", "");
        cleaned = InlineLinkRegex().Replace(cleaned, "$1");
        return CollapseWhitespace(cleaned);
    }

    public static string TruncateTableCellDescription(string cleaned, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (cleaned.Length <= maxLength)
        {
            return BalanceInlineBackticks(cleaned, appendEllipsis: false, maxLength);
        }

        int cutLength = Math.Max(1, maxLength - 3);
        string prefix = cleaned[..cutLength].TrimEnd('\\');
        return BalanceInlineBackticks(prefix, appendEllipsis: true, maxLength);
    }

    private static string BalanceInlineBackticks(string text, bool appendEllipsis, int maxLength)
    {
        int unclosedOpenerPos = FindUnclosedBacktickRun(text, out int unclosedLen);
        if (unclosedOpenerPos < 0)
        {
            return appendEllipsis ? text + "..." : text;
        }

        string beforeOpener = text[..unclosedOpenerPos];
        string insideSpan = text[(unclosedOpenerPos + unclosedLen)..];
        if (string.IsNullOrWhiteSpace(insideSpan))
        {
            string trimmed = beforeOpener.TrimEnd();
            return appendEllipsis ? trimmed + "..." : trimmed;
        }

        string closer = new('`', unclosedLen);
        if (appendEllipsis)
        {
            int maxPrefixBeforeEllipsis = Math.Max(unclosedOpenerPos + unclosedLen + 1, maxLength - 3 - unclosedLen);
            string bounded = text.Length > maxPrefixBeforeEllipsis
                ? text[..maxPrefixBeforeEllipsis].TrimEnd('\\')
                : text;
            if (bounded.Length <= unclosedOpenerPos + unclosedLen)
            {
                return beforeOpener.TrimEnd() + "...";
            }

            return bounded + "..." + closer;
        }

        return text + closer;
    }

    public static int FindUnclosedBacktickRun(string line, out int unclosedLen)
    {
        int pos = 0;
        while (pos < line.Length)
        {
            if (line[pos] == '\\' && pos + 1 < line.Length)
            {
                pos += 2;
                continue;
            }

            if (line[pos] != '`')
            {
                pos++;
                continue;
            }

            int runLen = 0;
            while (pos + runLen < line.Length && line[pos + runLen] == '`')
            {
                runLen++;
            }

            int search = pos + runLen;
            int matchedClose = -1;
            while (search < line.Length)
            {
                if (line[search] == '\\' && search + 1 < line.Length)
                {
                    search += 2;
                    continue;
                }

                if (line[search] != '`')
                {
                    search++;
                    continue;
                }

                int candidateLen = 0;
                while (search + candidateLen < line.Length && line[search + candidateLen] == '`')
                {
                    candidateLen++;
                }

                if (candidateLen == runLen)
                {
                    matchedClose = search;
                    break;
                }

                search += candidateLen;
            }

            if (matchedClose >= 0)
            {
                pos = matchedClose + runLen;
                continue;
            }

            if (runLen == 1)
            {
                unclosedLen = runLen;
                return pos;
            }

            int nextPipe = line.IndexOf('|', pos + runLen);
            string remainderInCell = nextPipe >= 0
                ? line[(pos + runLen)..nextPipe]
                : line[(pos + runLen)..];
            if (!string.IsNullOrWhiteSpace(remainderInCell))
            {
                unclosedLen = runLen;
                return pos;
            }

            pos += runLen;
        }

        unclosedLen = 0;
        return -1;
    }

    public static List<string> ValidateMarkdownTableLines(IReadOnlyList<string> lines, string displayPath)
    {
        var errors = new List<string>();
        char fenceChar = '\0';
        int fenceLen = 0;
        bool inHtmlComment = false;
        bool inTable = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string rawLine = lines[i].TrimEnd('\r');
            string trimmed = rawLine.Trim();

            if (TryMatchFenceLine(rawLine, out char markerChar, out int markerLen, out bool hasInfo))
            {
                if (fenceChar == '\0')
                {
                    fenceChar = markerChar;
                    fenceLen = markerLen;
                    inTable = false;
                    continue;
                }

                if (markerChar == fenceChar && markerLen >= fenceLen && !hasInfo)
                {
                    fenceChar = '\0';
                    fenceLen = 0;
                    continue;
                }
            }

            if (fenceChar != '\0')
            {
                continue;
            }

            if (inHtmlComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = false;
                }
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && !trimmed.StartsWith('|'))
            {
                if (!trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                }
                inTable = false;
                continue;
            }

            if (!IsPipeTableRowCandidate(trimmed))
            {
                inTable = false;
                continue;
            }

            if (!inTable)
            {
                string nextTrimmed = (i + 1 < lines.Count) ? lines[i + 1].Trim() : string.Empty;
                if (IsGfmDelimiterRow(nextTrimmed))
                {
                    inTable = true;
                }
                else
                {
                    errors.Add($"{displayPath}:{i + 1} - Orphaned Markdown table row without a preceding table header/delimiter (remove blank lines splitting tables).");
                }
            }

            if (HasUnclosedHtmlCommentInRow(trimmed))
            {
                errors.Add($"{displayPath}:{i + 1} - Unclosed or truncated HTML comment in Markdown table row.");
            }

            if (FindUnclosedBacktickRun(trimmed, out _) >= 0)
            {
                errors.Add($"{displayPath}:{i + 1} - Unbalanced inline backtick code span in Markdown table row.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that data rows within every Markdown table are sorted alphabetically by their primary
    /// link target (<see cref="CompareSortKeys"/>).
    /// </summary>
    public static List<string> ValidateTableRowSorting(IReadOnlyList<string> lines, string displayPath)
    {
        var errors = new List<string>();
        char fenceChar = '\0';
        int fenceLen = 0;
        bool inHtmlComment = false;
        bool inTable = false;
        int tableRowIndex = 0;
        string? prevSortKey = null;

        for (int i = 0; i < lines.Count; i++)
        {
            string rawLine = lines[i].TrimEnd('\r');
            string trimmed = rawLine.Trim();

            if (TryMatchFenceLine(rawLine, out char markerChar, out int markerLen, out bool hasInfo))
            {
                if (fenceChar == '\0')
                {
                    fenceChar = markerChar;
                    fenceLen = markerLen;
                    inTable = false;
                    prevSortKey = null;
                    continue;
                }

                if (markerChar == fenceChar && markerLen >= fenceLen && !hasInfo)
                {
                    fenceChar = '\0';
                    fenceLen = 0;
                    continue;
                }
            }

            if (fenceChar != '\0')
            {
                continue;
            }

            if (inHtmlComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = false;
                }
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && !trimmed.StartsWith('|'))
            {
                if (!trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                }
                inTable = false;
                prevSortKey = null;
                continue;
            }

            if (!IsPipeTableRowCandidate(trimmed))
            {
                inTable = false;
                prevSortKey = null;
                continue;
            }

            if (!inTable)
            {
                string nextTrimmed = (i + 1 < lines.Count) ? lines[i + 1].Trim() : string.Empty;
                if (IsGfmDelimiterRow(nextTrimmed))
                {
                    inTable = true;
                    tableRowIndex = 0;
                    prevSortKey = null;
                }
                continue;
            }

            tableRowIndex++;
            if (tableRowIndex == 1 && IsGfmDelimiterRow(trimmed))
            {
                continue;
            }

            string? currentSortKey = TryExtractRowPrimaryTarget(trimmed);
            if (string.IsNullOrEmpty(currentSortKey))
            {
                continue;
            }

            if (prevSortKey is not null && CompareSortKeys(prevSortKey, currentSortKey) > 0)
            {
                errors.Add($"{displayPath}:{i + 1} - Row target '{currentSortKey}' is out of alphabetical order after '{prevSortKey}'.");
            }

            prevSortKey = currentSortKey;
        }

        return errors;
    }

    private static List<string> FindForbiddenPerAdrRowsInHub(IReadOnlyList<string> hubLines)
    {
        var errors = new List<string>();
        for (int i = 0; i < hubLines.Count; i++)
        {
            string trimmed = hubLines[i].Trim();
            if (!IsPipeTableRowCandidate(trimmed) || IsGfmDelimiterRow(trimmed))
            {
                continue;
            }

            string? rawTarget = TryExtractRowPrimaryTarget(trimmed);
            if (string.IsNullOrEmpty(rawTarget))
            {
                continue;
            }

            string clean = NormalizeRelativePath(rawTarget.Split('#')[0]);
            if (!clean.StartsWith("adr/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsAllowedHubAdrTarget(clean))
            {
                continue;
            }

            errors.Add($"docs/DOCUMENTATION_INDEX.md:{i + 1} - Forbidden per-ADR entry '{clean}'; list ADRs only in 'docs/adr/<domain>/index.md' or 'docs/adr/legacy-index.md'.");
        }

        return errors;
    }

    private static bool IsAllowedHubAdrTarget(string cleanTarget)
    {
        if (cleanTarget.Equals("adr/index.md", StringComparison.OrdinalIgnoreCase) ||
            cleanTarget.Equals("adr/TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
            cleanTarget.Equals("adr/legacy-index.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] parts = cleanTarget.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 &&
               parts[0].Equals("adr", StringComparison.OrdinalIgnoreCase) &&
               AdrValidatorService.IsValidDomain(parts[1]) &&
               parts[2].Equals("index.md", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractRowPrimaryTarget(string trimmedRow)
    {
        string firstCell = ExtractFirstTableCell(trimmedRow);
        Match linkMatch = MarkdownLinkRegex().Match(firstCell);
        return linkMatch.Success ? linkMatch.Groups[1].Value.Trim() : null;
    }

    private static string ExtractFirstTableCell(string trimmedRow)
    {
        int start = trimmedRow.StartsWith('|') ? 1 : 0;
        int i = start;
        while (i < trimmedRow.Length)
        {
            if (trimmedRow[i] == '\\' && i + 1 < trimmedRow.Length)
            {
                i += 2;
                continue;
            }
            if (trimmedRow[i] == '|')
            {
                return trimmedRow[start..i].Trim();
            }
            i++;
        }
        return trimmedRow[start..].Trim();
    }

    private static bool TryMatchFenceLine(string line, out char markerChar, out int markerLen, out bool hasInfo)
    {
        string trimmedStart = line.TrimStart();
        if (trimmedStart.Length >= 3 && (trimmedStart[0] == '`' || trimmedStart[0] == '~'))
        {
            char ch = trimmedStart[0];
            int count = 0;
            while (count < trimmedStart.Length && trimmedStart[count] == ch)
            {
                count++;
            }

            if (count >= 3)
            {
                string rest = trimmedStart[count..].Trim();
                if (ch == '`' && rest.Contains('`'))
                {
                    markerChar = '\0';
                    markerLen = 0;
                    hasInfo = false;
                    return false;
                }

                markerChar = ch;
                markerLen = count;
                hasInfo = rest.Length > 0;
                return true;
            }
        }

        markerChar = '\0';
        markerLen = 0;
        hasInfo = false;
        return false;
    }

    private static bool IsPipeTableRowCandidate(string trimmed)
    {
        if (!trimmed.StartsWith('|'))
        {
            return false;
        }

        int unescapedPipes = 0;
        int i = 0;
        while (i < trimmed.Length)
        {
            if (trimmed[i] == '\\' && i + 1 < trimmed.Length)
            {
                i += 2;
                continue;
            }

            if (trimmed[i] == '|')
            {
                unescapedPipes++;
            }

            i++;
        }

        return unescapedPipes >= 2;
    }

    private static bool IsGfmDelimiterRow(string trimmed)
    {
        if (!trimmed.StartsWith('|'))
        {
            return false;
        }

        string inner = trimmed.Trim('|');
        string[] cells = inner.Split('|');
        if (cells.Length == 0)
        {
            return false;
        }

        foreach (string rawCell in cells)
        {
            string cell = rawCell.Trim();
            if (cell.Length == 0)
            {
                return false;
            }

            int start = cell[0] == ':' ? 1 : 0;
            int end = cell[^1] == ':' ? cell.Length - 1 : cell.Length;
            if (end <= start)
            {
                return false;
            }

            for (int k = start; k < end; k++)
            {
                if (cell[k] != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasUnclosedHtmlCommentInRow(string row)
    {
        int idx = 0;
        while (idx < row.Length)
        {
            int openIdx = row.IndexOf("<!--", idx, StringComparison.Ordinal);
            if (openIdx < 0)
            {
                return false;
            }

            int closeIdx = row.IndexOf("-->", openIdx + 4, StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                return true;
            }

            idx = closeIdx + 3;
        }

        return false;
    }

    public static List<string> CollapseIntraTableBlankLines(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        char fenceChar = '\0';
        int fenceLen = 0;
        bool inHtmlComment = false;
        bool prevWasPipeRow = false;

        int i = 0;
        while (i < lines.Count)
        {
            string rawLine = lines[i].TrimEnd('\r');
            string trimmed = rawLine.Trim();

            if (TryMatchFenceLine(rawLine, out char markerChar, out int markerLen, out bool hasInfo))
            {
                if (fenceChar == '\0')
                {
                    fenceChar = markerChar;
                    fenceLen = markerLen;
                }
                else if (markerChar == fenceChar && markerLen >= fenceLen && !hasInfo)
                {
                    fenceChar = '\0';
                    fenceLen = 0;
                }

                prevWasPipeRow = false;
                result.Add(lines[i]);
                i++;
                continue;
            }

            if (fenceChar != '\0')
            {
                result.Add(lines[i]);
                i++;
                continue;
            }

            if (inHtmlComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = false;
                }

                prevWasPipeRow = false;
                result.Add(lines[i]);
                i++;
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && !trimmed.StartsWith('|'))
            {
                if (!trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                }

                prevWasPipeRow = false;
                result.Add(lines[i]);
                i++;
                continue;
            }

            if (prevWasPipeRow && string.IsNullOrWhiteSpace(trimmed))
            {
                int nextNonBlank = i + 1;
                while (nextNonBlank < lines.Count && string.IsNullOrWhiteSpace(lines[nextNonBlank]))
                {
                    nextNonBlank++;
                }

                if (nextNonBlank < lines.Count)
                {
                    string nextTrimmed = lines[nextNonBlank].Trim();
                    string afterNextTrimmed = (nextNonBlank + 1 < lines.Count)
                        ? lines[nextNonBlank + 1].Trim()
                        : string.Empty;

                    if (IsPipeTableRowCandidate(nextTrimmed) && !IsGfmDelimiterRow(afterNextTrimmed))
                    {
                        i = nextNonBlank;
                        continue;
                    }
                }
            }

            prevWasPipeRow = IsPipeTableRowCandidate(trimmed);
            result.Add(lines[i]);
            i++;
        }

        return result;
    }

    private static string BuildInitialShardHeader(string shardRelPath)
    {
        if (shardRelPath.Equals("ROOT_INDEX.md", StringComparison.Ordinal))
        {
            return string.Join(
                '\n',
                "# Root & Cross-Cutting Guides Index",
                "",
                "Root-level `docs/*.md` guides and staged repository-root references. Part of the sharded [Documentation Index](DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.",
                "");
        }

        string topDir = shardRelPath.Split('/')[0];
        string title = topDir.Equals("blog", StringComparison.OrdinalIgnoreCase)
            ? "Engineering Blog Index"
            : $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topDir.Replace('-', ' ').Replace('_', ' '))} Documentation Index";

        return string.Join(
            '\n',
            $"# {title}",
            "",
            $"Documentation shard for `docs/{topDir}/`. Part of the sharded [Documentation Index](../DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.",
            "");
    }

    private static string GenerateUpdatedShardContent(
        string originalShardContent,
        List<MarkdownDocInfo> shardDiskDocs,
        List<string> unindexedPaths,
        string docsDir,
        string shardRelPath)
    {
        string shardFullPath = Path.Combine(docsDir, shardRelPath.Replace('/', Path.DirectorySeparatorChar));
        string shardDirFull = Path.GetDirectoryName(shardFullPath) ?? docsDir;

        var unindexedSet = new HashSet<string>(unindexedPaths, StringComparer.OrdinalIgnoreCase);
        var unindexedDocs = shardDiskDocs.Where(d => unindexedSet.Contains(d.RelativePath)).ToList();

        bool isBlogShard = shardRelPath.Equals("blog/DOC_INDEX.md", StringComparison.OrdinalIgnoreCase) ||
                           shardDiskDocs.Any(d => IsDatedBlogPostPath(d.RelativePath));

        var publishedBlogPosts = shardDiskDocs
            .Where(d => IsDatedBlogPostPath(d.RelativePath))
            .OrderBy(d => GetShardRelativeHref(shardDirFull, d.FullPath), Comparer<string>.Create(CompareSortKeys))
            .ToList();
        var blogIndexDoc = shardDiskDocs.FirstOrDefault(d =>
            d.RelativePath.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase));

        var categoryGroups = new Dictionary<string, List<MarkdownDocInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in unindexedDocs)
        {
            string category = DetermineCategory(doc.RelativePath);
            if (isBlogShard && string.Equals(category, "## Engineering blog", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!categoryGroups.TryGetValue(category, out var list))
            {
                list = [];
                categoryGroups[category] = list;
            }
            list.Add(doc);
        }

        var lines = CollapseIntraTableBlankLines([.. originalShardContent.Split('\n')]);

        var sectionIndices = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("## ", StringComparison.Ordinal))
            {
                sectionIndices.Add(i);
            }
        }

        bool sawBlogSection = false;

        for (int s = sectionIndices.Count - 1; s >= 0; s--)
        {
            int start = sectionIndices[s];
            int end = (s + 1 < sectionIndices.Count) ? sectionIndices[s + 1] : lines.Count;
            string categoryHeader = lines[start].Trim();

            if (isBlogShard && string.Equals(categoryHeader, "## Engineering blog", StringComparison.OrdinalIgnoreCase))
            {
                sawBlogSection = true;
                RebuildEngineeringBlogShardSection(lines, start, end, blogIndexDoc, publishedBlogPosts, docsDir, shardDirFull);
                continue;
            }

            if (categoryGroups.TryGetValue(categoryHeader, out var pendingDocs) && pendingDocs.Count > 0)
            {
                int lastTableLine = -1;
                bool hasStatusCol = true;
                for (int i = start; i < end; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("| Document |", StringComparison.Ordinal) ||
                        trimmed.StartsWith("| Location |", StringComparison.Ordinal))
                    {
                        hasStatusCol = trimmed.Contains("Status", StringComparison.Ordinal);
                    }
                    if (trimmed.StartsWith('|'))
                    {
                        lastTableLine = i;
                    }
                }

                var renderedRows = RenderDocRows(pendingDocs, shardDirFull, hasStatusCol);
                if (lastTableLine >= 0)
                {
                    bool needsTrailingBlankLine =
                        lastTableLine + 1 == end &&
                        end < lines.Count &&
                        !string.IsNullOrWhiteSpace(lines[end]);
                    if (needsTrailingBlankLine)
                    {
                        renderedRows.Add(string.Empty);
                    }

                    lines.InsertRange(lastTableLine + 1, renderedRows);
                }
                else
                {
                    var newTable = new List<string>
                    {
                        "",
                        "| Document | Status | What it is |",
                        "| --- | --- | --- |",
                    };
                    newTable.AddRange(renderedRows);
                    lines.InsertRange(end, newTable);
                }

                categoryGroups.Remove(categoryHeader);
            }
        }

        if (isBlogShard && !sawBlogSection && (blogIndexDoc is not null || publishedBlogPosts.Count > 0))
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add("");
            }
            lines.Add("## Engineering blog");
            lines.Add("");
            lines.Add("| Document | Status | What it is |");
            lines.Add("| --- | --- | --- |");
            if (blogIndexDoc is not null)
            {
                lines.Add(RenderSingleDocRow(blogIndexDoc, shardDirFull, hasStatusCol: true, includeBlogDate: false));
            }
            foreach (var post in publishedBlogPosts)
            {
                lines.Add(RenderSingleDocRow(post, shardDirFull, hasStatusCol: true, includeBlogDate: true));
            }
        }

        foreach (var (cat, docs) in categoryGroups.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (docs.Count > 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add("");
                }
                lines.Add(cat);
                lines.Add("");
                lines.Add("| Document | Status | What it is |");
                lines.Add("| --- | --- | --- |");
                lines.AddRange(RenderDocRows(docs, shardDirFull, hasStatusCol: true));
            }
        }

        SortAllTablesInLines(lines);
        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private static void RebuildEngineeringBlogShardSection(
        List<string> lines,
        int start,
        int end,
        MarkdownDocInfo? blogIndexDoc,
        List<MarkdownDocInfo> publishedBlogPosts,
        string docsDir,
        string shardDirFull)
    {
        int firstTableLine = -1;
        int lastTableLine = -1;
        bool hasStatusCol = true;
        var headerAndSepLines = new List<string>();
        string? existingBlogIndexRow = null;
        var preservedNonBlogRows = new List<string>();

        for (int i = start + 1; i < end; i++)
        {
            string rawLine = lines[i];
            string trimmed = rawLine.Trim();
            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            if (firstTableLine < 0)
            {
                firstTableLine = i;
            }
            lastTableLine = i;

            if (trimmed.StartsWith("| Document |", StringComparison.Ordinal) ||
                trimmed.StartsWith("| Location |", StringComparison.Ordinal))
            {
                hasStatusCol = trimmed.Contains("Status", StringComparison.Ordinal);
                headerAndSepLines.Add(rawLine);
                continue;
            }

            if (IsGfmDelimiterRow(trimmed))
            {
                headerAndSepLines.Add(rawLine);
                continue;
            }

            string? rowTargetRaw = TryExtractRowPrimaryTarget(trimmed);
            if (string.IsNullOrEmpty(rowTargetRaw))
            {
                preservedNonBlogRows.Add(rawLine);
                continue;
            }

            string cleanTarget = rowTargetRaw.Split('#')[0].Trim();
            string resolvedFull = Path.GetFullPath(Path.Combine(shardDirFull, cleanTarget.Replace('/', Path.DirectorySeparatorChar)));
            string relToDocs = Path.GetRelativePath(Path.GetFullPath(docsDir), resolvedFull).Replace('\\', '/');

            if (relToDocs.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase))
            {
                existingBlogIndexRow ??= rawLine;
                continue;
            }

            if (IsDatedBlogPostPath(relToDocs))
            {
                continue;
            }

            if (!IsDraftMarkdownFile(resolvedFull))
            {
                preservedNonBlogRows.Add(rawLine);
            }
        }

        var rebuiltTable = new List<string>();
        if (headerAndSepLines.Count > 0)
        {
            rebuiltTable.AddRange(headerAndSepLines);
        }
        else
        {
            rebuiltTable.Add("| Document | Status | What it is |");
            rebuiltTable.Add("| --- | --- | --- |");
        }

        var dataRows = new List<string>();
        if (existingBlogIndexRow is not null)
        {
            dataRows.Add(existingBlogIndexRow);
        }
        else if (blogIndexDoc is not null)
        {
            dataRows.Add(RenderSingleDocRow(blogIndexDoc, shardDirFull, hasStatusCol, includeBlogDate: false));
        }

        dataRows.AddRange(preservedNonBlogRows);

        foreach (var post in publishedBlogPosts)
        {
            dataRows.Add(RenderSingleDocRow(post, shardDirFull, hasStatusCol, includeBlogDate: true));
        }

        rebuiltTable.AddRange(SortTableDataRows(dataRows));

        if (firstTableLine >= 0 && lastTableLine >= firstTableLine)
        {
            bool needsTrailingBlankLine =
                lastTableLine + 1 == end &&
                end < lines.Count &&
                !string.IsNullOrWhiteSpace(lines[end]);
            if (needsTrailingBlankLine)
            {
                rebuiltTable.Add(string.Empty);
            }

            lines.RemoveRange(firstTableLine, lastTableLine - firstTableLine + 1);
            lines.InsertRange(firstTableLine, rebuiltTable);
        }
        else if (blogIndexDoc is not null || publishedBlogPosts.Count > 0)
        {
            var newBlock = new List<string> { "" };
            newBlock.AddRange(rebuiltTable);
            lines.InsertRange(end, newBlock);
        }
    }

    private static string RepairAndSyncHubContent(string hubOriginal, List<string> unlinkedShards)
    {
        var lines = CollapseIntraTableBlankLines([.. hubOriginal.Split('\n')]);

        // Strip any forbidden per-ADR rows in hub tables while keeping hub/shard ADR links intact.
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (!IsPipeTableRowCandidate(trimmed) || IsGfmDelimiterRow(trimmed))
            {
                continue;
            }

            string? rawTarget = TryExtractRowPrimaryTarget(trimmed);
            if (string.IsNullOrEmpty(rawTarget))
            {
                continue;
            }

            string clean = NormalizeRelativePath(rawTarget.Split('#')[0]);
            if (clean.StartsWith("adr/", StringComparison.OrdinalIgnoreCase) && !IsAllowedHubAdrTarget(clean))
            {
                lines.RemoveAt(i);
            }
        }

        if (unlinkedShards.Count > 0)
        {
            var shardRows = unlinkedShards
                .Order(Comparer<string>.Create(CompareSortKeys))
                .Select(RenderHubShardRow)
                .ToList();

            int shardSectionIdx = lines.FindIndex(l => l.Trim().Equals("## Category documentation shards", StringComparison.OrdinalIgnoreCase));
            if (shardSectionIdx >= 0)
            {
                int sectionEnd = lines.Count;
                for (int i = shardSectionIdx + 1; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("## ", StringComparison.Ordinal))
                    {
                        sectionEnd = i;
                        break;
                    }
                }

                int lastTableLine = -1;
                for (int i = shardSectionIdx + 1; i < sectionEnd; i++)
                {
                    if (lines[i].Trim().StartsWith('|'))
                    {
                        lastTableLine = i;
                    }
                }

                if (lastTableLine >= 0)
                {
                    bool needsBlank = lastTableLine + 1 == sectionEnd &&
                                      sectionEnd < lines.Count &&
                                      !string.IsNullOrWhiteSpace(lines[sectionEnd]);
                    if (needsBlank)
                    {
                        shardRows.Add(string.Empty);
                    }
                    lines.InsertRange(lastTableLine + 1, shardRows);
                }
                else
                {
                    var block = new List<string>
                    {
                        "",
                        "| Document | Status | What it is |",
                        "| --- | --- | --- |",
                    };
                    block.AddRange(shardRows);
                    lines.InsertRange(shardSectionIdx + 1, block);
                }
            }
            else
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add("");
                }
                lines.Add("## Category documentation shards");
                lines.Add("");
                lines.Add("| Document | Status | What it is |");
                lines.Add("| --- | --- | --- |");
                lines.AddRange(shardRows);
            }
        }

        SortAllTablesInLines(lines);
        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private static string RenderHubShardRow(string shardRelPath)
    {
        if (shardRelPath.Equals("ROOT_INDEX.md", StringComparison.Ordinal))
        {
            return "| [Root & cross-cutting guides index](ROOT_INDEX.md) | Current | Root-level guides (`docs/*.md` and repo root) |";
        }

        string topDir = shardRelPath.Split('/')[0];
        string status = DetermineStatus($"{topDir}/", string.Empty);
        return $"| [{topDir} documentation index]({shardRelPath}) | {status} | Documentation shard for `docs/{topDir}/` |";
    }

    private static void SortAllTablesInLines(List<string> lines)
    {
        char fenceChar = '\0';
        int fenceLen = 0;
        bool inHtmlComment = false;
        int i = 0;

        while (i < lines.Count)
        {
            string rawLine = lines[i].TrimEnd('\r');
            string trimmed = rawLine.Trim();

            if (TryMatchFenceLine(rawLine, out char markerChar, out int markerLen, out bool hasInfo))
            {
                if (fenceChar == '\0')
                {
                    fenceChar = markerChar;
                    fenceLen = markerLen;
                }
                else if (markerChar == fenceChar && markerLen >= fenceLen && !hasInfo)
                {
                    fenceChar = '\0';
                    fenceLen = 0;
                }
                i++;
                continue;
            }

            if (fenceChar != '\0')
            {
                i++;
                continue;
            }

            if (inHtmlComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = false;
                }
                i++;
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && !trimmed.StartsWith('|'))
            {
                if (!trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                }
                i++;
                continue;
            }

            if (IsPipeTableRowCandidate(trimmed) &&
                i + 1 < lines.Count &&
                IsGfmDelimiterRow(lines[i + 1].Trim()))
            {
                int dataStart = i + 2;
                int dataEnd = dataStart;
                while (dataEnd < lines.Count && IsPipeTableRowCandidate(lines[dataEnd].Trim()))
                {
                    dataEnd++;
                }

                if (dataEnd > dataStart + 1)
                {
                    var slice = lines.GetRange(dataStart, dataEnd - dataStart);
                    var sorted = SortTableDataRows(slice);
                    for (int k = 0; k < sorted.Count; k++)
                    {
                        lines[dataStart + k] = sorted[k];
                    }
                }

                i = dataEnd;
                continue;
            }

            i++;
        }
    }

    private static List<string> SortTableDataRows(List<string> rows)
    {
        return [.. rows
            .Select((row, idx) => (Row: row, Index: idx, Key: TryExtractRowPrimaryTarget(row.Trim()) ?? string.Empty))
            .OrderBy(item => item.Key, Comparer<string>.Create(CompareSortKeys))
            .ThenBy(item => item.Index)
            .Select(item => item.Row)];
    }

    private static string GetShardRelativeHref(string shardDirFull, string docFullPath) =>
        Path.GetRelativePath(shardDirFull, docFullPath).Replace('\\', '/');

    private static List<string> RenderDocRows(List<MarkdownDocInfo> docs, string shardDirFull, bool hasStatusCol)
    {
        var result = new List<string>();
        foreach (var doc in docs.OrderBy(d => GetShardRelativeHref(shardDirFull, d.FullPath), Comparer<string>.Create(CompareSortKeys)))
        {
            result.Add(RenderSingleDocRow(doc, shardDirFull, hasStatusCol, includeBlogDate: IsDatedBlogPostPath(doc.RelativePath)));
        }
        return result;
    }

    private static string RenderSingleDocRow(MarkdownDocInfo doc, string shardDirFull, bool hasStatusCol, bool includeBlogDate)
    {
        string fileContent = File.Exists(doc.FullPath) ? File.ReadAllText(doc.FullPath) : "";
        var (title, desc) = ExtractDocMetadata(doc.FullPath, doc.RelativePath);
        string status = DetermineStatus(doc.RelativePath, fileContent);
        string href = GetShardRelativeHref(shardDirFull, doc.FullPath);

        if (includeBlogDate)
        {
            string isoDate = ExtractBlogPostDate(doc.FullPath, doc.RelativePath);
            if (!string.IsNullOrEmpty(isoDate) && !desc.StartsWith(isoDate, StringComparison.Ordinal))
            {
                desc = $"{isoDate} — {desc}";
            }
        }

        title = title.Replace("|", "\\|", StringComparison.Ordinal);
        desc = desc.Replace("|", "\\|", StringComparison.Ordinal);

        return hasStatusCol
            ? $"| [{title}]({href}) | {status} | {desc} |"
            : $"| [{title}]({href}) | {desc} |";
    }
}

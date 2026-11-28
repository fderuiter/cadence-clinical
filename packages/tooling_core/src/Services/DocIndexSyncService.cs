using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Markdown;
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
        "runs"
    };

    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOCUMENTATION_INDEX.md"
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
        "index.md"
    };

    [GeneratedRegex(@"\]\(([^)\r\n]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();

    public static int SyncDocIndex(bool check = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");
        string indexPath = Path.Combine(docsDir, "DOCUMENTATION_INDEX.md");

        if (!Directory.Exists(docsDir) || !File.Exists(indexPath))
        {
            Console.WriteLine($"[ERROR] Documentation directory or index file not found at {indexPath}");
            return 1;
        }

        string originalContent = File.ReadAllText(indexPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        // 1. Scan filesystem for all valid non-draft markdown files in docs/
        var diskFiles = ScanMarkdownFiles(docsDir);

        // 2. Extract existing links and index mappings from DOCUMENTATION_INDEX.md
        var indexedTargets = ExtractIndexedTargets(originalContent);

        // 3. Find unindexed markdown files (non-blog sections + blog/index.md if missing)
        var unindexedFiles = diskFiles
            .Where(file => !indexedTargets.Contains(NormalizePath(file.RelativePath)))
            .Select(file => file.RelativePath)
            .ToList();

        // 4. Reconcile status drift on existing ADR entries in DOCUMENTATION_INDEX.md
        string reconciledContent = ReconcileAdrRowStatuses(originalContent, docsDir, out var staleAdrStatuses);

        // 5. Compute canonical updated index (adds unindexed files AND refreshes/prunes ## Engineering blog rows)
        string blogOnlyUpdatedContent = GenerateUpdatedIndex(reconciledContent, diskFiles, [], docsDir);
        string normalizedReconciled = reconciledContent.TrimEnd() + "\n";
        bool hasBlogSectionDrift = !string.Equals(normalizedReconciled, blogOnlyUpdatedContent, StringComparison.Ordinal);

        if (check)
        {
            var tableErrors = ValidateMarkdownTableLines(originalContent.Split('\n'), "docs/DOCUMENTATION_INDEX.md");
            if (tableErrors.Count > 0)
            {
                Console.WriteLine($"[ERROR] Documentation index 'docs/DOCUMENTATION_INDEX.md' contains {tableErrors.Count} malformed Markdown table row(s):");
                foreach (var err in tableErrors.Take(20))
                {
                    Console.WriteLine($"  - {err}");
                }

                if (tableErrors.Count > 20)
                {
                    Console.WriteLine($"  ... and {tableErrors.Count - 20} more table error(s).");
                }

                return 1;
            }

            if (unindexedFiles.Count > 0 || staleAdrStatuses.Count > 0 || hasBlogSectionDrift)
            {
                if (unindexedFiles.Count > 0)
                {
                    Console.WriteLine($"[ERROR] Documentation index 'docs/DOCUMENTATION_INDEX.md' is missing entries for {unindexedFiles.Count} markdown file(s):");
                    foreach (var unindexed in unindexedFiles.Take(20))
                    {
                        Console.WriteLine($"  - docs/{unindexed}");
                    }

                    if (unindexedFiles.Count > 20)
                    {
                        Console.WriteLine($"  ... and {unindexedFiles.Count - 20} more file(s).");
                    }
                }

                if (staleAdrStatuses.Count > 0)
                {
                    Console.WriteLine($"[ERROR] Documentation index 'docs/DOCUMENTATION_INDEX.md' has stale status values for {staleAdrStatuses.Count} ADR file(s):");
                    foreach (var stale in staleAdrStatuses.Take(20))
                    {
                        Console.WriteLine($"  - docs/{stale}");
                    }

                    if (staleAdrStatuses.Count > 20)
                    {
                        Console.WriteLine($"  ... and {staleAdrStatuses.Count - 20} more file(s).");
                    }
                }

                if (hasBlogSectionDrift)
                {
                    Console.WriteLine("[ERROR] Documentation index 'docs/DOCUMENTATION_INDEX.md' has stale or draft '## Engineering blog' entries.");
                }

                Console.WriteLine("\nRun 'pnpm cadence generate-doc-index' to update 'docs/DOCUMENTATION_INDEX.md'.");
                return 1;
            }

            Console.WriteLine($"✔ Documentation index parity check passed cleanly ({diskFiles.Count} documents indexed).");
            return 0;
        }

        if (unindexedFiles.Count == 0 && staleAdrStatuses.Count == 0 && !hasBlogSectionDrift)
        {
            Console.WriteLine("✔ Documentation index is already up to date.");
            return 0;
        }

        string updatedContent = unindexedFiles.Count > 0
            ? GenerateUpdatedIndex(reconciledContent, diskFiles, unindexedFiles, docsDir)
            : blogOnlyUpdatedContent;

        File.WriteAllText(indexPath, updatedContent);
        Console.WriteLine($"✔ Successfully synchronized documentation index in 'docs/DOCUMENTATION_INDEX.md' ({unindexedFiles.Count} new documents indexed, {staleAdrStatuses.Count} ADR statuses updated).");
        return 0;
    }

    public record MarkdownDocInfo(string RelativePath, string FullPath);

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
        return [.. result.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)];
    }

    private static void ScanDirRecursive(string currentDir, string docsDir, List<MarkdownDocInfo> result)
    {
        var dirName = Path.GetFileName(currentDir);
        if (ExcludedDirectories.Contains(dirName))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(currentDir, "*.md"))
        {
            string fileName = Path.GetFileName(file);
            string relPath = Path.GetRelativePath(docsDir, file).Replace('\\', '/');
            if (ExcludedFiles.Contains(fileName) || StagedSitePages.Contains(relPath))
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
               !normalized.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase);
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
        if (relPath.StartsWith("architecture/repository-migration/", StringComparison.OrdinalIgnoreCase)) return "## Former repository migration program";
        if (relPath.StartsWith("kernel/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("architecture/", StringComparison.OrdinalIgnoreCase)) return "## Kernel and roadmap";
        if (relPath.StartsWith("adr/", StringComparison.OrdinalIgnoreCase)) return "## Architecture decision records";
        if (relPath.StartsWith("SDLC/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("SRS.md", StringComparison.OrdinalIgnoreCase)) return "## Requirements, validation and GxP";
        if (relPath.StartsWith("migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("PYTHON_REFERENCE_INVENTORY.md", StringComparison.OrdinalIgnoreCase) ||
            relPath.Equals("MIGRATION_WORKFLOW.md", StringComparison.OrdinalIgnoreCase)) return "## Python-to-.NET port (completed)";
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
}

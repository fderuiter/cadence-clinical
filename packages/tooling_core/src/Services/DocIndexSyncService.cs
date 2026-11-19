using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        "LICENSE.md",
        "index.md"
    };

    [GeneratedRegex(@"\]\(([^)\r\n]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    // Inline links are reduced to their text in descriptions: their targets are relative to the source
    // document, not to the index, and truncation can cut a link in half.
    [GeneratedRegex(@"\[([^\]\r\n]*)\]\([^)\r\n]*\)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"^\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|")]
    private static partial Regex TableRow3ColRegex();

    [GeneratedRegex(@"^\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|")]
    private static partial Regex TableRow2ColRegex();

    public static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

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

        string originalContent = File.ReadAllText(indexPath);

        // 1. Scan filesystem for all valid markdown files in docs/
        var diskFiles = ScanMarkdownFiles(docsDir);

        // 2. Extract existing links and index mappings from DOCUMENTATION_INDEX.md
        var indexedTargets = ExtractIndexedTargets(originalContent);

        // 3. Find unindexed markdown files
        var unindexedFiles = diskFiles
            .Where(file => !indexedTargets.Contains(NormalizePath(file.RelativePath)))
            .Select(file => file.RelativePath)
            .ToList();

        if (check)
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
                Console.WriteLine("\nRun 'pnpm cadence generate-doc-index' to update 'docs/DOCUMENTATION_INDEX.md'.");
                return 1;
            }

            Console.WriteLine($"✔ Documentation index parity check passed cleanly ({diskFiles.Count} documents indexed).");
            return 0;
        }

        if (unindexedFiles.Count == 0)
        {
            Console.WriteLine("✔ Documentation index is already up to date.");
            return 0;
        }

        // 4. Update DOCUMENTATION_INDEX.md by adding unindexed files to appropriate categories
        string updatedContent = GenerateUpdatedIndex(originalContent, diskFiles.Where(f => unindexedFiles.Contains(f.RelativePath)).ToList());

        File.WriteAllText(indexPath, updatedContent);
        Console.WriteLine($"✔ Successfully synchronized documentation index in 'docs/DOCUMENTATION_INDEX.md' ({unindexedFiles.Count} new documents indexed).");
        return 0;
    }

    public record MarkdownDocInfo(string RelativePath, string FullPath);

    public static List<MarkdownDocInfo> ScanMarkdownFiles(string docsDir)
    {
        var result = new List<MarkdownDocInfo>();
        ScanDirRecursive(docsDir, docsDir, result);
        return result.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
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

    public static string DetermineStatus(string relPath, string fileContent)
    {
        if (relPath.StartsWith("adr/", StringComparison.OrdinalIgnoreCase)) return "Current";
        if (relPath.StartsWith("migration/", StringComparison.OrdinalIgnoreCase) ||
            relPath.StartsWith("architecture/repository-migration/", StringComparison.OrdinalIgnoreCase)) return "Historical";
        if (relPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase) || relPath.StartsWith("schema/", StringComparison.OrdinalIgnoreCase)) return "Generated";
        if (relPath.StartsWith("SDLC/", StringComparison.OrdinalIgnoreCase))
        {
            if (fileContent.Contains("Baseline", StringComparison.OrdinalIgnoreCase) || fileContent.Contains("Superseded", StringComparison.OrdinalIgnoreCase)) return "Baseline";
            return "Current";
        }
        return "Current";
    }

    public static (string Title, string Description) ExtractDocMetadata(string fullPath, string relPath)
    {
        string title = string.Empty;
        string description = string.Empty;

        try
        {
            string[] lines = File.ReadAllLines(fullPath);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(title) && trimmed.StartsWith("# "))
                {
                    title = trimmed[2..].Trim();
                }
                else if (!string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description) &&
                         !string.IsNullOrWhiteSpace(trimmed) &&
                         !trimmed.StartsWith('#') &&
                         !trimmed.StartsWith("---") &&
                         !trimmed.StartsWith('|') &&
                         !trimmed.StartsWith("```"))
                {
                    string cleaned = trimmed
                        .Replace("> [!WARNING]", "")
                        .Replace("> [!NOTE]", "")
                        .Replace("> [!IMPORTANT]", "")
                        .Replace("> [!TIP]", "")
                        .Replace(">", "")
                        .Replace("**", "")
                        .Trim();
                    cleaned = InlineLinkRegex().Replace(cleaned, "$1");

                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        description = cleaned.Length > 120 ? cleaned[..117] + "..." : cleaned;
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
            title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
        }

        if (string.IsNullOrEmpty(description))
        {
            description = title;
        }

        // Clean up markdown in title if any
        title = title.Replace("`", "").Trim();

        return (title, description);
    }

    private static string GenerateUpdatedIndex(string originalContent, List<MarkdownDocInfo> unindexedDocs)
    {
        var categoryGroups = new Dictionary<string, List<MarkdownDocInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in unindexedDocs)
        {
            string category = DetermineCategory(doc.RelativePath);
            if (!categoryGroups.TryGetValue(category, out var list))
            {
                list = new List<MarkdownDocInfo>();
                categoryGroups[category] = list;
            }
            list.Add(doc);
        }

        var lines = originalContent.Split('\n').ToList();

        // Identify sections starting with "## "
        var sectionIndices = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("## "))
            {
                sectionIndices.Add(i);
            }
        }

        // Process sections from bottom to top so insertions don't invalidate earlier section indices
        for (int s = sectionIndices.Count - 1; s >= 0; s--)
        {
            int start = sectionIndices[s];
            int end = (s + 1 < sectionIndices.Count) ? sectionIndices[s + 1] : lines.Count;
            string categoryHeader = lines[start].Trim();

            if (categoryGroups.TryGetValue(categoryHeader, out var pendingDocs) && pendingDocs.Count > 0)
            {
                // Find last line starting with '|' in this section
                int lastTableLine = -1;
                bool hasStatusCol = true;
                for (int i = start; i < end; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("| Document |") || trimmed.StartsWith("| Location |"))
                    {
                        hasStatusCol = trimmed.Contains("Status");
                    }
                    if (trimmed.StartsWith('|'))
                    {
                        lastTableLine = i;
                    }
                }

                var renderedRows = RenderDocRows(pendingDocs, hasStatusCol);
                if (lastTableLine >= 0)
                {
                    lines.InsertRange(lastTableLine + 1, renderedRows);
                }
                else
                {
                    var newTable = new List<string>
                    {
                        "",
                        "| Document | Status | What it is |",
                        "| --- | --- | --- |"
                    };
                    newTable.AddRange(renderedRows);
                    lines.InsertRange(end, newTable);
                }

                categoryGroups.Remove(categoryHeader);
            }
        }

        // Flush any remaining category groups at end of file
        foreach (var (cat, docs) in categoryGroups)
        {
            if (docs.Count > 0)
            {
                lines.Add("");
                lines.Add(cat);
                lines.Add("");
                lines.Add("| Document | Status | What it is |");
                lines.Add("| --- | --- | --- |");
                lines.AddRange(RenderDocRows(docs, hasStatusCol: true));
            }
        }

        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private static List<string> RenderDocRows(List<MarkdownDocInfo> docs, bool hasStatusCol)
    {
        var result = new List<string>();
        foreach (var doc in docs.OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            string fileContent = File.Exists(doc.FullPath) ? File.ReadAllText(doc.FullPath) : "";
            var (title, desc) = ExtractDocMetadata(doc.FullPath, doc.RelativePath);
            string status = DetermineStatus(doc.RelativePath, fileContent);

            // Escape pipe chars in title or desc
            title = title.Replace("|", "\\|");
            desc = desc.Replace("|", "\\|");

            if (hasStatusCol)
            {
                result.Add($"| [{title}]({doc.RelativePath}) | {status} | {desc} |");
            }
            else
            {
                result.Add($"| [{title}]({doc.RelativePath}) | {desc} |");
            }
        }
        return result;
    }
}

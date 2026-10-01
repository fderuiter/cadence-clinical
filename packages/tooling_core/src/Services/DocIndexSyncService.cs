using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static class DocIndexSyncService
{
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static int SyncDocIndex(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs");
        string indexPath = Path.Combine(docsDir, "DOCUMENTATION_INDEX.md");

        if (!Directory.Exists(docsDir))
        {
            Console.WriteLine("[ERROR] docs/ directory not found.");
            return 1;
        }

        string[] allMdFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}SDLC{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(docsDir, f).Replace('\\', '/'))
            .OrderBy(f => f)
            .ToArray();

        string indexContent = File.Exists(indexPath) ? File.ReadAllText(indexPath) : "# Documentation Index\n\n";

        HashSet<string> existingLinkedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MarkdownLinkRegex.Matches(indexContent))
        {
            string rawLink = match.Groups[2].Value.Trim().Split('#')[0].Split('?')[0];
            if (string.IsNullOrWhiteSpace(rawLink) || rawLink.StartsWith("http://") || rawLink.StartsWith("https://") || rawLink.StartsWith("mailto:"))
            {
                continue;
            }

            string normalized = rawLink;
            if (normalized.StartsWith("../docs/"))
            {
                normalized = normalized["../docs/".Length..];
            }
            else if (normalized.StartsWith("../"))
            {
                normalized = normalized["../".Length..];
            }
            else if (normalized.StartsWith("./"))
            {
                normalized = normalized["./".Length..];
            }

            existingLinkedPaths.Add(normalized);
            if (normalized == "README.md" || normalized == "index.md")
            {
                existingLinkedPaths.Add("README.md");
                existingLinkedPaths.Add("index.md");
            }
        }

        List<string> missingFiles = allMdFiles
            .Where(f => !existingLinkedPaths.Contains(f))
            .ToList();

        if (missingFiles.Count == 0)
        {
            Console.WriteLine($"✔ docs/DOCUMENTATION_INDEX.md is up to date ({allMdFiles.Length} non-execution markdown files indexed).");
            return 0;
        }

        Console.WriteLine($"Found {missingFiles.Count} missing documentation files. Synchronizing docs/DOCUMENTATION_INDEX.md...");

        StringBuilder sb = new(indexContent.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();

        // Group missing files by category
        var groups = missingFiles.GroupBy(GetCategoryForFile).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            string sectionHeading = group.Key;
            if (!indexContent.Contains(sectionHeading))
            {
                sb.AppendLine($"{sectionHeading}");
                sb.AppendLine();
                sb.AppendLine("| Document | Status | Description |");
                sb.AppendLine("| --- | --- | --- |");
            }

            foreach (string relPath in group.OrderBy(f => f))
            {
                string fullPath = Path.Combine(docsDir, relPath);
                string title = ExtractTitle(fullPath, relPath);
                string status = GetStatusForFile(relPath);
                string description = GetDescriptionForFile(title, relPath);

                sb.AppendLine($"| [{EscapePipes(title)}]({relPath}) | {status} | {EscapePipes(description)} |");
            }

            sb.AppendLine();
        }

        File.WriteAllText(indexPath, sb.ToString().TrimEnd() + "\n");
        Console.WriteLine($"✔ Successfully synchronized docs/DOCUMENTATION_INDEX.md ({allMdFiles.Length} total non-execution markdown files now registered).");
        return 0;
    }

    private static string GetCategoryForFile(string relPath)
    {
        if (relPath.StartsWith("adr/")) return "## Architecture decision records";
        if (relPath.StartsWith("schema/") || relPath.StartsWith("CDISC/") || relPath.StartsWith("NCI/") || relPath.StartsWith("openapi/")) return "## Standards reference material";
        if (relPath.StartsWith("migration/")) return "## Python-to-.NET port (completed)";
        if (relPath.StartsWith("api/") || relPath.StartsWith("components/") || relPath.StartsWith("web/")) return "## Developing and running";
        if (relPath.StartsWith("specs/")) return "## Design proposals";
        if (relPath.StartsWith("agents/")) return "## Repository policies";
        if (relPath.StartsWith("SDLC/")) return "## Requirements, validation and GxP";
        if (relPath.StartsWith("kernel/") || relPath.StartsWith("ux/")) return "## Kernel and roadmap";
        return "## Start here";
    }

    private static string GetStatusForFile(string relPath)
    {
        if (relPath.StartsWith("adr/")) return "Current";
        if (relPath.StartsWith("migration/")) return "Historical";
        if (relPath.StartsWith("schema/") || relPath.StartsWith("openapi/")) return "Generated";
        if (relPath.StartsWith("specs/") || relPath.StartsWith("SDLC/")) return "Baseline";
        if (relPath.StartsWith("kernel/")) return "Normative";
        return "Current";
    }

    private static string GetDescriptionForFile(string title, string relPath)
    {
        if (relPath.StartsWith("adr/")) return $"Architectural decision record: {title}";
        if (relPath.StartsWith("schema/")) return $"Database schema definition for {title}";
        if (relPath.StartsWith("migration/")) return $"Historical migration record: {title}";
        if (relPath.StartsWith("api/")) return $"API reference documentation: {title}";
        if (relPath.StartsWith("specs/")) return $"Technical specification: {title}";
        if (relPath.StartsWith("agents/")) return $"Agent guidance: {title}";
        return title;
    }

    private static string ExtractTitle(string fullPath, string relPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                string content = File.ReadAllText(fullPath);

                // Frontmatter check
                Match fmMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
                if (fmMatch.Success)
                {
                    Match titleMatch = Regex.Match(fmMatch.Groups[1].Value, @"^title:\s*[""]?(.*?)[""]?$", RegexOptions.Multiline);
                    if (titleMatch.Success && !string.IsNullOrWhiteSpace(titleMatch.Groups[1].Value))
                    {
                        return titleMatch.Groups[1].Value.Trim();
                    }
                }

                // First H1 match
                Match h1Match = Regex.Match(content, @"^#\s+(.*)", RegexOptions.Multiline);
                if (h1Match.Success && !string.IsNullOrWhiteSpace(h1Match.Groups[1].Value))
                {
                    return h1Match.Groups[1].Value.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            // Fallback to formatted filename if file reading or parsing fails
            _ = ex;
        }

        string filename = Path.GetFileNameWithoutExtension(relPath);
        return FormatFilename(filename);
    }

    private static string FormatFilename(string filename)
    {
        string cleaned = Regex.Replace(filename, @"^(\d+)[-_]", "");
        cleaned = cleaned.Replace('-', ' ').Replace('_', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned);
    }

    private static string EscapePipes(string text)
    {
        return text.Replace("|", "\\|");
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

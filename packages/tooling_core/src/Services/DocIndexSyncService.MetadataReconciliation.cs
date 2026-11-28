using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Markdown;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
    private const string ClinicalKernelInceptionDate = "2026-09-29"; // deid-ignore: architectural milestone date, not PHI

    // Inline links are reduced to their text in descriptions: their targets are relative to the source
    // document, not to the index, and truncation can cut a link in half.
    [GeneratedRegex(@"\[([^\]\r\n]*)\]\([^)\r\n]*\)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"^\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|")]
    private static partial Regex TableRow3ColRegex();

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

    private static string ReconcileAdrRowStatuses(string indexContent, string docsDir, out List<string> updatedPaths)
    {
        updatedPaths = [];
        string[] lines = indexContent.Split('\n');
        bool inAdrSection = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string hasCr = lines[i].EndsWith('\r') ? "\r" : "";
            string line = hasCr.Length > 0 ? lines[i][..^1] : lines[i];
            string trimmed = line.Trim();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                inAdrSection = trimmed.Equals("## Architecture decision records", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inAdrSection || !trimmed.StartsWith('|'))
            {
                continue;
            }

            Match rowMatch = TableRow3ColRegex().Match(line);
            if (!rowMatch.Success)
            {
                continue;
            }

            string docCell = rowMatch.Groups[1].Value.Trim();
            string statusCell = rowMatch.Groups[2].Value.Trim();
            if (docCell.Equals("Document", StringComparison.OrdinalIgnoreCase) ||
                statusCell.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            Match linkMatch = MarkdownLinkRegex().Match(docCell);
            if (!linkMatch.Success)
            {
                continue;
            }

            string rawTarget = linkMatch.Groups[1].Value;
            int anchorIdx = rawTarget.IndexOf('#');
            if (anchorIdx >= 0)
            {
                rawTarget = rawTarget[..anchorIdx];
            }

            string normTarget = NormalizePath(rawTarget);
            if (!normTarget.StartsWith("adr/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fullPath = Path.Combine(docsDir, normTarget.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string expectedStatus = DetermineStatus(normTarget, File.ReadAllText(fullPath));
            if (!string.Equals(statusCell, expectedStatus, StringComparison.Ordinal))
            {
                Group statusGroup = rowMatch.Groups[2];
                string prefix = line[..statusGroup.Index].TrimEnd();
                string suffix = line[(statusGroup.Index + statusGroup.Length)..].TrimStart();
                lines[i] = $"{prefix} {expectedStatus} {suffix}{hasCr}";
                updatedPaths.Add(normTarget);
            }
        }

        return string.Join('\n', lines);
    }

    public static (string Title, string Description) ExtractDocMetadata(string fullPath, string relPath)
    {
        _ = relPath;
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

        // Clean up markdown in title if any
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
}

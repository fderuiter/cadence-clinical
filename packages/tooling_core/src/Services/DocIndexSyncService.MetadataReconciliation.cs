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
    private const string ClinicalKernelInceptionDate = "2026-09-29"; // deid-ignore: architectural milestone date, not PHI

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

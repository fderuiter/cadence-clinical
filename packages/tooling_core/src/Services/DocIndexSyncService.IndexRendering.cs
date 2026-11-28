using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
    [GeneratedRegex(@"^\s*\|\s*\[ADR-(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AdrTableRowNumberRegex();

    private static string GenerateUpdatedIndex(
        string originalContent,
        List<MarkdownDocInfo> allDiskDocs,
        List<string> unindexedPaths,
        string docsDir)
    {
        var unindexedSet = new HashSet<string>(unindexedPaths, StringComparer.OrdinalIgnoreCase);
        var unindexedDocs = allDiskDocs.Where(d => unindexedSet.Contains(d.RelativePath)).ToList();
        var publishedBlogPosts = allDiskDocs
            .Where(d => IsDatedBlogPostPath(d.RelativePath))
            .Select(doc => (Doc: doc, Date: ExtractBlogPostDate(doc.FullPath, doc.RelativePath)))
            .OrderByDescending(entry => entry.Date, StringComparer.Ordinal)
            .ThenByDescending(entry => entry.Doc.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Doc)
            .ToList();
        var blogIndexDoc = allDiskDocs.FirstOrDefault(d =>
            d.RelativePath.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase));

        var categoryGroups = new Dictionary<string, List<MarkdownDocInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in unindexedDocs)
        {
            string category = DetermineCategory(doc.RelativePath);
            if (string.Equals(category, "## Engineering blog", StringComparison.OrdinalIgnoreCase))
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

        var lines = CollapseIntraTableBlankLines([.. originalContent.Split('\n')]);

        // Identify sections starting with "## "
        var sectionIndices = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("## "))
            {
                sectionIndices.Add(i);
            }
        }

        bool sawBlogSection = false;

        // Process sections from bottom to top so insertions don't invalidate earlier section indices
        for (int s = sectionIndices.Count - 1; s >= 0; s--)
        {
            int start = sectionIndices[s];
            int end = (s + 1 < sectionIndices.Count) ? sectionIndices[s + 1] : lines.Count;
            string categoryHeader = lines[start].Trim();

            if (string.Equals(categoryHeader, "## Engineering blog", StringComparison.OrdinalIgnoreCase))
            {
                sawBlogSection = true;
                RebuildEngineeringBlogSection(lines, start, end, blogIndexDoc, publishedBlogPosts, docsDir);
                continue;
            }

            if (categoryGroups.TryGetValue(categoryHeader, out var pendingDocs) && pendingDocs.Count > 0)
            {
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

                if (lastTableLine >= 0)
                {
                    if (string.Equals(categoryHeader, "## Architecture decision records", StringComparison.OrdinalIgnoreCase))
                    {
                        InsertAdrRowsByNumber(lines, start, ref end, ref lastTableLine, pendingDocs, hasStatusCol);
                    }
                    else
                    {
                        var renderedRows = RenderDocRows(pendingDocs, hasStatusCol);
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
                }
                else
                {
                    var renderedRows = RenderDocRows(pendingDocs, hasStatusCol);
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

        if (!sawBlogSection && (blogIndexDoc is not null || publishedBlogPosts.Count > 0))
        {
            lines.Add("");
            lines.Add("## Engineering blog");
            lines.Add("");
            lines.Add("| Document | Status | What it is |");
            lines.Add("| --- | --- | --- |");
            if (blogIndexDoc is not null)
            {
                lines.Add(RenderSingleDocRow(blogIndexDoc, hasStatusCol: true, includeBlogDate: false));
            }

            foreach (var post in publishedBlogPosts)
            {
                lines.Add(RenderSingleDocRow(post, hasStatusCol: true, includeBlogDate: true));
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

    private static void RebuildEngineeringBlogSection(
        List<string> lines,
        int start,
        int end,
        MarkdownDocInfo? blogIndexDoc,
        List<MarkdownDocInfo> publishedBlogPosts,
        string docsDir)
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

            if (trimmed.StartsWith("| ---", StringComparison.Ordinal) ||
                trimmed.StartsWith("|---", StringComparison.Ordinal) ||
                trimmed.StartsWith("| :---", StringComparison.Ordinal))
            {
                headerAndSepLines.Add(rawLine);
                continue;
            }

            Match linkMatch = MarkdownLinkRegex().Match(trimmed);
            if (!linkMatch.Success)
            {
                preservedNonBlogRows.Add(rawLine);
                continue;
            }

            string rowTarget = NormalizePath(linkMatch.Groups[1].Value.Split('#')[0]);
            if (rowTarget.Equals("blog/index.md", StringComparison.OrdinalIgnoreCase))
            {
                existingBlogIndexRow ??= rawLine;
                continue;
            }

            if (IsDatedBlogPostPath(rowTarget))
            {
                // Rebuilt from published disk posts below; draft or deleted posts are excluded.
                continue;
            }

            string candidateFull = Path.Combine(docsDir, rowTarget);
            if (!IsDraftMarkdownFile(candidateFull))
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

        if (existingBlogIndexRow is not null)
        {
            rebuiltTable.Add(existingBlogIndexRow);
        }
        else if (blogIndexDoc is not null)
        {
            rebuiltTable.Add(RenderSingleDocRow(blogIndexDoc, hasStatusCol, includeBlogDate: false));
        }

        rebuiltTable.AddRange(preservedNonBlogRows);

        foreach (var post in publishedBlogPosts)
        {
            rebuiltTable.Add(RenderSingleDocRow(post, hasStatusCol, includeBlogDate: true));
        }

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

    private static void InsertAdrRowsByNumber(
        List<string> lines,
        int start,
        ref int end,
        ref int lastTableLine,
        List<MarkdownDocInfo> pendingDocs,
        bool hasStatusCol)
    {
        var renderedPending = pendingDocs
            .Select(doc =>
            {
                string row = RenderSingleDocRow(doc, hasStatusCol, includeBlogDate: false);
                return (Doc: doc, Row: row, AdrNumber: TryExtractAdrNumberFromRow(row));
            })
            .OrderBy(item => item.AdrNumber.HasValue ? 0 : 1)
            .ThenBy(item => item.AdrNumber ?? int.MaxValue)
            .ThenBy(item => item.Doc.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in renderedPending)
        {
            int insertIndex = lastTableLine + 1;
            if (item.AdrNumber is int targetAdrNumber)
            {
                int bestPredecessorLine = -1;
                int bestPredecessorNumber = int.MinValue;
                int smallestSuccessorLine = -1;
                int smallestSuccessorNumber = int.MaxValue;

                for (int i = start; i <= lastTableLine; i++)
                {
                    if (TryExtractAdrNumberFromRow(lines[i]) is not int existingAdrNumber)
                    {
                        continue;
                    }

                    if (existingAdrNumber <= targetAdrNumber && existingAdrNumber >= bestPredecessorNumber)
                    {
                        bestPredecessorNumber = existingAdrNumber;
                        bestPredecessorLine = i;
                    }
                    else if (existingAdrNumber > targetAdrNumber && existingAdrNumber < smallestSuccessorNumber)
                    {
                        smallestSuccessorNumber = existingAdrNumber;
                        smallestSuccessorLine = i;
                    }
                }

                if (bestPredecessorLine >= 0)
                {
                    insertIndex = bestPredecessorLine + 1;
                }
                else if (smallestSuccessorLine >= 0)
                {
                    insertIndex = smallestSuccessorLine;
                }
            }

            lines.Insert(insertIndex, item.Row);
            lastTableLine++;
            end++;
        }

        if (lastTableLine + 1 == end &&
            end < lines.Count &&
            !string.IsNullOrWhiteSpace(lines[end]))
        {
            lines.Insert(lastTableLine + 1, string.Empty);
        }
    }

    private static int? TryExtractAdrNumberFromRow(string row)
    {
        Match match = AdrTableRowNumberRegex().Match(row);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int adrNumber)
            ? adrNumber
            : null;
    }

    private static List<string> RenderDocRows(List<MarkdownDocInfo> docs, bool hasStatusCol)
    {
        var result = new List<string>();
        foreach (var doc in docs.OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(RenderSingleDocRow(doc, hasStatusCol, includeBlogDate: IsDatedBlogPostPath(doc.RelativePath)));
        }

        return result;
    }

    private static string RenderSingleDocRow(MarkdownDocInfo doc, bool hasStatusCol, bool includeBlogDate)
    {
        string fileContent = File.Exists(doc.FullPath) ? File.ReadAllText(doc.FullPath) : "";
        var (title, desc) = ExtractDocMetadata(doc.FullPath, doc.RelativePath);
        string status = DetermineStatus(doc.RelativePath, fileContent);

        if (includeBlogDate)
        {
            string isoDate = ExtractBlogPostDate(doc.FullPath, doc.RelativePath);
            if (!string.IsNullOrEmpty(isoDate) && !desc.StartsWith(isoDate, StringComparison.Ordinal))
            {
                desc = $"{isoDate} — {desc}";
            }
        }

        // Escape pipe chars in title or desc
        title = title.Replace("|", "\\|", StringComparison.Ordinal);
        desc = desc.Replace("|", "\\|", StringComparison.Ordinal);

        return hasStatusCol
            ? $"| [{title}]({doc.RelativePath}) | {status} | {desc} |"
            : $"| [{title}]({doc.RelativePath}) | {desc} |";
    }
}

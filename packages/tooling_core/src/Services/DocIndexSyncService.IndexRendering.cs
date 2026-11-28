using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
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
}

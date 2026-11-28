using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
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
}

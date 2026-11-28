using System;
using System.Collections.Generic;

namespace Cadence.ToolingCore.Services;

public static partial class DocIndexSyncService
{
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

            // Search for a matching backtick run of exact length `runLen`.
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

            // CommonMark treats a standalone empty ` `` ` (with no content inside a cell) as literal text,
            // whereas single unclosed backticks or cut code spans break inline code parsing.
            if (runLen == 1)
            {
                unclosedLen = runLen;
                return pos;
            }

            // For multi-backtick runs (>= 2), flag when non-whitespace content follows before the next pipe or end of line.
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
                // Per CommonMark, backtick fences cannot contain backticks in their info string (otherwise it's inline code).
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

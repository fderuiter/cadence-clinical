using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Primitives;

/// <summary>
/// Shared reflection-free YAML 1.2 line, comment, scalar, mapping, flow-array, and block-scalar helpers.
/// </summary>
internal static partial class YamlPrimitives
{
    [GeneratedRegex(@"^([>|])([+-]?)(\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockHeaderRegex();

    [GeneratedRegex(@"^([>|])(\d+)([+-]?)$", RegexOptions.CultureInvariant)]
    private static partial Regex AltBlockHeaderRegex();

    public static int CountLeadingSpaces(ReadOnlySpan<char> line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Strips a trailing YAML <c>#</c> comment outside single- or double-quoted strings while preserving leading indentation.
    /// </summary>
    public static string StripLineComment(string line)
    {
        char quote = '\0';
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < line.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < line.Length && line[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                i++;
                continue;
            }

            if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i].TrimEnd();
            }

            i++;
        }

        return line.TrimEnd();
    }

    public static string StripInlineComment(string value) =>
        StripLineComment(value).Trim();

    public static string UnquoteScalar(string raw, bool decodeCommonEscapes = false)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            string inner = trimmed[1..^1];
            if (decodeCommonEscapes)
            {
                return inner
                    .Replace("\\n", "\n", StringComparison.Ordinal)
                    .Replace("\\r", "\r", StringComparison.Ordinal)
                    .Replace("\\t", "\t", StringComparison.Ordinal)
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            return inner
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
    }

    public static string ParseScalar(string raw, bool decodeCommonEscapes = true) =>
        UnquoteScalar(StripInlineComment(raw), decodeCommonEscapes);

    public static bool IsSequenceItem(string trimmed, out string remainder)
    {
        if (string.Equals(trimmed, "-", StringComparison.Ordinal))
        {
            remainder = string.Empty;
            return true;
        }

        if (trimmed.Length > 1 && trimmed[0] == '-' && (trimmed[1] == ' ' || trimmed[1] == '\t'))
        {
            remainder = trimmed[1..].TrimStart(' ', '\t');
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    public static bool TrySplitMapping(string segment, out string key, out string rawValue)
    {
        key = string.Empty;
        rawValue = string.Empty;
        char quote = '\0';
        int flowDepth = 0;

        int i = 0;
        while (i < segment.Length)
        {
            char c = segment[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < segment.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < segment.Length && segment[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '[' or '{')
            {
                flowDepth++;
            }
            else if (c is ']' or '}' && flowDepth > 0)
            {
                flowDepth--;
            }
            else if (flowDepth == 0 && c == ':' && (i + 1 == segment.Length || char.IsWhiteSpace(segment[i + 1])))
            {
                key = UnquoteScalar(segment[..i].Trim());
                rawValue = segment[(i + 1)..].Trim();
                return key.Length > 0;
            }

            i++;
        }

        return false;
    }

    public static bool IsBlockScalarHeader(string rawValue) =>
        TryParseBlockHeader(rawValue, out _, out _, out _);

    public static bool TryParseBlockHeader(string rawHeader, out char style, out char chomp, out int indentHint)
    {
        string cleaned = StripInlineComment(rawHeader);
        Match m = BlockHeaderRegex().Match(cleaned);
        if (m.Success)
        {
            style = m.Groups[1].Value[0];
            chomp = m.Groups[2].Value.Length > 0 ? m.Groups[2].Value[0] : '\0';
            indentHint = m.Groups[3].Value.Length > 0 && int.TryParse(m.Groups[3].Value, out int parsed) ? parsed : 0;
            return true;
        }

        Match alt = AltBlockHeaderRegex().Match(cleaned);
        if (alt.Success)
        {
            style = alt.Groups[1].Value[0];
            indentHint = int.TryParse(alt.Groups[2].Value, out int parsedAlt) ? parsedAlt : 0;
            chomp = alt.Groups[3].Value.Length > 0 ? alt.Groups[3].Value[0] : '\0';
            return true;
        }

        style = '\0';
        chomp = '\0';
        indentHint = 0;
        return false;
    }

    public static List<string> ParseFlowStringArray(string bracketedRaw, bool decodeCommonEscapes = true)
    {
        string trimmed = bracketedRaw.Trim();
        string inner = trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']'
            ? trimmed[1..^1].Trim()
            : trimmed;
        var items = new List<string>();
        if (inner.Length == 0)
        {
            return items;
        }

        var current = new StringBuilder();
        char quote = '\0';
        int i = 0;
        while (i < inner.Length)
        {
            char ch = inner[i];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"' && i + 1 < inner.Length)
                {
                    current.Append(ch).Append(inner[i + 1]);
                    i += 2;
                    continue;
                }

                if (quote == '\'' && ch == '\'' && i + 1 < inner.Length && inner[i + 1] == '\'')
                {
                    current.Append("''");
                    i += 2;
                    continue;
                }

                if (ch == quote)
                {
                    quote = '\0';
                }

                current.Append(ch);
                i++;
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                current.Append(ch);
                i++;
                continue;
            }

            if (ch == ',')
            {
                string parsed = ParseScalar(current.ToString(), decodeCommonEscapes).Trim();
                if (parsed.Length > 0)
                {
                    items.Add(parsed);
                }

                current.Clear();
                i++;
                continue;
            }

            current.Append(ch);
            i++;
        }

        string tail = ParseScalar(current.ToString(), decodeCommonEscapes).Trim();
        if (tail.Length > 0)
        {
            items.Add(tail);
        }

        return items;
    }

    public static (string Value, int NextIndex) ConsumeBlockScalar(
        IReadOnlyList<string> lines,
        int startIndex,
        int endLine,
        int parentIndent,
        char style,
        char chomp,
        int indentHint)
    {
        var rawBlockLines = new List<string>();
        int idx = startIndex;

        while (idx < endLine)
        {
            string candidate = lines[idx].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(candidate))
            {
                rawBlockLines.Add(string.Empty);
                idx++;
                continue;
            }

            int indent = CountLeadingSpaces(candidate);
            if (indent <= parentIndent)
            {
                break;
            }

            rawBlockLines.Add(candidate);
            idx++;
        }

        int blockIndent = indentHint > 0 ? parentIndent + indentHint : 0;
        if (blockIndent == 0)
        {
            foreach (string rawLine in rawBlockLines)
            {
                if (rawLine.Length > 0)
                {
                    int ind = CountLeadingSpaces(rawLine);
                    if (blockIndent == 0 || ind < blockIndent)
                    {
                        blockIndent = ind;
                    }
                }
            }
        }

        if (blockIndent == 0)
        {
            return (string.Empty, idx);
        }

        int trailingEmptyCount = 0;
        while (rawBlockLines.Count > 0 && rawBlockLines[^1].Length == 0)
        {
            rawBlockLines.RemoveAt(rawBlockLines.Count - 1);
            trailingEmptyCount++;
        }

        if (rawBlockLines.Count == 0)
        {
            return (chomp == '+' ? new string('\n', trailingEmptyCount) : string.Empty, idx);
        }

        var stripped = new List<string>(rawBlockLines.Count);
        foreach (string rawLine in rawBlockLines)
        {
            if (rawLine.Length == 0)
            {
                stripped.Add(string.Empty);
                continue;
            }

            int spaces = Math.Min(CountLeadingSpaces(rawLine), blockIndent);
            stripped.Add(rawLine[spaces..]);
        }

        string bodyText = style == '|' ? string.Join('\n', stripped) : FoldBlockLines(stripped);
        return chomp == '+'
            ? ($"{bodyText}\n{new string('\n', trailingEmptyCount)}", idx)
            : (bodyText, idx);
    }

    private static string FoldBlockLines(List<string> stripped)
    {
        var sb = new StringBuilder();
        int pendingEmpty = 0;
        bool prevWasNormal = false;
        bool prevWasIndented = false;

        foreach (string contentLine in stripped)
        {
            if (contentLine.Length == 0)
            {
                pendingEmpty++;
                continue;
            }

            bool isMoreIndented = contentLine.StartsWith(' ');
            if (sb.Length == 0)
            {
                if (pendingEmpty > 0)
                {
                    sb.Append('\n', pendingEmpty);
                    pendingEmpty = 0;
                }

                sb.Append(contentLine);
            }
            else if (pendingEmpty > 0)
            {
                int extraBreaks = prevWasIndented || isMoreIndented ? pendingEmpty + 1 : pendingEmpty;
                sb.Append('\n', extraBreaks).Append(contentLine);
                pendingEmpty = 0;
            }
            else if (prevWasNormal && !isMoreIndented)
            {
                sb.Append(' ').Append(contentLine);
            }
            else
            {
                sb.Append('\n').Append(contentLine);
            }

            prevWasNormal = !isMoreIndented;
            prevWasIndented = isMoreIndented;
        }

        return sb.ToString();
    }
}

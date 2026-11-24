using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Markdown;

public sealed record YamlRelatedLinkEntry(string Link, int LineNumber);

public sealed record ParsedYamlFrontmatter(
    bool HasFrontmatter,
    int BodyStartLineIndex,
    IReadOnlyDictionary<string, string> Scalars,
    IReadOnlyList<YamlRelatedLinkEntry> RelatedLinks)
{
    public bool IsDraft =>
        Scalars.TryGetValue("draft", out string? rawDraft) &&
        string.Equals(rawDraft.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

public static partial class YamlFrontmatterParser
{
    [GeneratedRegex(@"^([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex TopKeyRegex();

    [GeneratedRegex(@"^(\s*)-\s*(.*)$")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^(\s+)([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex SubFieldRegex();

    [GeneratedRegex(@"^([>|])([+-]?)(\d*)$")]
    private static partial Regex BlockHeaderRegex();

    [GeneratedRegex(@"^([>|])(\d+)([+-]?)$")]
    private static partial Regex AltBlockHeaderRegex();

    public static ParsedYamlFrontmatter Parse(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var related = new List<YamlRelatedLinkEntry>();

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return new ParsedYamlFrontmatter(false, 0, scalars, related);
        }

        int endLine = -1;
        for (int idx = 1; idx < lines.Length; idx++)
        {
            string marker = lines[idx].Trim();
            if (marker is "---" or "...")
            {
                endLine = idx;
                break;
            }
        }

        if (endLine < 0)
        {
            return new ParsedYamlFrontmatter(false, 0, scalars, related);
        }

        int i = 1;
        while (i < endLine)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                i++;
                continue;
            }

            Match keyMatch = TopKeyRegex().Match(line);
            if (!keyMatch.Success)
            {
                i++;
                continue;
            }

            string key = keyMatch.Groups[1].Value;
            string rest = keyMatch.Groups[2].Value.Trim();
            int keyLineNumber = i + 1;

            if (TryParseBlockHeader(rest, out char style, out char chomp, out int indentHint))
            {
                var (blockValue, nextIdx) = ConsumeBlockScalar(lines, i + 1, endLine, parentIndent: 0, style, chomp, indentHint);
                scalars[key] = blockValue;
                i = nextIdx;
                continue;
            }

            string uncommented = StripInlineComment(rest);
            if (uncommented.StartsWith('[') && uncommented.EndsWith(']'))
            {
                if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string item in ParseInlineArray(uncommented))
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            related.Add(new YamlRelatedLinkEntry(item.Trim(), keyLineNumber));
                        }
                    }
                }
                i++;
                continue;
            }

            if (uncommented.Length == 0)
            {
                i++;
                while (i < endLine)
                {
                    string itemLine = lines[i].TrimEnd('\r');
                    string itemTrimmed = itemLine.Trim();
                    if (string.IsNullOrEmpty(itemTrimmed) || itemTrimmed.StartsWith('#'))
                    {
                        i++;
                        continue;
                    }

                    if (TopKeyRegex().IsMatch(itemLine))
                    {
                        break;
                    }

                    Match listMatch = ListItemRegex().Match(itemLine);
                    if (!listMatch.Success)
                    {
                        i++;
                        continue;
                    }

                    int dashIndent = listMatch.Groups[1].Value.Length;
                    string firstEntry = listMatch.Groups[2].Value.Trim();
                    int itemLineNumber = i + 1;

                    if (TryParseBlockHeader(firstEntry, out char itemStyle, out char itemChomp, out int itemIndentHint))
                    {
                        var (itemBlockVal, nextItemIdx) = ConsumeBlockScalar(
                            lines,
                            i + 1,
                            endLine,
                            dashIndent,
                            itemStyle,
                            itemChomp,
                            itemIndentHint);
                        if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(itemBlockVal))
                        {
                            related.Add(new YamlRelatedLinkEntry(itemBlockVal.Trim(), itemLineNumber));
                        }
                        i = nextItemIdx;
                        continue;
                    }

                    Match objectFieldMatch = TopKeyRegex().Match(firstEntry);
                    if (objectFieldMatch.Success)
                    {
                        string firstFieldKey = objectFieldMatch.Groups[1].Value;
                        string firstFieldRaw = objectFieldMatch.Groups[2].Value.Trim();
                        int firstFieldIndent = itemLine.IndexOf(firstFieldKey, StringComparison.Ordinal);

                        if (TryParseBlockHeader(firstFieldRaw, out char fStyle, out char fChomp, out int fIndentHint))
                        {
                            var (fVal, nextFIdx) = ConsumeBlockScalar(
                                lines,
                                i + 1,
                                endLine,
                                firstFieldIndent,
                                fStyle,
                                fChomp,
                                fIndentHint);
                            if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                                IsLinkKey(firstFieldKey) &&
                                !string.IsNullOrWhiteSpace(fVal))
                            {
                                related.Add(new YamlRelatedLinkEntry(fVal.Trim(), itemLineNumber));
                            }
                            i = nextFIdx;
                        }
                        else
                        {
                            string parsedFirst = ParseScalar(firstFieldRaw);
                            if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                                IsLinkKey(firstFieldKey) &&
                                !string.IsNullOrWhiteSpace(parsedFirst))
                            {
                                related.Add(new YamlRelatedLinkEntry(parsedFirst.Trim(), itemLineNumber));
                            }
                            i++;
                        }

                        while (i < endLine)
                        {
                            string subLine = lines[i].TrimEnd('\r');
                            string subTrimmed = subLine.Trim();
                            if (string.IsNullOrEmpty(subTrimmed) || subTrimmed.StartsWith('#'))
                            {
                                i++;
                                continue;
                            }

                            if (TopKeyRegex().IsMatch(subLine) || ListItemRegex().IsMatch(subLine))
                            {
                                break;
                            }

                            Match subMatch = SubFieldRegex().Match(subLine);
                            if (subMatch.Success)
                            {
                                int subIndent = subMatch.Groups[1].Value.Length;
                                string subKey = subMatch.Groups[2].Value;
                                string subRaw = subMatch.Groups[3].Value.Trim();
                                int subLineNumber = i + 1;

                                if (TryParseBlockHeader(subRaw, out char sStyle, out char sChomp, out int sIndentHint))
                                {
                                    var (sVal, nextSIdx) = ConsumeBlockScalar(
                                        lines,
                                        i + 1,
                                        endLine,
                                        subIndent,
                                        sStyle,
                                        sChomp,
                                        sIndentHint);
                                    if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                                        IsLinkKey(subKey) &&
                                        !string.IsNullOrWhiteSpace(sVal))
                                    {
                                        related.Add(new YamlRelatedLinkEntry(sVal.Trim(), subLineNumber));
                                    }
                                    i = nextSIdx;
                                    continue;
                                }

                                string parsedSub = ParseScalar(subRaw);
                                if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                                    IsLinkKey(subKey) &&
                                    !string.IsNullOrWhiteSpace(parsedSub))
                                {
                                    related.Add(new YamlRelatedLinkEntry(parsedSub.Trim(), subLineNumber));
                                }
                            }
                            i++;
                        }
                    }
                    else
                    {
                        string parsedItem = ParseScalar(firstEntry);
                        if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(parsedItem))
                        {
                            related.Add(new YamlRelatedLinkEntry(parsedItem.Trim(), itemLineNumber));
                        }
                        i++;
                    }
                }
                continue;
            }

            scalars[key] = ParseScalar(rest);
            i++;
        }

        return new ParsedYamlFrontmatter(true, endLine + 1, scalars, related);
    }

    private static bool IsLinkKey(string key) =>
        string.Equals(key, "link", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "href", StringComparison.OrdinalIgnoreCase);

    private static string StripInlineComment(string raw)
    {
        char quote = '\0';
        int i = 0;
        while (i < raw.Length)
        {
            char ch = raw[i];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"' && i + 1 < raw.Length)
                {
                    i += 2;
                    continue;
                }
                if (ch == quote)
                {
                    quote = '\0';
                }
                i++;
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                i++;
                continue;
            }

            if (ch == '#' && (i == 0 || char.IsWhiteSpace(raw[i - 1])))
            {
                return raw[..i].Trim();
            }

            i++;
        }

        return raw.Trim();
    }

    public static string ParseScalar(string raw)
    {
        string trimmed = StripInlineComment(raw);
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static List<string> ParseInlineArray(string raw)
    {
        string inner = raw[1..^1].Trim();
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
                string parsed = ParseScalar(current.ToString()).Trim();
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

        string tail = ParseScalar(current.ToString()).Trim();
        if (tail.Length > 0)
        {
            items.Add(tail);
        }

        return items;
    }

    private static bool TryParseBlockHeader(string rawHeader, out char style, out char chomp, out int indentHint)
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

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }
        return count;
    }

    private static (string Value, int NextIndex) ConsumeBlockScalar(
        string[] lines,
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

        string bodyText;
        if (style == '|')
        {
            bodyText = string.Join('\n', stripped);
        }
        else
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

            bodyText = sb.ToString();
        }

        if (chomp == '+')
        {
            return ($"{bodyText}\n{new string('\n', trailingEmptyCount)}", idx);
        }

        return (bodyText, idx);
    }
}

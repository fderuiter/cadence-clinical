using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

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

            if (YamlPrimitives.TryParseBlockHeader(rest, out char style, out char chomp, out int indentHint))
            {
                var (blockValue, nextIdx) = YamlPrimitives.ConsumeBlockScalar(lines, i + 1, endLine, parentIndent: 0, style, chomp, indentHint);
                scalars[key] = blockValue;
                i = nextIdx;
                continue;
            }

            string uncommented = YamlPrimitives.StripInlineComment(rest);
            if (uncommented.StartsWith('[') && uncommented.EndsWith(']'))
            {
                if (string.Equals(key, "related", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string item in YamlPrimitives.ParseFlowStringArray(uncommented))
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
                i = ParseIndentedSequenceItems(lines, i + 1, endLine, key, related);
                continue;
            }

            scalars[key] = ParseScalar(rest);
            i++;
        }

        return new ParsedYamlFrontmatter(true, endLine + 1, scalars, related);
    }

    public static string ParseScalar(string raw) =>
        YamlPrimitives.ParseScalar(raw, decodeCommonEscapes: true);

    private static int ParseIndentedSequenceItems(
        string[] lines,
        int startIndex,
        int endLine,
        string key,
        List<YamlRelatedLinkEntry> related)
    {
        bool isRelatedKey = string.Equals(key, "related", StringComparison.OrdinalIgnoreCase);
        int i = startIndex;

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

            if (YamlPrimitives.TryParseBlockHeader(firstEntry, out char itemStyle, out char itemChomp, out int itemIndentHint))
            {
                var (itemBlockVal, nextItemIdx) = YamlPrimitives.ConsumeBlockScalar(
                    lines,
                    i + 1,
                    endLine,
                    dashIndent,
                    itemStyle,
                    itemChomp,
                    itemIndentHint);
                if (isRelatedKey && !string.IsNullOrWhiteSpace(itemBlockVal))
                {
                    related.Add(new YamlRelatedLinkEntry(itemBlockVal.Trim(), itemLineNumber));
                }

                i = nextItemIdx;
                continue;
            }

            Match objectFieldMatch = TopKeyRegex().Match(firstEntry);
            if (objectFieldMatch.Success)
            {
                i = ParseSequenceObjectFields(lines, i, endLine, itemLine, objectFieldMatch, isRelatedKey, related);
            }
            else
            {
                string parsedItem = ParseScalar(firstEntry);
                if (isRelatedKey && !string.IsNullOrWhiteSpace(parsedItem))
                {
                    related.Add(new YamlRelatedLinkEntry(parsedItem.Trim(), itemLineNumber));
                }

                i++;
            }
        }

        return i;
    }

    private static int ParseSequenceObjectFields(
        string[] lines,
        int itemIndex,
        int endLine,
        string itemLine,
        Match objectFieldMatch,
        bool isRelatedKey,
        List<YamlRelatedLinkEntry> related)
    {
        string firstFieldKey = objectFieldMatch.Groups[1].Value;
        string firstFieldRaw = objectFieldMatch.Groups[2].Value.Trim();
        int firstFieldIndent = itemLine.IndexOf(firstFieldKey, StringComparison.Ordinal);
        int itemLineNumber = itemIndex + 1;
        int i = itemIndex;

        if (YamlPrimitives.TryParseBlockHeader(firstFieldRaw, out char fStyle, out char fChomp, out int fIndentHint))
        {
            var (fVal, nextFIdx) = YamlPrimitives.ConsumeBlockScalar(
                lines,
                i + 1,
                endLine,
                firstFieldIndent,
                fStyle,
                fChomp,
                fIndentHint);
            if (isRelatedKey && IsLinkKey(firstFieldKey) && !string.IsNullOrWhiteSpace(fVal))
            {
                related.Add(new YamlRelatedLinkEntry(fVal.Trim(), itemLineNumber));
            }

            i = nextFIdx;
        }
        else
        {
            string parsedFirst = ParseScalar(firstFieldRaw);
            if (isRelatedKey && IsLinkKey(firstFieldKey) && !string.IsNullOrWhiteSpace(parsedFirst))
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

                if (YamlPrimitives.TryParseBlockHeader(subRaw, out char sStyle, out char sChomp, out int sIndentHint))
                {
                    var (sVal, nextSIdx) = YamlPrimitives.ConsumeBlockScalar(
                        lines,
                        i + 1,
                        endLine,
                        subIndent,
                        sStyle,
                        sChomp,
                        sIndentHint);
                    if (isRelatedKey && IsLinkKey(subKey) && !string.IsNullOrWhiteSpace(sVal))
                    {
                        related.Add(new YamlRelatedLinkEntry(sVal.Trim(), subLineNumber));
                    }

                    i = nextSIdx;
                    continue;
                }

                string parsedSub = ParseScalar(subRaw);
                if (isRelatedKey && IsLinkKey(subKey) && !string.IsNullOrWhiteSpace(parsedSub))
                {
                    related.Add(new YamlRelatedLinkEntry(parsedSub.Trim(), subLineNumber));
                }
            }

            i++;
        }

        return i;
    }

    private static bool IsLinkKey(string key) =>
        string.Equals(key, "link", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "href", StringComparison.OrdinalIgnoreCase);
}

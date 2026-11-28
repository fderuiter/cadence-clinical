using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    private static ParsedDependabotConfig ParseDependabotConfig(string content, List<string> parseErrors)
    {
        string[] rawLines = content.ReplaceLineEndings("\n").Split('\n');
        string[] lines = new string[rawLines.Length];
        for (int i = 0; i < rawLines.Length; i++)
        {
            lines[i] = YamlPrimitives.StripLineComment(rawLines[i]);
        }

        var config = new ParsedDependabotConfig();
        bool inUpdates = false, inGroups = false, inIgnore = false;
        int updatesIndent = -1, updateItemIndent = -1;
        int groupsKeyIndent = -1, groupItemIndent = -1;
        int ignoreKeyIndent = -1, ignoreItemIndent = -1;
        DependabotUpdateEntry? currentUpdate = null;
        DependabotGroup? currentGroup = null;
        DependabotIgnoreEntry? currentIgnore = null;

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = YamlPrimitives.CountLeadingSpaces(line);
            string trimmed = line[indent..].TrimEnd();
            if (trimmed is "---" or "...")
            {
                continue;
            }

            if (indent == 0)
            {
                inUpdates = inGroups = inIgnore = false;
                currentUpdate = null;
                currentGroup = null;
                currentIgnore = null;

                if (YamlPrimitives.TrySplitMapping(trimmed, out string rootKey, out string rootVal))
                {
                    if (string.Equals(rootKey, "version", StringComparison.Ordinal))
                    {
                        config.Version = YamlPrimitives.UnquoteScalar(rootVal);
                    }
                    else if (string.Equals(rootKey, "updates", StringComparison.Ordinal))
                    {
                        inUpdates = true;
                        updatesIndent = 0;
                        updateItemIndent = -1;
                    }
                }

                continue;
            }

            if (!inUpdates || indent <= updatesIndent)
            {
                continue;
            }

            if (YamlPrimitives.IsSequenceItem(trimmed, out string afterUpdateDash) &&
                (updateItemIndent == -1 || indent == updateItemIndent))
            {
                updateItemIndent = indent;
                currentUpdate = new DependabotUpdateEntry();
                config.Updates.Add(currentUpdate);
                inGroups = inIgnore = false;
                groupsKeyIndent = groupItemIndent = ignoreKeyIndent = ignoreItemIndent = -1;
                currentGroup = null;
                currentIgnore = null;

                if (afterUpdateDash.Length > 0)
                {
                    int virtualIndent = indent + (trimmed.Length - afterUpdateDash.Length);
                    ApplyUpdatePropertyLine(
                        lines, ref lineIdx, virtualIndent, afterUpdateDash, currentUpdate,
                        ref inGroups, ref groupsKeyIndent, ref groupItemIndent, ref currentGroup,
                        ref inIgnore, ref ignoreKeyIndent, ref ignoreItemIndent, parseErrors);
                }

                continue;
            }

            if (currentUpdate is null)
            {
                continue;
            }

            if (inGroups && indent > groupsKeyIndent)
            {
                ApplyGroupSubsectionLine(lines, ref lineIdx, indent, trimmed, currentUpdate, ref groupItemIndent, ref currentGroup, parseErrors);
                continue;
            }

            if (inGroups && indent <= groupsKeyIndent)
            {
                inGroups = false;
                currentGroup = null;
            }

            if (inIgnore && indent > ignoreKeyIndent)
            {
                if (YamlPrimitives.IsSequenceItem(trimmed, out string afterIgnoreDash) &&
                    (ignoreItemIndent == -1 || indent == ignoreItemIndent))
                {
                    ignoreItemIndent = indent;
                    currentIgnore = new DependabotIgnoreEntry();
                    currentUpdate.Ignores.Add(currentIgnore);

                    if (afterIgnoreDash.Length > 0)
                    {
                        int virtualIndent = indent + (trimmed.Length - afterIgnoreDash.Length);
                        ApplyIgnorePropertyLine(lines, ref lineIdx, virtualIndent, afterIgnoreDash, currentIgnore, parseErrors);
                    }

                    continue;
                }

                if (currentIgnore is not null && (ignoreItemIndent == -1 || indent > ignoreItemIndent))
                {
                    ApplyIgnorePropertyLine(lines, ref lineIdx, indent, trimmed, currentIgnore, parseErrors);
                }

                continue;
            }

            if (inIgnore && indent <= ignoreKeyIndent)
            {
                inIgnore = false;
                currentIgnore = null;
            }

            if (indent > updateItemIndent)
            {
                ApplyUpdatePropertyLine(
                    lines, ref lineIdx, indent, trimmed, currentUpdate,
                    ref inGroups, ref groupsKeyIndent, ref groupItemIndent, ref currentGroup,
                    ref inIgnore, ref ignoreKeyIndent, ref ignoreItemIndent, parseErrors);
            }
        }

        return config;
    }

    private static void ApplyGroupSubsectionLine(
        string[] lines,
        ref int lineIdx,
        int indent,
        string trimmedLine,
        DependabotUpdateEntry update,
        ref int groupItemIndent,
        ref DependabotGroup? currentGroup,
        List<string> parseErrors)
    {
        if (!YamlPrimitives.TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        if (groupItemIndent == -1 || indent == groupItemIndent)
        {
            groupItemIndent = indent;
            if (!update.Groups.TryGetValue(key, out currentGroup))
            {
                currentGroup = new DependabotGroup { Name = key };
                update.Groups[key] = currentGroup;
                update.OrderedGroups.Add(currentGroup);
            }

            return;
        }

        if (currentGroup is null || indent <= groupItemIndent)
        {
            return;
        }

        switch (key)
        {
            case "patterns":
                foreach (string item in ReadStringListValue(lines, ref lineIdx, indent, val, parseErrors))
                {
                    currentGroup.Patterns.Add(item);
                }

                break;
            case "exclude-patterns":
                foreach (string item in ReadStringListValue(lines, ref lineIdx, indent, val, parseErrors))
                {
                    currentGroup.ExcludePatterns.Add(item);
                }

                break;
        }
    }

    private static void ApplyUpdatePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotUpdateEntry update,
        ref bool inGroups,
        ref int groupsKeyIndent,
        ref int groupItemIndent,
        ref DependabotGroup? currentGroup,
        ref bool inIgnore,
        ref int ignoreKeyIndent,
        ref int ignoreItemIndent,
        List<string> parseErrors)
    {
        if (!YamlPrimitives.TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "package-ecosystem":
                update.PackageEcosystem = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directory":
                update.Directory = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directories":
                update.Directories.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "target-branch":
                update.TargetBranch = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "open-pull-requests-limit":
                string rawLimit = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                update.RawOpenPullRequestsLimit = rawLimit;
                update.OpenPullRequestsLimit = int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLimit)
                    ? parsedLimit
                    : null;
                break;
            case "groups":
                inGroups = true;
                groupsKeyIndent = propIndent;
                groupItemIndent = -1;
                currentGroup = null;
                inIgnore = false;
                break;
            case "ignore":
                inIgnore = true;
                ignoreKeyIndent = propIndent;
                ignoreItemIndent = -1;
                inGroups = false;
                currentGroup = null;
                break;
        }
    }

    private static void ApplyIgnorePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotIgnoreEntry ignore,
        List<string> parseErrors)
    {
        if (!YamlPrimitives.TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "dependency-name":
                ignore.DependencyName = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "versions":
                ignore.HasVersions = true;
                ignore.Versions.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "update-types":
                ignore.HasUpdateTypes = true;
                ignore.UpdateTypes.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
        }
    }

    private static string ReadScalarValue(string[] lines, ref int lineIdx, int propIndent, string inlineVal)
    {
        if (!string.IsNullOrWhiteSpace(inlineVal))
        {
            return YamlPrimitives.UnquoteScalar(inlineVal);
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next >= 0)
        {
            int nextIndent = YamlPrimitives.CountLeadingSpaces(lines[next]);
            string nextTrimmed = lines[next][nextIndent..].Trim();
            if (nextIndent > propIndent && !nextTrimmed.StartsWith('-') && !YamlPrimitives.TrySplitMapping(nextTrimmed, out _, out _))
            {
                lineIdx = next;
                return YamlPrimitives.UnquoteScalar(nextTrimmed);
            }
        }

        return string.Empty;
    }

    private static List<string> ReadStringListValue(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string inlineVal,
        List<string> parseErrors)
    {
        string trimmedInline = inlineVal.Trim();
        if (trimmedInline.StartsWith('['))
        {
            return ParseMultiLineFlowStringArray(lines, ref lineIdx, trimmedInline, parseErrors);
        }

        if (trimmedInline.Length > 0)
        {
            parseErrors.Add($"Expected YAML sequence on line {lineIdx + 1}, found scalar '{trimmedInline}'.");
            return [];
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next < 0)
        {
            return [];
        }

        int nextIndent = YamlPrimitives.CountLeadingSpaces(lines[next]);
        if (nextIndent <= propIndent)
        {
            return [];
        }

        string nextTrimmed = lines[next][nextIndent..].Trim();
        if (nextTrimmed.StartsWith('['))
        {
            lineIdx = next;
            return ParseMultiLineFlowStringArray(lines, ref lineIdx, nextTrimmed, parseErrors);
        }

        var items = new List<string>();
        int seqIndent = -1;
        while (true)
        {
            int peek = PeekNextContentLine(lines, lineIdx + 1);
            if (peek < 0)
            {
                break;
            }

            int peekIndent = YamlPrimitives.CountLeadingSpaces(lines[peek]);
            if (peekIndent <= propIndent)
            {
                break;
            }

            string peekTrimmed = lines[peek][peekIndent..].TrimEnd();
            if (!YamlPrimitives.IsSequenceItem(peekTrimmed, out string itemText))
            {
                break;
            }

            if (seqIndent == -1)
            {
                seqIndent = peekIndent;
            }
            else if (peekIndent != seqIndent)
            {
                break;
            }

            lineIdx = peek;
            items.Add(YamlPrimitives.UnquoteScalar(itemText));
        }

        return items;
    }

    private static List<string> ParseMultiLineFlowStringArray(
        string[] lines,
        ref int lineIdx,
        string initialSegment,
        List<string> parseErrors)
    {
        var combined = new StringBuilder(initialSegment);
        while (FindMatchingCloseBracket(combined.ToString()) < 0)
        {
            if (lineIdx + 1 >= lines.Length)
            {
                parseErrors.Add("Unclosed flow sequence '[' in dependabot configuration.");
                return [];
            }

            lineIdx++;
            combined.Append(' ').Append(lines[lineIdx].Trim());
        }

        string full = combined.ToString().Trim();
        int closeIdx = FindMatchingCloseBracket(full);
        if (closeIdx < 0)
        {
            parseErrors.Add("Malformed flow sequence '[' in dependabot configuration.");
            return [];
        }

        return YamlPrimitives.ParseFlowStringArray(full[..(closeIdx + 1)], decodeCommonEscapes: false);
    }

    private static int FindMatchingCloseBracket(string text)
    {
        int depth = 0;
        char quote = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
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
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']' && --depth == 0)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static int PeekNextContentLine(string[] lines, int startIdx)
    {
        for (int i = startIdx; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

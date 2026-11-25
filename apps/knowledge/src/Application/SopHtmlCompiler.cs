using System;
using System.Collections.Generic;
using System.Text;

namespace Cadence.Apps.Knowledge.Application;

/// <summary>
/// Deterministic, zero-dependency Markdown-to-HTML compiler for controlled GxP SOP snapshots.
/// Escapes all raw HTML characters prior to inline formatting and assigns deterministic heading IDs
/// so every published SOP section and persona spotlight anchor resolves to a navigable DOM target.
/// </summary>
public static class SopHtmlCompiler
{
    public static string Compile(
        string? markdown,
        string? defaultPrimaryAnchor = null,
        IReadOnlyDictionary<string, string>? headingIdOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        string normalized = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] lines = normalized.Split('\n');
        var sb = new StringBuilder(normalized.Length + 256);
        var paragraphLines = new List<string>();
        var listItems = new List<string>();
        string? activeListTag = null;
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        bool assignedPrimaryAnchor = false;
        string? normalizedPrimaryAnchor = SanitizeAnchorId(defaultPrimaryAnchor);

        void FlushParagraph()
        {
            if (paragraphLines.Count == 0)
            {
                return;
            }

            string joined = string.Join(" ", paragraphLines);
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append("<p>");
            sb.Append(FormatInline(joined));
            sb.Append("</p>");
            paragraphLines.Clear();
        }

        void FlushList()
        {
            if (activeListTag is null || listItems.Count == 0)
            {
                listItems.Clear();
                activeListTag = null;
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append('<').Append(activeListTag).Append('>');
            foreach (string item in listItems)
            {
                sb.Append("\n  <li>");
                sb.Append(FormatInline(item));
                sb.Append("</li>");
            }

            sb.Append("\n</").Append(activeListTag).Append('>');
            listItems.Clear();
            activeListTag = null;
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (TryParseHeading(line, out int level, out string headingText, out string? inlineAnchor))
            {
                FlushParagraph();
                FlushList();

                string resolvedId = ResolveHeadingId(
                    headingText,
                    inlineAnchor,
                    !assignedPrimaryAnchor ? normalizedPrimaryAnchor : null,
                    headingIdOverrides,
                    usedIds);
                assignedPrimaryAnchor = true;

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                string tag = $"h{level}";
                sb.Append('<').Append(tag).Append(" id=\"").Append(EscapeHtml(resolvedId)).Append("\">");
                sb.Append(FormatInline(headingText));
                sb.Append("</").Append(tag).Append('>');
                continue;
            }

            if (TryParseOrderedListItem(line, out string orderedItem))
            {
                FlushParagraph();
                if (!string.Equals(activeListTag, "ol", StringComparison.Ordinal))
                {
                    FlushList();
                    activeListTag = "ol";
                }

                listItems.Add(orderedItem);
                continue;
            }

            if (TryParseUnorderedListItem(line, out string unorderedItem))
            {
                FlushParagraph();
                if (!string.Equals(activeListTag, "ul", StringComparison.Ordinal))
                {
                    FlushList();
                    activeListTag = "ul";
                }

                listItems.Add(unorderedItem);
                continue;
            }

            FlushList();
            paragraphLines.Add(line);
        }

        FlushParagraph();
        FlushList();

        return sb.ToString();
    }

    public static string? EnsureCompiledHtml(
        string? markdown,
        string? existingHtml,
        string? defaultPrimaryAnchor = null)
    {
        if (string.IsNullOrWhiteSpace(existingHtml))
        {
            return string.IsNullOrWhiteSpace(markdown)
                ? existingHtml
                : Compile(markdown, defaultPrimaryAnchor);
        }

        if (!string.IsNullOrWhiteSpace(markdown) && IsHtmlIncompleteForMarkdown(markdown, existingHtml))
        {
            return Compile(markdown, defaultPrimaryAnchor);
        }

        return existingHtml;
    }

    public static bool HasCompleteHeadingIdsAndSteps(string? markdown, string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.IsNullOrWhiteSpace(markdown);
        }

        if (!string.IsNullOrWhiteSpace(markdown) && IsHtmlIncompleteForMarkdown(markdown, html))
        {
            return false;
        }

        return !ContainsHeadingWithoutId(html);
    }

    public static string SlugifyHeading(string headingText)
    {
        if (string.IsNullOrWhiteSpace(headingText))
        {
            return "section";
        }

        string expanded = headingText.Replace("&", " and ", StringComparison.Ordinal);
        var sb = new StringBuilder(expanded.Length + 8);
        bool prevHyphen = false;

        foreach (char ch in expanded)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevHyphen = false;
            }
            else if (ch is '\'' or '"' or '`')
            {
                continue;
            }
            else if (sb.Length > 0 && !prevHyphen)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.Length > 0 ? sb.ToString() : "section";
    }

    private static bool IsHtmlIncompleteForMarkdown(string markdown, string html)
    {
        if (ContainsHeadingWithoutId(html))
        {
            return true;
        }

        if (!html.Contains("<ol>", StringComparison.OrdinalIgnoreCase)
            && ContainsOrderedStepLine(markdown))
        {
            return true;
        }

        if (!html.Contains("id=\"", StringComparison.OrdinalIgnoreCase)
            && ContainsMarkdownHeading(markdown))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsHeadingWithoutId(string html) =>
        html.Contains("<h1>", StringComparison.OrdinalIgnoreCase)
        || html.Contains("<h2>", StringComparison.OrdinalIgnoreCase)
        || html.Contains("<h3>", StringComparison.OrdinalIgnoreCase)
        || html.Contains("<h4>", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsOrderedStepLine(string markdown)
    {
        foreach (ReadOnlySpan<char> rawLine in markdown.AsSpan().EnumerateLines())
        {
            if (IsOrderedListItemSpan(rawLine.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMarkdownHeading(string markdown)
    {
        foreach (ReadOnlySpan<char> rawLine in markdown.AsSpan().EnumerateLines())
        {
            if (IsHeadingSpan(rawLine.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOrderedListItemSpan(ReadOnlySpan<char> line)
    {
        int idx = 0;
        while (idx < line.Length && char.IsAsciiDigit(line[idx]))
        {
            idx++;
        }

        if (idx == 0 || idx + 1 >= line.Length || line[idx] != '.' || !char.IsWhiteSpace(line[idx + 1]))
        {
            return false;
        }

        return !line[(idx + 2)..].Trim().IsEmpty;
    }

    private static bool IsHeadingSpan(ReadOnlySpan<char> line)
    {
        int hashes = 0;
        while (hashes < line.Length && hashes < 6 && line[hashes] == '#')
        {
            hashes++;
        }

        if (hashes == 0 || hashes >= line.Length || !char.IsWhiteSpace(line[hashes]))
        {
            return false;
        }

        ReadOnlySpan<char> rest = line[(hashes + 1)..].Trim();
        if (rest.IsEmpty)
        {
            return false;
        }

        if (rest[^1] == '}')
        {
            int braceOpen = rest.LastIndexOf("{#", StringComparison.Ordinal);
            if (braceOpen > 0)
            {
                ReadOnlySpan<char> rawId = rest[(braceOpen + 2)..^1].Trim();
                if (!rawId.IsEmpty)
                {
                    rest = rest[..braceOpen].TrimEnd();
                }
            }
        }

        return !rest.IsEmpty;
    }

    private static string ResolveHeadingId(
        string headingText,
        string? inlineAnchor,
        string? primaryAnchor,
        IReadOnlyDictionary<string, string>? headingIdOverrides,
        HashSet<string> usedIds)
    {
        string? candidate = SanitizeAnchorId(inlineAnchor);

        if (string.IsNullOrEmpty(candidate)
            && headingIdOverrides is not null
            && headingIdOverrides.TryGetValue(headingText, out string? mapped))
        {
            candidate = SanitizeAnchorId(mapped);
        }

        if (string.IsNullOrEmpty(candidate) && !string.IsNullOrEmpty(primaryAnchor))
        {
            candidate = primaryAnchor;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            candidate = SlugifyHeading(headingText);
        }

        string unique = candidate;
        int suffix = 2;
        while (!usedIds.Add(unique))
        {
            unique = $"{candidate}-{suffix}";
            suffix++;
        }

        return unique;
    }

    private static string? SanitizeAnchorId(string? rawAnchor)
    {
        if (string.IsNullOrWhiteSpace(rawAnchor))
        {
            return null;
        }

        ReadOnlySpan<char> trimmed = rawAnchor.AsSpan().Trim().TrimStart('#');
        if (trimmed.IsEmpty)
        {
            return null;
        }

        var sb = new StringBuilder(trimmed.Length);
        bool prevHyphen = false;
        foreach (char ch in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevHyphen = false;
            }
            else if ((ch is '-' or '_') && sb.Length > 0 && !prevHyphen)
            {
                sb.Append(ch);
                prevHyphen = ch == '-';
            }
        }

        while (sb.Length > 0 && sb[^1] is '-' or '_')
        {
            sb.Length--;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static bool TryParseHeading(
        string line,
        out int level,
        out string headingText,
        out string? inlineAnchor)
    {
        level = 0;
        headingText = string.Empty;
        inlineAnchor = null;

        int hashes = 0;
        while (hashes < line.Length && hashes < 6 && line[hashes] == '#')
        {
            hashes++;
        }

        if (hashes == 0 || hashes >= line.Length || !char.IsWhiteSpace(line[hashes]))
        {
            return false;
        }

        level = Math.Clamp(hashes, 1, 4);
        string rest = line[(hashes + 1)..].Trim();
        if (rest.Length == 0)
        {
            return false;
        }

        if (rest.EndsWith('}'))
        {
            int braceOpen = rest.LastIndexOf("{#", StringComparison.Ordinal);
            if (braceOpen > 0)
            {
                string rawId = rest[(braceOpen + 2)..^1].Trim();
                if (rawId.Length > 0)
                {
                    inlineAnchor = rawId;
                    rest = rest[..braceOpen].TrimEnd();
                }
            }
        }

        headingText = rest;
        return headingText.Length > 0;
    }

    private static bool TryParseOrderedListItem(string line, out string itemText)
    {
        itemText = string.Empty;
        int idx = 0;
        while (idx < line.Length && char.IsAsciiDigit(line[idx]))
        {
            idx++;
        }

        if (idx == 0 || idx + 1 >= line.Length || line[idx] != '.' || !char.IsWhiteSpace(line[idx + 1]))
        {
            return false;
        }

        itemText = line[(idx + 2)..].Trim();
        return itemText.Length > 0;
    }

    private static bool TryParseUnorderedListItem(string line, out string itemText)
    {
        itemText = string.Empty;
        if (line.Length < 3 || line[0] is not ('-' or '*') || !char.IsWhiteSpace(line[1]))
        {
            return false;
        }

        itemText = line[2..].Trim();
        return itemText.Length > 0;
    }

    private static string FormatInline(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(rawText.Length + 32);
        int cursor = 0;

        while (cursor < rawText.Length)
        {
            int codeStart = rawText.IndexOf('`', cursor);
            if (codeStart < 0)
            {
                sb.Append(FormatEmphasisOnEscaped(EscapeHtml(rawText[cursor..])));
                break;
            }

            int codeEnd = rawText.IndexOf('`', codeStart + 1);
            if (codeEnd < 0)
            {
                sb.Append(FormatEmphasisOnEscaped(EscapeHtml(rawText[cursor..])));
                break;
            }

            if (codeStart > cursor)
            {
                sb.Append(FormatEmphasisOnEscaped(EscapeHtml(rawText[cursor..codeStart])));
            }

            sb.Append("<code>");
            sb.Append(EscapeHtml(rawText[(codeStart + 1)..codeEnd]));
            sb.Append("</code>");
            cursor = codeEnd + 1;
        }

        return sb.ToString();
    }

    private static string FormatEmphasisOnEscaped(string escaped)
    {
        if (!escaped.Contains('*', StringComparison.Ordinal))
        {
            return escaped;
        }

        string withStrong = ReplaceSymmetricDelimiter(escaped, "**", "<strong>", "</strong>");
        return ReplaceSymmetricDelimiter(withStrong, "*", "<em>", "</em>");
    }

    private static string ReplaceSymmetricDelimiter(
        string text,
        string delimiter,
        string openTag,
        string closeTag)
    {
        int first = text.IndexOf(delimiter, StringComparison.Ordinal);
        if (first < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 16);
        int cursor = 0;
        while (cursor < text.Length)
        {
            int start = text.IndexOf(delimiter, cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(text, cursor, text.Length - cursor);
                break;
            }

            int contentStart = start + delimiter.Length;
            int end = text.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
            if (end < 0 || end == contentStart)
            {
                sb.Append(text, cursor, text.Length - cursor);
                break;
            }

            sb.Append(text, cursor, start - cursor);
            sb.Append(openTag);
            sb.Append(text, contentStart, end - contentStart);
            sb.Append(closeTag);
            cursor = end + delimiter.Length;
        }

        return sb.ToString();
    }

    public static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length + 16);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&#39;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}

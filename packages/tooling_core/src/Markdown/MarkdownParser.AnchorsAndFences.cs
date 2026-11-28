using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Markdown;

public static partial class MarkdownParser
{
    [GeneratedRegex(@"^(`{3,}|~{3,})(.*)$")]
    private static partial Regex CodeFenceLineRegex();

    [GeneratedRegex(@"\b(?:id|name)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlIdAttrRegex();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*$")]
    private static partial Regex HeadingLineRegex();

    [GeneratedRegex(@"\s+#+\s*$")]
    private static partial Regex TrailingHashesRegex();

    [GeneratedRegex(@"\s*\{#([^}\s]+)\}\s*$")]
    private static partial Regex CustomHeadingIdRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)")]
    private static partial Regex InlineImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex InlineLinkTextRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeTextRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*|__([^_]+)__|\*([^*]+)\*")]
    private static partial Regex InlineEmphasisRegex();

    [GeneratedRegex(@"(`[^`]+`)|<[^>]+>")]
    private static partial Regex CodeSpanOrHtmlTagRegex();

    [GeneratedRegex(@"[\s~`!@#$%^&*()\-_+=[\]{}|\\;:""'<>,.?/]+")]
    private static partial Regex VitePressSpecialCharsRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiHyphenRegex();

    public static HashSet<string> ExtractMarkdownAnchors(string markdownContent)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var vpCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var ghCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        char fenceChar = '\0';
        int fenceLen = 0;

        foreach (string rawLine in markdownContent.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (UpdateCodeFenceState(line, ref fenceChar, ref fenceLen) || fenceLen > 0)
            {
                continue;
            }

            string trimmed = line.TrimStart();

            var attrMatches = HtmlIdAttrRegex().Matches(line);
            for (int a = 0; a < attrMatches.Count; a++)
            {
                Match attrMatch = attrMatches[a];
                if (attrMatch.Groups[1].Success && !string.IsNullOrEmpty(attrMatch.Groups[1].Value))
                {
                    anchors.Add(attrMatch.Groups[1].Value);
                }
            }

            Match headingMatch = HeadingLineRegex().Match(trimmed);
            if (!headingMatch.Success)
            {
                continue;
            }

            string headingText = TrailingHashesRegex().Replace(headingMatch.Groups[2].Value, string.Empty);
            Match customIdMatch = CustomHeadingIdRegex().Match(headingText);
            if (customIdMatch.Success)
            {
                anchors.Add(customIdMatch.Groups[1].Value);
                headingText = headingText[..customIdMatch.Index].Trim();
            }

            string vpSlug = VitePressSlugify(headingText);
            if (!string.IsNullOrEmpty(vpSlug))
            {
                vpCounts.TryGetValue(vpSlug, out int vpSeen);
                vpCounts[vpSlug] = vpSeen + 1;
                anchors.Add(vpSeen == 0 ? vpSlug : $"{vpSlug}-{vpSeen}");
            }

            string ghSlug = GitHubSlugify(headingText);
            if (!string.IsNullOrEmpty(ghSlug))
            {
                ghCounts.TryGetValue(ghSlug, out int ghSeen);
                ghCounts[ghSlug] = ghSeen + 1;
                anchors.Add(ghSeen == 0 ? ghSlug : $"{ghSlug}-{ghSeen}");
            }
        }

        return anchors;
    }

    public static List<MarkdownValidationIssue> ValidateCodeFencesAndHeadings(
        string repoRoot,
        string fullMarkdownPath,
        string[] lines)
    {
        var issues = new List<MarkdownValidationIssue>();
        string relPath = Path.GetRelativePath(repoRoot, fullMarkdownPath).Replace('\\', '/');
        char fenceChar = '\0';
        int fenceLen = 0;
        string fenceInfo = string.Empty;
        int fenceStartLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.TrimStart();
            Match fenceMatch = CodeFenceLineRegex().Match(trimmed);

            if (fenceMatch.Success)
            {
                string marker = fenceMatch.Groups[1].Value;
                string rawRest = fenceMatch.Groups[2].Value;
                string rest = rawRest.Trim();
                bool isInlineBacktickSpan = marker[0] == '`' && rawRest.Contains('`', StringComparison.Ordinal);

                if (fenceLen == 0)
                {
                    if (!isInlineBacktickSpan)
                    {
                        fenceChar = marker[0];
                        fenceLen = marker.Length;
                        fenceInfo = rest;
                        fenceStartLine = i + 1;
                        continue;
                    }
                }
                else if (marker[0] == fenceChar && rest.Length == 0)
                {
                    if (marker.Length == fenceLen)
                    {
                        fenceChar = '\0';
                        fenceLen = 0;
                        fenceInfo = string.Empty;
                        fenceStartLine = 0;
                        continue;
                    }

                    if (marker.Length > fenceLen)
                    {
                        issues.Add(new MarkdownValidationIssue(
                            relPath,
                            marker,
                            $"{relPath}:{i + 1} - Mismatched closing code fence length: opened with {fenceLen} '{fenceChar}' at line {fenceStartLine}, closed with {marker.Length}.",
                            i + 1));
                        fenceChar = '\0';
                        fenceLen = 0;
                        fenceInfo = string.Empty;
                        fenceStartLine = 0;
                        continue;
                    }

                    if (!IsMarkdownFenceInfo(fenceInfo))
                    {
                        issues.Add(new MarkdownValidationIssue(
                            relPath,
                            marker,
                            $"{relPath}:{i + 1} - Mismatched closing code fence length: opened with {fenceLen} '{fenceChar}' at line {fenceStartLine}, closed with {marker.Length}.",
                            i + 1));
                        continue;
                    }
                }
            }

            if (fenceLen > 0)
            {
                if (fenceInfo.Length == 0 && HeadingLineRegex().IsMatch(trimmed))
                {
                    issues.Add(new MarkdownValidationIssue(
                        relPath,
                        line.Trim(),
                        $"{relPath}:{i + 1} - Swallowed heading inside unlabeled code fence opened at line {fenceStartLine}: '{line.Trim()}'",
                        i + 1));
                }

                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                int hashCount = 0;
                while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                {
                    hashCount++;
                }

                if (hashCount >= 1 && hashCount <= 6 && hashCount < trimmed.Length)
                {
                    char nextChar = trimmed[hashCount];
                    if (nextChar != ' ' && nextChar != '\t' && nextChar != '#' && !char.IsDigit(nextChar))
                    {
                        issues.Add(new MarkdownValidationIssue(
                            relPath,
                            line.Trim(),
                            $"{relPath}:{i + 1} - Heading missing space after '#': '{line.Trim()}'",
                            i + 1));
                    }
                }
            }
        }

        if (fenceLen > 0)
        {
            string openMarker = new(fenceChar, fenceLen);
            issues.Add(new MarkdownValidationIssue(
                relPath,
                openMarker,
                $"{relPath}:{fenceStartLine} - Unclosed code fence '{openMarker}' reached end of file.",
                fenceStartLine));
        }

        return issues;
    }

    private static bool IsMarkdownFenceInfo(string fenceInfo)
    {
        if (string.IsNullOrEmpty(fenceInfo))
        {
            return false;
        }

        int spaceIdx = fenceInfo.IndexOfAny([' ', '\t']);
        string lang = spaceIdx >= 0 ? fenceInfo[..spaceIdx] : fenceInfo;
        return lang.Equals("markdown", StringComparison.OrdinalIgnoreCase) ||
               lang.Equals("md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UpdateCodeFenceState(string line, ref char fenceChar, ref int fenceLen)
    {
        Match fenceMatch = CodeFenceLineRegex().Match(line.TrimStart());
        if (!fenceMatch.Success)
        {
            return false;
        }

        string marker = fenceMatch.Groups[1].Value;
        string rawRest = fenceMatch.Groups[2].Value;
        string rest = rawRest.Trim();
        if (fenceLen == 0)
        {
            if (marker[0] == '`' && rawRest.Contains('`', StringComparison.Ordinal))
            {
                return false;
            }

            fenceChar = marker[0];
            fenceLen = marker.Length;
            return true;
        }

        if (marker[0] == fenceChar && marker.Length >= fenceLen && rest.Length == 0)
        {
            fenceChar = '\0';
            fenceLen = 0;
            return true;
        }

        return false;
    }

    private static HashSet<string> GetMarkdownAnchors(
        string repoRoot,
        string fullMarkdownPath,
        Dictionary<string, HashSet<string>> anchorCache,
        GitIndexReader? indexReader)
    {
        if (anchorCache.TryGetValue(fullMarkdownPath, out var cached))
        {
            return cached;
        }

        string content;
        if (indexReader is not null &&
            GitIndexReader.TryNormalizeRelativePath(repoRoot, fullMarkdownPath) is { Length: > 0 } rel &&
            indexReader.TryReadText(rel, out string indexedText))
        {
            content = indexedText;
        }
        else
        {
            content = File.ReadAllText(fullMarkdownPath);
        }

        var extracted = ExtractMarkdownAnchors(content);
        anchorCache[fullMarkdownPath] = extracted;
        return extracted;
    }

    private static string StripInlineMarkdown(string rawHeading)
    {
        string text = InlineImageRegex().Replace(rawHeading, "$1");
        text = InlineLinkTextRegex().Replace(text, "$1");
        text = CodeSpanOrHtmlTagRegex().Replace(text, "$1");
        text = InlineCodeTextRegex().Replace(text, "$1");
        text = InlineEmphasisRegex().Replace(text, static m =>
        {
            if (m.Groups[1].Success)
            {
                return m.Groups[1].Value;
            }

            if (m.Groups[2].Success)
            {
                return m.Groups[2].Value;
            }

            return m.Groups[3].Value;
        });
        text = text
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&#39;", "'", StringComparison.Ordinal);
        return text.Trim();
    }

    private static string VitePressSlugify(string rawHeading)
    {
        string normalized = StripInlineMarkdown(rawHeading).Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char ch in normalized)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark || ch <= '\u001f')
            {
                continue;
            }

            sb.Append(ch);
        }

        string slug = VitePressSpecialCharsRegex().Replace(sb.ToString(), "-");
        slug = MultiHyphenRegex().Replace(slug, "-").Trim('-');
        if (slug.Length > 0 && char.IsAsciiDigit(slug[0]))
        {
            slug = "_" + slug;
        }

        return slug.ToLowerInvariant();
    }

    private static string GitHubSlugify(string rawHeading)
    {
        string cleaned = StripInlineMarkdown(rawHeading).ToLowerInvariant();
        var sb = new StringBuilder(cleaned.Length);
        foreach (char ch in cleaned)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append('-');
            }
        }

        return sb.ToString();
    }
}

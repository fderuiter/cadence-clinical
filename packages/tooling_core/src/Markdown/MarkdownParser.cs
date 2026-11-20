using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Markdown;

public record MarkdownValidationIssue(
    string FilePath,
    string TargetLink,
    string Message,
    int LineNumber = 0
);

public static partial class MarkdownParser
{
    // Matches inline code spans first so `[text](link)` inside backticks is skipped while [`code`](link) is checked.
    private static readonly Regex InlineCodeOrLinkRegex = new(
        @"`[^`\r\n]+`|\[((?:[^\]\r\n]|\\\])+)\]\(([^)\r\n]+)\)",
        RegexOptions.Compiled);

    // A path into a .NET test project folder (tests/Cadence.UnitTests/..., tests/Cadence.IntegrationTests/...):
    // the first segment starts with a capital letter. Lower-case paths such as tests/fixtures or a web app's own
    // tests/ folder are not matched.
    private static readonly Regex TestProjectPathRegex = new(
        @"(?<![A-Za-z0-9_./-])tests/[A-Z][A-Za-z0-9_.-]*(?:/[A-Za-z0-9_.-]+)*/?",
        RegexOptions.Compiled);

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

    private static readonly string[] IgnoredSubstrings =
    [
        "placeholder", "your-", "example", "path-to-new-adr", "node.js", "..."
    ];

    public static List<MarkdownValidationIssue> ValidateWorkspaceLinks(
        string repoRoot,
        IEnumerable<string> filesToScan)
    {
        var issues = new List<MarkdownValidationIssue>();
        var anchorCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in filesToScan)
        {
            if (file.Contains("node_modules") || file.Contains(".venv") || file.Contains(".git")) continue;
            if (!File.Exists(file)) continue;

            string fullSourceFile = Path.GetFullPath(file);
            string relativeFile = Path.GetRelativePath(repoRoot, fullSourceFile).Replace('\\', '/');
            bool isHistoricalDoc = relativeFile.StartsWith("docs/adr/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/migration/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/specs/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/MIGRATION_WORKFLOW.md", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/AGENTS.md", StringComparison.OrdinalIgnoreCase);

            try
            {
                string[] lines = File.ReadAllLines(fullSourceFile);
                char fenceChar = '\0';
                int fenceLen = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (UpdateCodeFenceState(lines[i], ref fenceChar, ref fenceLen) || fenceLen > 0)
                    {
                        continue;
                    }

                    var matches = InlineCodeOrLinkRegex.Matches(lines[i]);
                    for (int mIdx = 0; mIdx < matches.Count; mIdx++)
                    {
                        Match m = matches[mIdx];
                        if (!m.Groups[2].Success)
                        {
                            continue;
                        }

                        string rawTarget = StripOptionalLinkTitle(m.Groups[2].Value.Trim());
                        if (string.IsNullOrEmpty(rawTarget) ||
                            rawTarget.Contains("://", StringComparison.Ordinal) ||
                            rawTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                            rawTarget.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                            rawTarget.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                            rawTarget.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        int hashIdx = rawTarget.IndexOf('#');
                        string beforeHash = hashIdx >= 0 ? rawTarget[..hashIdx] : rawTarget;
                        string rawFragment = hashIdx >= 0 ? rawTarget[(hashIdx + 1)..].Trim() : string.Empty;

                        // Same-page heading anchor link (#section)
                        if (rawTarget.StartsWith('#'))
                        {
                            ValidateAnchorFragment(
                                fullSourceFile,
                                rawFragment,
                                relativeFile,
                                rawTarget,
                                i + 1,
                                anchorCache,
                                issues);
                            continue;
                        }

                        // Strip queries and pytest selectors from the file portion
                        string target = beforeHash
                            .Split('?')[0]
                            .Split(["::"], StringSplitOptions.None)[0]
                            .Trim();
                        if (string.IsNullOrEmpty(target))
                        {
                            continue;
                        }

                        // Ignore placeholders
                        if (IgnoredSubstrings.Any(ign => target.Contains(ign, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string fileDir = Path.GetDirectoryName(fullSourceFile) ?? repoRoot;
                        string decodedTarget = UnescapeUriOrRaw(target);

                        // Try relative to file directory
                        string pathFromDir = Path.GetFullPath(Path.Combine(fileDir, decodedTarget));
                        if (File.Exists(pathFromDir) || Directory.Exists(pathFromDir))
                        {
                            ValidateResolvedTargetAnchor(
                                pathFromDir,
                                rawFragment,
                                relativeFile,
                                rawTarget,
                                i + 1,
                                anchorCache,
                                issues);
                            continue;
                        }

                        // Try relative to repo root
                        string pathFromRoot = Path.GetFullPath(Path.Combine(repoRoot, decodedTarget.TrimStart('/')));
                        if (File.Exists(pathFromRoot) || Directory.Exists(pathFromRoot))
                        {
                            ValidateResolvedTargetAnchor(
                                pathFromRoot,
                                rawFragment,
                                relativeFile,
                                rawTarget,
                                i + 1,
                                anchorCache,
                                issues);
                            continue;
                        }

                        // Fallback 1: Check .cs equivalent or src/ directory for transcribed C# code
                        if (target.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                            target.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                            target.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                            target.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
                        {
                            string csTarget = Path.ChangeExtension(target, ".cs");
                            string csFromRoot = Path.GetFullPath(Path.Combine(repoRoot, csTarget.TrimStart('/')));
                            if (File.Exists(csFromRoot)) continue;

                            if (isHistoricalDoc)
                            {
                                // Historical ADRs/migration docs reference pre-migration Python/Vue files
                                continue;
                            }
                        }

                        if (isHistoricalDoc && target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        {
                            // Allow historical ADR cross-references
                            continue;
                        }

                        issues.Add(new MarkdownValidationIssue(
                            relativeFile,
                            rawTarget,
                            $"Broken Markdown link in '{relativeFile}:{i + 1}': '{rawTarget}'",
                            i + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new MarkdownValidationIssue(relativeFile, "", $"Error reading '{file}': {ex.Message}"));
            }
        }

        return issues;
    }

    private static string StripOptionalLinkTitle(string rawTarget)
    {
        if (rawTarget.StartsWith('<') && rawTarget.EndsWith('>') && rawTarget.Length >= 2)
        {
            return rawTarget[1..^1].Trim();
        }

        int quoteIdx = rawTarget.IndexOf(" \"", StringComparison.Ordinal);
        if (quoteIdx < 0)
        {
            quoteIdx = rawTarget.IndexOf(" '", StringComparison.Ordinal);
        }

        return quoteIdx > 0 ? rawTarget[..quoteIdx].Trim() : rawTarget;
    }

    private static string UnescapeUriOrRaw(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static void ValidateResolvedTargetAnchor(
        string resolvedPath,
        string rawFragment,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        Dictionary<string, HashSet<string>> anchorCache,
        List<MarkdownValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(rawFragment))
        {
            return;
        }

        string? markdownTarget = null;
        if (File.Exists(resolvedPath) && resolvedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            markdownTarget = resolvedPath;
        }
        else if (Directory.Exists(resolvedPath))
        {
            string indexCandidate = Path.Combine(resolvedPath, "index.md");
            string readmeCandidate = Path.Combine(resolvedPath, "README.md");
            if (File.Exists(indexCandidate))
            {
                markdownTarget = indexCandidate;
            }
            else if (File.Exists(readmeCandidate))
            {
                markdownTarget = readmeCandidate;
            }
        }

        if (markdownTarget is not null)
        {
            ValidateAnchorFragment(
                markdownTarget,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues);
        }
    }

    private static void ValidateAnchorFragment(
        string targetMarkdownPath,
        string rawFragment,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        Dictionary<string, HashSet<string>> anchorCache,
        List<MarkdownValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(rawFragment))
        {
            return;
        }

        string fragment = UnescapeUriOrRaw(rawFragment);
        if (IgnoredSubstrings.Any(ign => fragment.Contains(ign, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var anchors = GetMarkdownAnchors(targetMarkdownPath, anchorCache);
        if (!anchors.Contains(fragment))
        {
            issues.Add(new MarkdownValidationIssue(
                relativeFile,
                rawTarget,
                $"Broken Markdown heading anchor in '{relativeFile}:{lineNumber}': '{rawTarget}'",
                lineNumber));
        }
    }

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

    private static bool UpdateCodeFenceState(string line, ref char fenceChar, ref int fenceLen)
    {
        Match fenceMatch = CodeFenceLineRegex().Match(line.TrimStart());
        if (!fenceMatch.Success)
        {
            return false;
        }

        string marker = fenceMatch.Groups[1].Value;
        string rest = fenceMatch.Groups[2].Value.Trim();
        if (fenceLen == 0)
        {
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
        string fullMarkdownPath,
        Dictionary<string, HashSet<string>> anchorCache)
    {
        if (anchorCache.TryGetValue(fullMarkdownPath, out var cached))
        {
            return cached;
        }

        string content = File.ReadAllText(fullMarkdownPath);
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

    /// <summary>
    /// Reports every path into a .NET test project folder (<c>tests/Cadence.UnitTests/...</c>) that names a file or
    /// folder that does not exist, whether or not it is written as a link. Test files move when the test layout
    /// changes, and a stale path in a decision record or guide sends a reader to nothing. The migration working
    /// papers under <c>docs/migration/</c> record the tree as it was and are skipped, as are the generated RTM and
    /// IQ/OQ/PQ report, which <c>gxp sync</c> rewrites.
    /// </summary>
    public static List<MarkdownValidationIssue> ValidateTestProjectPaths(
        string repoRoot,
        IEnumerable<string> filesToScan)
    {
        var issues = new List<MarkdownValidationIssue>();

        foreach (var file in filesToScan)
        {
            if (!File.Exists(file)) continue;

            string relativeFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relativeFile.StartsWith("docs/migration/", StringComparison.OrdinalIgnoreCase) ||
                relativeFile.EndsWith("Requirements_Traceability_Matrix.md", StringComparison.OrdinalIgnoreCase) ||
                relativeFile.EndsWith("IQ_OQ_PQ_Execution_Report.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] lines;
            try { lines = File.ReadAllLines(file); } catch (IOException) { continue; }

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in TestProjectPathRegex.Matches(lines[i]))
                {
                    int end = m.Index + m.Length;
                    if (end < lines[i].Length && lines[i][end] == '*') continue; // a glob, not a path

                    string target = m.Value.TrimEnd('.');
                    string fullPath = Path.Combine(repoRoot, target);
                    if (File.Exists(fullPath) || Directory.Exists(fullPath)) continue;

                    issues.Add(new MarkdownValidationIssue(relativeFile, target,
                        $"Path to a missing test file or folder in '{relativeFile}:{i + 1}': '{target}'", i + 1));
                }
            }
        }

        return issues;
    }
}

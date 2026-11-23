using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Markdown;

public record MarkdownValidationIssue(
    string FilePath,
    string TargetLink,
    string Message,
    int LineNumber = 0
);

public static partial class MarkdownParser
{
    // Matches inline code spans first so `[text](link)` inside backticks is skipped while [`code`](link) and
    // links whose `[label]` wraps across lines within the same block are checked.
    [GeneratedRegex(@"(?:``[^`\n]+``|`[^`\n]+`)|\[((?:`[^`\n]+`|!\[[^\n\]]*\]\([^)\n]*\)|[^\[\]\n]|\\[\[\]]|\n(?![ \t]*(?:\n|#{1,6}[ \t]|[-*+][ \t]|\d+\.[ \t]|\|)))+)\]\(([^)\n]+)\)")]
    private static partial Regex InlineCodeOrLinkRegex();

    [GeneratedRegex(@"^[ \t]{0,3}\[([^\n\]]+)\]:[ \t]*<?([^>\s]+)>?", RegexOptions.Multiline)]
    private static partial Regex LinkReferenceDefRegex();

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
        IEnumerable<string> filesToScan,
        GitIndexReader? indexReader = null)
    {
        var issues = new List<MarkdownValidationIssue>();
        var anchorCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in filesToScan)
        {
            if (file.Contains("node_modules") || file.Contains(".venv") || file.Contains(".cadence") || file.Contains(".git")) continue;
            if (!SnapshotFileExists(repoRoot, file, indexReader)) continue;

            string fullSourceFile = Path.GetFullPath(file);
            string relativeFile = Path.GetRelativePath(repoRoot, fullSourceFile).Replace('\\', '/');
            bool isHistoricalDoc = relativeFile.StartsWith("docs/adr/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/migration/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/specs/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/MIGRATION_WORKFLOW.md", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/AGENTS.md", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (!TryReadSnapshotLines(repoRoot, fullSourceFile, indexReader, out string[] lines))
                {
                    continue;
                }
                char fenceChar = '\0';
                int fenceLen = 0;
                var maskedLines = new string[lines.Length];
                var lineStarts = new int[lines.Length];
                int currentOffset = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    lineStarts[i] = currentOffset;
                    string cleanLine = lines[i].TrimEnd('\r');
                    maskedLines[i] = UpdateCodeFenceState(cleanLine, ref fenceChar, ref fenceLen) || fenceLen > 0
                        ? string.Empty
                        : cleanLine;
                    currentOffset += maskedLines[i].Length + 1;
                }

                string maskedContent = string.Join('\n', maskedLines);

                var matches = InlineCodeOrLinkRegex().Matches(maskedContent);
                for (int mIdx = 0; mIdx < matches.Count; mIdx++)
                {
                    Match m = matches[mIdx];
                    if (!m.Groups[2].Success)
                    {
                        continue;
                    }

                    int lineNumber = GetOneBasedLineNumber(lineStarts, m.Index);
                    ValidateSingleLinkTarget(
                        repoRoot,
                        fullSourceFile,
                        relativeFile,
                        isHistoricalDoc,
                        m.Groups[2].Value,
                        lineNumber,
                        anchorCache,
                        issues,
                        indexReader);
                }

                var refMatches = LinkReferenceDefRegex().Matches(maskedContent);
                for (int rIdx = 0; rIdx < refMatches.Count; rIdx++)
                {
                    Match rm = refMatches[rIdx];
                    if (!rm.Groups[2].Success)
                    {
                        continue;
                    }

                    int lineNumber = GetOneBasedLineNumber(lineStarts, rm.Index);
                    ValidateSingleLinkTarget(
                        repoRoot,
                        fullSourceFile,
                        relativeFile,
                        isHistoricalDoc,
                        rm.Groups[2].Value,
                        lineNumber,
                        anchorCache,
                        issues,
                        indexReader);
                }
            }
            catch (Exception ex)
            {
                issues.Add(new MarkdownValidationIssue(relativeFile, "", $"Error reading '{file}': {ex.Message}"));
            }
        }

        return issues;
    }

    private static int GetOneBasedLineNumber(int[] lineStarts, int index)
    {
        int found = Array.BinarySearch(lineStarts, index);
        return found >= 0 ? found + 1 : ~found;
    }

    private static void ValidateSingleLinkTarget(
        string repoRoot,
        string fullSourceFile,
        string relativeFile,
        bool isHistoricalDoc,
        string capturedTarget,
        int lineNumber,
        Dictionary<string, HashSet<string>> anchorCache,
        List<MarkdownValidationIssue> issues,
        GitIndexReader? indexReader)
    {
        string rawTarget = StripOptionalLinkTitle(capturedTarget.Trim());
        if (string.IsNullOrEmpty(rawTarget) ||
            rawTarget.Contains("://", StringComparison.Ordinal) ||
            rawTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int hashIdx = rawTarget.IndexOf('#');
        string beforeHash = hashIdx >= 0 ? rawTarget[..hashIdx] : rawTarget;
        string rawFragment = hashIdx >= 0 ? rawTarget[(hashIdx + 1)..].Trim() : string.Empty;

        // Same-page heading anchor link (#section)
        if (rawTarget.StartsWith('#'))
        {
            ValidateAnchorFragment(
                repoRoot,
                fullSourceFile,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues,
                indexReader);
            return;
        }

        // Strip queries and pytest selectors from the file portion
        string target = beforeHash
            .Split('?')[0]
            .Split(["::"], StringSplitOptions.None)[0]
            .Trim();
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        // Ignore placeholders
        if (IgnoredSubstrings.Any(ign => target.Contains(ign, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string fileDir = Path.GetDirectoryName(fullSourceFile) ?? repoRoot;
        string decodedTarget = UnescapeUriOrRaw(target);

        // Try relative to file directory
        string pathFromDir = Path.GetFullPath(Path.Combine(fileDir, decodedTarget));
        if (SnapshotFileExists(repoRoot, pathFromDir, indexReader) ||
            SnapshotDirectoryExists(repoRoot, pathFromDir, indexReader))
        {
            ValidateResolvedTargetAnchor(
                repoRoot,
                pathFromDir,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues,
                indexReader);
            return;
        }

        // Try relative to repo root
        string pathFromRoot = Path.GetFullPath(Path.Combine(repoRoot, decodedTarget.TrimStart('/')));
        if (SnapshotFileExists(repoRoot, pathFromRoot, indexReader) ||
            SnapshotDirectoryExists(repoRoot, pathFromRoot, indexReader))
        {
            ValidateResolvedTargetAnchor(
                repoRoot,
                pathFromRoot,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues,
                indexReader);
            return;
        }

        // Fallback 1: Check .cs equivalent or src/ directory for transcribed C# code
        if (target.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
            target.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            target.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            target.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
        {
            string csTarget = Path.ChangeExtension(target, ".cs");
            string csFromRoot = Path.GetFullPath(Path.Combine(repoRoot, csTarget.TrimStart('/')));
            if (SnapshotFileExists(repoRoot, csFromRoot, indexReader)) return;

            if (isHistoricalDoc)
            {
                // Historical ADRs/migration docs reference pre-migration Python/Vue files
                return;
            }
        }

        if (isHistoricalDoc && target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            // Allow historical ADR cross-references
            return;
        }

        issues.Add(new MarkdownValidationIssue(
            relativeFile,
            rawTarget,
            $"Broken Markdown link in '{relativeFile}:{lineNumber}': '{rawTarget}'",
            lineNumber));
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
        string repoRoot,
        string resolvedPath,
        string rawFragment,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        Dictionary<string, HashSet<string>> anchorCache,
        List<MarkdownValidationIssue> issues,
        GitIndexReader? indexReader)
    {
        if (string.IsNullOrEmpty(rawFragment))
        {
            return;
        }

        string? markdownTarget = null;
        if (SnapshotFileExists(repoRoot, resolvedPath, indexReader) &&
            resolvedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            markdownTarget = resolvedPath;
        }
        else if (SnapshotDirectoryExists(repoRoot, resolvedPath, indexReader))
        {
            string indexCandidate = Path.Combine(resolvedPath, "index.md");
            string readmeCandidate = Path.Combine(resolvedPath, "README.md");
            if (SnapshotFileExists(repoRoot, indexCandidate, indexReader))
            {
                markdownTarget = indexCandidate;
            }
            else if (SnapshotFileExists(repoRoot, readmeCandidate, indexReader))
            {
                markdownTarget = readmeCandidate;
            }
        }

        if (markdownTarget is not null)
        {
            ValidateAnchorFragment(
                repoRoot,
                markdownTarget,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues,
                indexReader);
        }
    }

    private static void ValidateAnchorFragment(
        string repoRoot,
        string targetMarkdownPath,
        string rawFragment,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        Dictionary<string, HashSet<string>> anchorCache,
        List<MarkdownValidationIssue> issues,
        GitIndexReader? indexReader)
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

        var anchors = GetMarkdownAnchors(repoRoot, targetMarkdownPath, anchorCache, indexReader);
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

    internal static bool UpdateCodeFenceState(string line, ref char fenceChar, ref int fenceLen)
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

    /// <summary>
    /// Reports every path into a .NET test project folder (<c>tests/Cadence.UnitTests/...</c>) that names a file or
    /// folder that does not exist, whether or not it is written as a link. Test files move when the test layout
    /// changes, and a stale path in a decision record or guide sends a reader to nothing. The migration working
    /// papers under <c>docs/migration/</c> record the tree as it was and are skipped, as are the generated RTM and
    /// IQ/OQ/PQ report, which <c>gxp sync</c> rewrites.
    /// </summary>
    public static List<MarkdownValidationIssue> ValidateTestProjectPaths(
        string repoRoot,
        IEnumerable<string> filesToScan,
        GitIndexReader? indexReader = null)
    {
        var issues = new List<MarkdownValidationIssue>();

        foreach (var file in filesToScan)
        {
            if (file.Contains("node_modules") || file.Contains(".venv") || file.Contains(".cadence") || file.Contains(".git")) continue;
            if (!SnapshotFileExists(repoRoot, file, indexReader)) continue;

            string relativeFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relativeFile.StartsWith("docs/migration/", StringComparison.OrdinalIgnoreCase) ||
                relativeFile.EndsWith("Requirements_Traceability_Matrix.md", StringComparison.OrdinalIgnoreCase) ||
                relativeFile.EndsWith("IQ_OQ_PQ_Execution_Report.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadSnapshotLines(repoRoot, file, indexReader, out string[] lines))
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in TestProjectPathRegex.Matches(lines[i]))
                {
                    int end = m.Index + m.Length;
                    if (end < lines[i].Length && lines[i][end] == '*') continue; // a glob, not a path

                    string target = m.Value.TrimEnd('.');
                    string fullPath = Path.Combine(repoRoot, target);
                    if (SnapshotFileExists(repoRoot, fullPath, indexReader) ||
                        SnapshotDirectoryExists(repoRoot, fullPath, indexReader))
                    {
                        continue;
                    }

                    issues.Add(new MarkdownValidationIssue(relativeFile, target,
                        $"Path to a missing test file or folder in '{relativeFile}:{i + 1}': '{target}'", i + 1));
                }
            }
        }

        return issues;
    }

    private static bool SnapshotFileExists(string repoRoot, string candidatePath, GitIndexReader? indexReader)
    {
        if (indexReader is not null)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, candidatePath);
            return !string.IsNullOrEmpty(rel) && indexReader.FileExists(rel);
        }

        return File.Exists(candidatePath);
    }

    private static bool SnapshotDirectoryExists(string repoRoot, string candidatePath, GitIndexReader? indexReader)
    {
        if (indexReader is not null)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, candidatePath);
            return rel is not null && indexReader.DirectoryExists(rel);
        }

        return Directory.Exists(candidatePath);
    }

    private static bool TryReadSnapshotLines(
        string repoRoot,
        string candidatePath,
        GitIndexReader? indexReader,
        out string[] lines)
    {
        if (indexReader is not null)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, candidatePath);
            if (!string.IsNullOrEmpty(rel))
            {
                return indexReader.TryReadLines(rel, out lines);
            }

            lines = [];
            return false;
        }

        try
        {
            lines = File.ReadAllLines(candidatePath);
            return true;
        }
        catch (IOException)
        {
            lines = [];
            return false;
        }
    }
}

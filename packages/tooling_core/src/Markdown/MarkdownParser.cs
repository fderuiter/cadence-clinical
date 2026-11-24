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

    private static readonly Dictionary<string, string> StagedRootDocs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["README"] = "README.md",
        ["README.md"] = "README.md",
        ["ARCHITECTURE"] = "ARCHITECTURE.md",
        ["ARCHITECTURE.md"] = "ARCHITECTURE.md",
        ["AGENTS"] = "AGENTS.md",
        ["AGENTS.md"] = "AGENTS.md",
        ["CONTEXT"] = "CONTEXT.md",
        ["CONTEXT.md"] = "CONTEXT.md",
        ["CONTRIBUTING"] = "CONTRIBUTING.md",
        ["CONTRIBUTING.md"] = "CONTRIBUTING.md",
        ["SECURITY"] = "SECURITY.md",
        ["SECURITY.md"] = "SECURITY.md",
        ["SUPPORT"] = "SUPPORT.md",
        ["SUPPORT.md"] = "SUPPORT.md",
        ["LICENSE"] = "LICENSE",
        ["LICENSE.md"] = "LICENSE",
    };

    public static List<MarkdownValidationIssue> ValidateWorkspaceLinks(
        string repoRoot,
        IEnumerable<string> filesToScan,
        GitIndexReader? indexReader = null)
    {
        var issues = new List<MarkdownValidationIssue>();
        var anchorCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var draftCache = new Dictionary<string, bool>(StringComparer.Ordinal);

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

                var parsedFm = YamlFrontmatterParser.Parse(lines);
                int startLineIdx = parsedFm.HasFrontmatter ? parsedFm.BodyStartLineIndex : 0;

                if (parsedFm.HasFrontmatter && parsedFm.RelatedLinks.Count > 0)
                {
                    foreach (var relatedEntry in parsedFm.RelatedLinks)
                    {
                        ValidateSingleLinkTarget(
                            repoRoot,
                            fullSourceFile,
                            relativeFile,
                            relatedEntry.Link,
                            relatedEntry.LineNumber,
                            isHistoricalDoc: false,
                            isFrontmatterRelated: true,
                            anchorCache,
                            draftCache,
                            issues,
                            indexReader);
                    }
                }

                char fenceChar = '\0';
                int fenceLen = 0;

                for (int i = startLineIdx; i < lines.Length; i++)
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
                        ValidateSingleLinkTarget(
                            repoRoot,
                            fullSourceFile,
                            relativeFile,
                            rawTarget,
                            i + 1,
                            isHistoricalDoc,
                            isFrontmatterRelated: false,
                            anchorCache,
                            draftCache,
                            issues,
                            indexReader);
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

    private static void ValidateSingleLinkTarget(
        string repoRoot,
        string fullSourceFile,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        bool isHistoricalDoc,
        bool isFrontmatterRelated,
        Dictionary<string, HashSet<string>> anchorCache,
        Dictionary<string, bool> draftCache,
        List<MarkdownValidationIssue> issues,
        GitIndexReader? indexReader)
    {
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

        if (IsCohostedOrStaticPortalEndpoint(target))
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

        if (!target.StartsWith('/'))
        {
            // Try relative to file directory
            string pathFromDir = Path.GetFullPath(Path.Combine(fileDir, decodedTarget));
            if (TryResolveTargetCandidate(repoRoot, pathFromDir, indexReader, out string resolvedFromDir))
            {
                if (CheckDraftBlogTarget(repoRoot, fullSourceFile, resolvedFromDir, relativeFile, rawTarget, lineNumber, draftCache, issues, indexReader))
                {
                    return;
                }

                ValidateResolvedTargetAnchor(
                    repoRoot,
                    resolvedFromDir,
                    rawFragment,
                    relativeFile,
                    rawTarget,
                    lineNumber,
                    anchorCache,
                    issues,
                    indexReader);
                return;
            }
        }

        // Try relative to repo root (or docs/ portal root for leading '/' links)
        string trimmedRootTarget = decodedTarget.TrimStart('/');
        string pathFromRoot = Path.GetFullPath(Path.Combine(repoRoot, trimmedRootTarget));
        if (TryResolveTargetCandidate(repoRoot, pathFromRoot, indexReader, out string resolvedFromRoot))
        {
            if (CheckDraftBlogTarget(repoRoot, fullSourceFile, resolvedFromRoot, relativeFile, rawTarget, lineNumber, draftCache, issues, indexReader))
            {
                return;
            }

            ValidateResolvedTargetAnchor(
                repoRoot,
                resolvedFromRoot,
                rawFragment,
                relativeFile,
                rawTarget,
                lineNumber,
                anchorCache,
                issues,
                indexReader);
            return;
        }

        if (target.StartsWith('/') &&
            TryResolvePortalRootLink(repoRoot, trimmedRootTarget, indexReader, out string portalTarget))
        {
            if (CheckDraftBlogTarget(repoRoot, fullSourceFile, portalTarget, relativeFile, rawTarget, lineNumber, draftCache, issues, indexReader))
            {
                return;
            }

            ValidateResolvedTargetAnchor(
                repoRoot,
                portalTarget,
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
        if (!isFrontmatterRelated &&
            (target.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
             target.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
             target.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
             target.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)))
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

        if (!isFrontmatterRelated && isHistoricalDoc && target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            // Allow historical ADR cross-references
            return;
        }

        string label = isFrontmatterRelated ? "frontmatter.related link" : "Markdown link";
        issues.Add(new MarkdownValidationIssue(
            relativeFile,
            rawTarget,
            $"Broken {label} in '{relativeFile}:{lineNumber}': '{rawTarget}'",
            lineNumber));
    }

    private static bool IsCohostedOrStaticPortalEndpoint(string target)
    {
        if (!target.StartsWith('/'))
        {
            return false;
        }

        return target.Equals("/demo", StringComparison.OrdinalIgnoreCase) ||
               target.StartsWith("/demo/", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("/subject-portal", StringComparison.OrdinalIgnoreCase) ||
               target.StartsWith("/subject-portal/", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("/feed.xml", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("/cadence-clinical/feed.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveTargetCandidate(
        string repoRoot,
        string candidatePath,
        GitIndexReader? indexReader,
        out string resolvedPath)
    {
        if (SnapshotFileExists(repoRoot, candidatePath, indexReader) ||
            SnapshotDirectoryExists(repoRoot, candidatePath, indexReader))
        {
            resolvedPath = candidatePath;
            return true;
        }

        if (candidatePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            string mdEquivalent = Path.ChangeExtension(candidatePath, ".md");
            if (SnapshotFileExists(repoRoot, mdEquivalent, indexReader))
            {
                resolvedPath = mdEquivalent;
                return true;
            }
        }
        else if (!Path.HasExtension(candidatePath))
        {
            string mdWithExt = candidatePath + ".md";
            if (SnapshotFileExists(repoRoot, mdWithExt, indexReader))
            {
                resolvedPath = mdWithExt;
                return true;
            }
        }

        resolvedPath = string.Empty;
        return false;
    }

    private static bool TryResolvePortalRootLink(
        string repoRoot,
        string trimmedRootTarget,
        GitIndexReader? indexReader,
        out string resolvedPath)
    {
        string docsDir = Path.Combine(repoRoot, "docs");
        string cleanKey = trimmedRootTarget.TrimEnd('/');
        if (cleanKey.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            cleanKey = cleanKey[..^5];
        }

        if (string.IsNullOrEmpty(cleanKey))
        {
            string docsHome = Path.Combine(docsDir, "index.md");
            if (SnapshotFileExists(repoRoot, docsHome, indexReader))
            {
                resolvedPath = docsHome;
                return true;
            }
        }

        if (StagedRootDocs.TryGetValue(cleanKey, out string? stagedRootName))
        {
            string rootDocPath = Path.Combine(repoRoot, stagedRootName);
            if (SnapshotFileExists(repoRoot, rootDocPath, indexReader))
            {
                resolvedPath = rootDocPath;
                return true;
            }
        }

        string docsCandidate = Path.GetFullPath(Path.Combine(docsDir, cleanKey));
        if (TryResolveTargetCandidate(repoRoot, docsCandidate, indexReader, out resolvedPath))
        {
            return true;
        }

        string docsIndexCandidate = Path.Combine(docsDir, cleanKey, "index.md");
        if (SnapshotFileExists(repoRoot, docsIndexCandidate, indexReader))
        {
            resolvedPath = docsIndexCandidate;
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }

    private static bool CheckDraftBlogTarget(
        string repoRoot,
        string fullSourceFile,
        string resolvedTarget,
        string relativeFile,
        string rawTarget,
        int lineNumber,
        Dictionary<string, bool> draftCache,
        List<MarkdownValidationIssue> issues,
        GitIndexReader? indexReader)
    {
        if (string.Equals(Path.GetFullPath(fullSourceFile), Path.GetFullPath(resolvedTarget), StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsDraftBlogPostSnapshot(repoRoot, resolvedTarget, draftCache, indexReader))
        {
            return false;
        }

        issues.Add(new MarkdownValidationIssue(
            relativeFile,
            rawTarget,
            $"Link in '{relativeFile}:{lineNumber}' references draft blog post: '{rawTarget}'",
            lineNumber));
        return true;
    }

    private static bool IsDraftBlogPostSnapshot(
        string repoRoot,
        string resolvedTarget,
        Dictionary<string, bool> draftCache,
        GitIndexReader? indexReader)
    {
        string normalizedFull = Path.GetFullPath(resolvedTarget);
        if (draftCache.TryGetValue(normalizedFull, out bool cached))
        {
            return cached;
        }

        string rel = Path.GetRelativePath(repoRoot, normalizedFull).Replace('\\', '/');
        if (!rel.StartsWith("docs/blog/", StringComparison.OrdinalIgnoreCase) ||
            rel.Equals("docs/blog/index.md", StringComparison.OrdinalIgnoreCase) ||
            !rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            draftCache[normalizedFull] = false;
            return false;
        }

        bool isDraft = false;
        if (TryReadSnapshotLines(repoRoot, normalizedFull, indexReader, out string[] targetLines))
        {
            var parsed = YamlFrontmatterParser.Parse(targetLines);
            isDraft = parsed.HasFrontmatter && parsed.IsDraft;
        }

        draftCache[normalizedFull] = isDraft;
        return isDraft;
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

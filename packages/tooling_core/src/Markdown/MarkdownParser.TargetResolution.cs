using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Markdown;

public static partial class MarkdownParser
{
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
}

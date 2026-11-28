using System;
using System.Collections.Generic;
using System.IO;
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

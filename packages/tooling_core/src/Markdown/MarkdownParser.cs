using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Markdown;

public record MarkdownValidationIssue(
    string FilePath,
    string TargetLink,
    string Message,
    int LineNumber = 0
);

public static class MarkdownParser
{
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    // A path into a .NET test project folder (tests/Cadence.UnitTests/..., tests/Cadence.IntegrationTests/...):
    // the first segment starts with a capital letter. Lower-case paths such as tests/fixtures or a web app's own
    // tests/ folder are not matched.
    private static readonly Regex TestProjectPathRegex = new(
        @"(?<![A-Za-z0-9_./-])tests/[A-Z][A-Za-z0-9_.-]*(?:/[A-Za-z0-9_.-]+)*/?", RegexOptions.Compiled);

    private static readonly string[] IgnoredSubstrings = new[]
    {
        "placeholder", "your-", "example", "path-to-new-adr", "node.js", "..."
    };

    public static List<MarkdownValidationIssue> ValidateWorkspaceLinks(
        string repoRoot,
        IEnumerable<string> filesToScan)
    {
        var issues = new List<MarkdownValidationIssue>();

        foreach (var file in filesToScan)
        {
            if (file.Contains("node_modules") || file.Contains(".venv") || file.Contains(".git")) continue;
            if (!File.Exists(file)) continue;

            string relativeFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            bool isHistoricalDoc = relativeFile.StartsWith("docs/adr/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/migration/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.StartsWith("docs/specs/", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/MIGRATION_WORKFLOW.md", StringComparison.OrdinalIgnoreCase) ||
                                  relativeFile.Equals("docs/AGENTS.md", StringComparison.OrdinalIgnoreCase);

            try
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var matches = MarkdownLinkRegex.Matches(lines[i]);
                    foreach (Match m in matches)
                    {
                        string rawTarget = m.Groups[2].Value.Trim();
                        if (rawTarget.Contains("://") ||
                            rawTarget.StartsWith('#') ||
                            rawTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                            rawTarget.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Strip anchors, queries, pytest selectors
                        string target = rawTarget.Split('#')[0].Split('?')[0].Split(new[] { "::" }, StringSplitOptions.None)[0].Trim();
                        if (string.IsNullOrEmpty(target)) continue;

                        // Ignore placeholders
                        if (IgnoredSubstrings.Any(ign => target.Contains(ign, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string fileDir = Path.GetDirectoryName(file) ?? repoRoot;

                        // Try relative to file directory
                        string pathFromDir = Path.GetFullPath(Path.Combine(fileDir, target));
                        if (File.Exists(pathFromDir) || Directory.Exists(pathFromDir))
                        {
                            continue;
                        }

                        // Try relative to repo root
                        string pathFromRoot = Path.GetFullPath(Path.Combine(repoRoot, target.TrimStart('/')));
                        if (File.Exists(pathFromRoot) || Directory.Exists(pathFromRoot))
                        {
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

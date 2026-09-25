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

    private static readonly string[] IgnoredSubstrings = new[]
    {
        "placeholder", "your-", "example", "path-to-new-adr", "node.js"
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
                string content = File.ReadAllText(file);
                var matches = MarkdownLinkRegex.Matches(content);
                foreach (Match m in matches)
                {
                    string rawTarget = m.Groups[2].Value.Trim();
                    if (rawTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        rawTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
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

                    issues.Add(new MarkdownValidationIssue(relativeFile, rawTarget, $"Broken Markdown link in '{relativeFile}': '{rawTarget}'"));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new MarkdownValidationIssue(relativeFile, "", $"Error reading '{file}': {ex.Message}"));
            }
        }

        return issues;
    }
}

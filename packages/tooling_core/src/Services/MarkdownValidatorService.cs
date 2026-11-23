using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Markdown;

namespace Cadence.ToolingCore.Services;

public static partial class MarkdownValidatorService
{
    public const int MaxProseLineLength = 120;
    public const int MaxTableRowLength = 200;

    private static readonly string[] ExemptPathPrefixes =
    [
        "docs/adr/",
        "docs/SDLC/",
        "docs/migration/",
        "docs/specs/",
        ".agents/",
        "docs/CDISC/",
        "docs/NCI/",
        "docs/openapi/",
        "docs/api/",
        "docs/schema/"
    ];

    private static readonly string[] ExemptExactPaths =
    [
        "docs/SRS.md",
        "docs/FUNCTIONAL_PRODUCT_SPECIFICATION.md",
        "docs/DOCUMENTATION_INDEX.md"
    ];

    public static bool IsWidthExemptDoc(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return ExemptExactPaths.Any(p => normalized.Equals(p, StringComparison.OrdinalIgnoreCase)) ||
               ExemptPathPrefixes.Any(p => normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public static int ValidateMarkdown(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating Markdown documentation formatting and workspace links...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ Markdown files and workspace links validated cleanly.");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            string[] excludedDirs = [
                $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.agents{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.cadence{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.system_generated{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}"
            ];

            string[] mdFiles;
            if (files is not null)
            {
                mdFiles = files
                    .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                    .Where(rel => !string.IsNullOrEmpty(rel) &&
                                  (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                    .Select(rel => Path.Combine(repoRoot, rel!))
                    .Where(f => !excludedDirs.Any(d => f.Contains(d)))
                    .ToArray();
            }
            else
            {
                mdFiles = Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
                    .Where(f => !excludedDirs.Any(d => f.Contains(d)))
                    .ToArray();
            }

            if (mdFiles.Length == 0)
            {
                Console.WriteLine("✔ Markdown files and workspace links validated cleanly.");
                return 0;
            }

            int issuesCount = 0;

            // Rule 1: Workspace Link Validation via MarkdownParser
            var linkIssues = MarkdownParser.ValidateWorkspaceLinks(repoRoot, mdFiles, reader);
            foreach (var issue in linkIssues)
            {
                Console.WriteLine($"[MARKDOWN LINK] {issue.Message}");
                issuesCount++;
            }

            // Rule 2: Paths into .NET test project folders must exist
            foreach (var issue in MarkdownParser.ValidateTestProjectPaths(repoRoot, mdFiles, reader))
            {
                Console.WriteLine($"[MARKDOWN TEST PATH] {issue.Message}");
                issuesCount++;
            }

            // Rule 3: Heading space validation and active-doc prose/table width limits
            foreach (var file in mdFiles)
            {
                string[] lines;
                if (reader is not null)
                {
                    if (!reader.TryReadLines(file, out lines))
                    {
                        continue;
                    }
                }
                else
                {
                    try { lines = File.ReadAllLines(file); } catch { continue; }
                }

                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                bool enforceWidths = !IsWidthExemptDoc(relPath);
                char fenceChar = '\0';
                int fenceLen = 0;
                string? activeGeneratedRegionId = null;

                int startLine = 0;
                if (lines.Length > 1 && lines[0].TrimEnd('\r') == "---")
                {
                    for (int fm = 1; fm < lines.Length; fm++)
                    {
                        string fmLine = lines[fm].TrimEnd('\r');
                        if (fmLine == "---" || fmLine == "...")
                        {
                            startLine = fm + 1;
                            break;
                        }
                    }
                }

                for (int i = startLine; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (MarkdownParser.UpdateCodeFenceState(line, ref fenceChar, ref fenceLen) || fenceLen > 0)
                    {
                        continue;
                    }

                    string trimmed = line.TrimStart();
                    string trimmedBoth = trimmed.TrimEnd();

                    if (activeGeneratedRegionId is null)
                    {
                        var beginMatch = GeneratedRegionBeginRegex().Match(trimmedBoth);
                        if (beginMatch.Success)
                        {
                            activeGeneratedRegionId = beginMatch.Groups[1].Value;
                            continue;
                        }
                    }
                    else if (string.Equals(trimmedBoth, $"<!-- {activeGeneratedRegionId}:end -->", StringComparison.Ordinal))
                    {
                        activeGeneratedRegionId = null;
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
                                Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Heading missing space after '#': '{line.Trim()}'");
                                issuesCount++;
                            }
                        }
                    }

                    if (!enforceWidths || activeGeneratedRegionId is not null)
                    {
                        continue;
                    }

                    if (trimmed.StartsWith('|'))
                    {
                        if (line.Length > MaxTableRowLength)
                        {
                            Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Table row exceeds {MaxTableRowLength} characters ({line.Length})");
                            issuesCount++;
                        }
                    }
                    else if (line.Length > MaxProseLineLength && !IsStandaloneUnbreakableLink(line))
                    {
                        Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Prose line exceeds {MaxProseLineLength} characters ({line.Length})");
                        issuesCount++;
                    }
                }
            }

            if (issuesCount > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {issuesCount} markdown issues.");
                return 1;
            }

            Console.WriteLine("✔ Markdown files and workspace links validated cleanly.");
            return 0;
        }
    }

    private static bool IsStandaloneUnbreakableLink(string line) =>
        StandaloneInlineLinkRegex().IsMatch(line) ||
        LinkReferenceDefinitionRegex().IsMatch(line) ||
        StandaloneUrlRegex().IsMatch(line);

    [GeneratedRegex(@"^<!--\s*([a-z0-9-]+):begin\b[^\r\n]*\bdo not edit\b[^\r\n]*-->$")]
    private static partial Regex GeneratedRegionBeginRegex();

    [GeneratedRegex(@"^\s*(?:>\s*)*(?:(?:[-*+]|\d+\.)\s+)?[*_(]*\[(?:[^\[\]\s]+|[^\[\]]{1,80})\]\([^\s()]+\)[*_.,;:·)\s]*$")]
    private static partial Regex StandaloneInlineLinkRegex();

    [GeneratedRegex(@"^\s*\[[^\[\]]{1,80}\]:\s*\S+\s*$")]
    private static partial Regex LinkReferenceDefinitionRegex();

    [GeneratedRegex(@"^\s*(?:>\s*)*(?:(?:[-*+]|\d+\.)\s+)?<?https?://\S+>?\s*$")]
    private static partial Regex StandaloneUrlRegex();

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}

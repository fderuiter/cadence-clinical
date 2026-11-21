using System;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Markdown;

namespace Cadence.ToolingCore.Services;

public static class MarkdownValidatorService
{
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

            // Rule 3: Heading space validation
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

                bool inFencedBlock = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                    {
                        inFencedBlock = !inFencedBlock;
                        continue;
                    }

                    if (inFencedBlock)
                    {
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
                                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                                Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Heading missing space after '#': '{line.Trim()}'");
                                issuesCount++;
                            }
                        }
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

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}

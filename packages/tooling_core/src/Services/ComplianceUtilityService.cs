using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static class ComplianceUtilityService
{
    // PRD tags only: an SRS tag (@req:SRS-SYS-005) is a different, valid requirement family and must not become
    // "PRD-SRS-SYS-005".
    private static readonly Regex ReqRegex = new(@"@req:(?:prd-|PRD-)?(?!srs-|SRS-)([a-zA-Z0-9]+)-([a-zA-Z0-9]+)", RegexOptions.Compiled);

    public static int ExecuteComplianceUtility(bool sanitize = false, string[]? targetFiles = null, string? docsDir = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine($"Executing GxP compliance utility for root '{repoRoot}'...");

        if (sanitize)
        {
            int updatedCount = SanitizeRequirementTags(repoRoot, targetFiles, docsDir);
            Console.WriteLine($"✔ Document paths and requirement prefixes sanitized ({updatedCount} files updated).");
        }

        return 0;
    }

    /// <summary>
    /// The <c>sanitizer</c> gate: in normal runs, upper-cases <c>@req:PRD-…</c> tags in <c>docs/**/*.md</c> (or the given
    /// Markdown files) and fails when it had to change a file; during <c>--pre-commit</c> runs, inspects staged blobs
    /// via <see cref="GitIndexReader"/> and reports lower-case tags as errors without mutating working-tree files.
    /// </summary>
    public static int CheckRequirementTags(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            Console.WriteLine("✔ No staged Markdown files to check for requirement tag casing.");
            return 0;
        }

        if (preCommit)
        {
            int violationCount = CountStagedLowercaseRequirementTags(repoRoot, targetFiles, indexReader);
            if (violationCount > 0)
            {
                Console.WriteLine($"[ERROR] Found lower-case requirement tag prefix(es) (@req:prd-…) in {violationCount} staged Markdown file(s). Run 'pnpm cadence fix' (or upper-case to '@req:PRD-…') and stage the fix.");
                return 1;
            }

            Console.WriteLine("✔ Requirement tag prefixes are already upper case.");
            return 0;
        }

        int updatedCount = SanitizeRequirementTags(repoRoot, targetFiles, docsDir: null);
        if (updatedCount > 0)
        {
            Console.WriteLine($"[ERROR] Upper-cased the requirement tags in {updatedCount} Markdown file(s). Review and stage them, then commit again.");
            return 1;
        }

        Console.WriteLine("✔ Requirement tag prefixes are already upper case.");
        return 0;
    }

    private static int CountStagedLowercaseRequirementTags(string repoRoot, string[]? targetFiles, GitIndexReader? indexReader)
    {
        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit: true, indexReader, out var ownedReader);
        using (ownedReader)
        {
            IEnumerable<string> candidates;
            if (targetFiles is not null)
            {
                candidates = targetFiles;
            }
            else if (reader is { HasAuthoritativeIndex: true })
            {
                candidates = reader.Paths.Where(p => p.StartsWith("docs/", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                string docsDir = Path.Combine(repoRoot, "docs");
                candidates = Directory.Exists(docsDir)
                    ? Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                    : [];
            }

            int violations = 0;
            foreach (string rawFile in candidates)
            {
                string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, rawFile);
                if (string.IsNullOrEmpty(rel))
                {
                    continue;
                }

                if (rel.Contains("docs/SDLC/runs/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string content;
                if (reader is not null)
                {
                    if (!reader.TryReadText(rel, out content))
                    {
                        continue;
                    }
                }
                else
                {
                    string fullPath = Path.Combine(repoRoot, rel);
                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }

                    content = File.ReadAllText(fullPath);
                }

                string sanitized = ReqRegex.Replace(content, m => $"@req:PRD-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}");
                if (sanitized != content)
                {
                    Console.WriteLine($"[SANITIZER] {rel}: contains lower-case requirement tag(s).");
                    violations++;
                }
            }

            return violations;
        }
    }

    /// <summary>Rewrites lower-case <c>@req:prd-…</c> tags and returns how many files changed.</summary>
    public static int SanitizeRequirementTags(string repoRoot, string[]? targetFiles, string? docsDir)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        string targetDocsDir = docsDir ?? Path.Combine(repoRoot, "docs");
        if (targetFiles is null && !Directory.Exists(targetDocsDir))
        {
            return 0;
        }

        var files = targetFiles is not null
            ? targetFiles.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f))
            : Directory.GetFiles(targetDocsDir, "*.md", SearchOption.AllDirectories);

        int updatedCount = 0;
        foreach (var file in files)
        {
            if (file.Replace('\\', '/').Contains("docs/SDLC/runs/")) continue;
            // The sanitizer covers documentation; staged source files are passed in too and are left alone.
            if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string content = File.ReadAllText(file);
                string sanitized = ReqRegex.Replace(content, m => $"@req:PRD-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}");

                if (sanitized != content)
                {
                    File.WriteAllText(file, sanitized);
                    updatedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[WARNING] Error processing {file}: {ex.Message}");
            }
        }

        return updatedCount;
    }

    private static string FindRepoRoot() => RepositoryRoot.Find();
}

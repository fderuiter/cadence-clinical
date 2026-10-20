using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
    /// The <c>sanitizer</c> gate: upper-cases <c>@req:PRD-…</c> tags in <c>docs/**/*.md</c> (or the given Markdown
    /// files) and fails when it had to change a file, so the fix is reviewed and staged instead of slipping past the
    /// commit that needed it.
    /// </summary>
    public static int CheckRequirementTags(string? repoRoot = null, string[]? targetFiles = null)
    {
        repoRoot ??= FindRepoRoot();
        int updatedCount = SanitizeRequirementTags(repoRoot, targetFiles, docsDir: null);
        if (updatedCount > 0)
        {
            Console.WriteLine($"[ERROR] Upper-cased the requirement tags in {updatedCount} Markdown file(s). Review and stage them, then commit again.");
            return 1;
        }

        Console.WriteLine("✔ Requirement tag prefixes are already upper case.");
        return 0;
    }

    /// <summary>Rewrites lower-case <c>@req:prd-…</c> tags and returns how many files changed.</summary>
    public static int SanitizeRequirementTags(string repoRoot, string[]? targetFiles, string? docsDir)
    {
        string targetDocsDir = docsDir ?? Path.Combine(repoRoot, "docs");
        if (!Directory.Exists(targetDocsDir) && targetFiles is not { Length: > 0 })
        {
            return 0;
        }

        var files = targetFiles != null && targetFiles.Length > 0
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

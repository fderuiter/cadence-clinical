using System;
using System.IO;
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
            string targetDocsDir = docsDir ?? Path.Combine(repoRoot, "docs");
            if (Directory.Exists(targetDocsDir))
            {
                var files = targetFiles != null && targetFiles.Length > 0
                    ? targetFiles
                    : Directory.GetFiles(targetDocsDir, "*.md", SearchOption.AllDirectories);

                int updatedCount = 0;
                foreach (var file in files)
                {
                    if (file.Contains("docs/SDLC/runs/")) continue;
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
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Error processing {file}: {ex.Message}");
                    }
                }
                Console.WriteLine($"✔ Document paths and requirement prefixes sanitized ({updatedCount} files updated).");
            }
        }

        return 0;
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

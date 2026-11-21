using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Enforces trailing-whitespace and end-of-file newline hygiene across text files while skipping
/// binary assets, third-party clinical schemas, SHA-256-pinned seed manifests, and GxP snapshot reports.
/// </summary>
public static class FileHygieneValidatorService
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Pre-existing clinical specification Markdown files containing Markdown two-space line breaks,
    /// exempted only on whole-repository scans when no explicit target files are provided.
    /// </summary>
    private static readonly HashSet<string> LegacyMarkdownTrailingWhitespaceExemptions = new(StringComparer.Ordinal)
    {
        "docs/COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md",
        "docs/FUNCTIONAL_PRODUCT_SPECIFICATION.md",
        "docs/SDLC/01_Product_Requirements_Document_PRD.md",
        "docs/SDLC/02_Technical_Design_Document_TDD.md",
        "docs/SDLC/03_API_Integration_Specification.md",
        "docs/SDLC/SPEC_Knowledge_Support_Hub.md",
        "docs/SRS.md",
        "docs/migration/slices/execution-observations/01_spec.md",
        "docs/migration/slices/execution-observations/03_tickets.md",
        "docs/specs/ecrf_designer_ide_spec.md",
    };

    public static int ValidateTrailingWhitespace(string[]? targetFiles = null, string? repoRoot = null, bool preCommit = false)
    {
        repoRoot ??= FindRepoRoot();
        bool hasExplicitTargets = preCommit || targetFiles is not null;
        var files = ResolveCandidateFiles(repoRoot, targetFiles, preCommit);
        if (files.Count == 0)
        {
            return 0;
        }

        var errors = new List<string>();
        foreach (var (fullPath, relPath) in files)
        {
            if (!hasExplicitTargets && LegacyMarkdownTrailingWhitespaceExemptions.Contains(relPath))
            {
                continue;
            }

            string? text = TryReadUtf8Text(fullPath);
            if (text is null || text.Length == 0)
            {
                continue;
            }

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }

                if (line.Length > 0 && (line[^1] == ' ' || line[^1] == '\t'))
                {
                    errors.Add($"{relPath}:{i + 1}: Trailing whitespace.");
                }
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine("[TRAILING WHITESPACE] Violations detected:");
            foreach (string error in errors.Take(50))
            {
                Console.WriteLine($"  - {error}");
            }

            if (errors.Count > 50)
            {
                Console.WriteLine($"  ... and {errors.Count - 50} more.");
            }

            return 1;
        }

        return 0;
    }

    public static int ValidateEndOfFileNewline(string[]? targetFiles = null, string? repoRoot = null, bool preCommit = false)
    {
        repoRoot ??= FindRepoRoot();
        var files = ResolveCandidateFiles(repoRoot, targetFiles, preCommit);
        if (files.Count == 0)
        {
            return 0;
        }

        var errors = new List<string>();
        foreach (var (fullPath, relPath) in files)
        {
            string? text = TryReadUtf8Text(fullPath);
            if (text is null || text.Length == 0)
            {
                continue;
            }

            if (!text.EndsWith('\n'))
            {
                errors.Add($"{relPath}: File does not end with a newline.");
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine("[EOF NEWLINE] Files missing a final newline:");
            foreach (string error in errors.Take(50))
            {
                Console.WriteLine($"  - {error}");
            }

            if (errors.Count > 50)
            {
                Console.WriteLine($"  ... and {errors.Count - 50} more.");
            }

            return 1;
        }

        return 0;
    }

    private static List<(string FullPath, string RelativePath)> ResolveCandidateFiles(
        string repoRoot,
        string[]? targetFiles,
        bool preCommit)
    {
        if (preCommit)
        {
            var staged = targetFiles is { Length: > 0 }
                ? (IEnumerable<string>)targetFiles
                : PreCommitOpenApiService.GetStagedFiles(repoRoot);
            return FilterFiles(repoRoot, staged);
        }

        if (targetFiles is not null)
        {
            return FilterFiles(repoRoot, targetFiles);
        }

        var tracked = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot);
        if (tracked is not null)
        {
            return FilterFiles(repoRoot, tracked);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        return FilterFiles(repoRoot, Directory.EnumerateFiles(repoRoot, "*", options));
    }

    private static List<(string FullPath, string RelativePath)> FilterFiles(string repoRoot, IEnumerable<string> candidates)
    {
        var result = new List<(string FullPath, string RelativePath)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in candidates)
        {
            string fullPath = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            if (IsExcludedPath(rel) || !seen.Add(rel))
            {
                continue;
            }

            result.Add((fullPath, rel));
        }

        result.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        return result;
    }

    private static bool IsExcludedPath(string rel) =>
        rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
        rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) ||
        rel.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("dist/", StringComparison.OrdinalIgnoreCase) ||
        rel.Contains("/dist/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
        rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
        rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("TestResults/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("apps/safety/Schemas/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("docs/CDISC/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("docs/NCI/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("docs/SDLC/runs/", StringComparison.OrdinalIgnoreCase) ||
        rel.Equals("docs/SDLC/Requirements_Traceability_Matrix.md", StringComparison.OrdinalIgnoreCase) ||
        rel.Equals("docs/SDLC/IQ_OQ_PQ_Execution_Report.md", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("packages/kernel/src/Cadence.Standards/Seed/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("packages/kernel/src/Cadence.Standards/Blocks/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("tests/fixtures/", StringComparison.OrdinalIgnoreCase) ||
        rel.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadUtf8Text(string fullPath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (IOException)
        {
            return null;
        }

        int probeLen = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < probeLen; i++)
        {
            if (bytes[i] == 0)
            {
                return null;
            }
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Enforces trailing-whitespace and end-of-file newline hygiene across text files while skipping
/// binary assets, third-party clinical schemas, SHA-256-pinned seed manifests, and GxP snapshot reports.
/// </summary>
public static class FileHygieneValidatorService
{
    public static int ValidateTrailingWhitespace(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null) =>
        RunTextHygieneValidation(
            targetFiles,
            repoRoot,
            preCommit,
            indexReader,
            headerMessage: "[TRAILING WHITESPACE] Violations detected:",
            inspectFile: static (relPath, text, errors) =>
            {
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
            });

    public static int ValidateEndOfFileNewline(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null) =>
        RunTextHygieneValidation(
            targetFiles,
            repoRoot,
            preCommit,
            indexReader,
            headerMessage: "[EOF NEWLINE] Files missing a final newline:",
            inspectFile: static (relPath, text, errors) =>
            {
                if (!text.EndsWith('\n'))
                {
                    errors.Add($"{relPath}: File does not end with a newline.");
                }
            });

    private static int RunTextHygieneValidation(
        string[]? targetFiles,
        string? repoRoot,
        bool preCommit,
        GitIndexReader? indexReader,
        string headerMessage,
        Action<string, string, List<string>> inspectFile)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var files = ResolveCandidateFiles(repoRoot, targetFiles, preCommit, reader);
            if (files.Count == 0)
            {
                return 0;
            }

            var errors = new List<string>();
            foreach (var (fullPath, relPath) in files)
            {
                string? text = WorkspacePrimitives.TryReadUtf8Text(fullPath, relPath, reader, rejectBinaryNullBytes: true);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                inspectFile(relPath, text, errors);
            }

            if (errors.Count == 0)
            {
                return 0;
            }

            Console.WriteLine(headerMessage);
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
    }

    private static List<(string FullPath, string RelativePath)> ResolveCandidateFiles(
        string repoRoot,
        string[]? targetFiles,
        bool preCommit,
        GitIndexReader? reader)
    {
        if (preCommit)
        {
            var staged = targetFiles is not null
                ? (IEnumerable<string>)targetFiles
                : PreCommitOpenApiService.GetStagedFiles(repoRoot);
            return FilterFiles(repoRoot, staged, reader);
        }

        if (targetFiles is not null)
        {
            return FilterFiles(repoRoot, targetFiles, reader: null);
        }

        var tracked = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot);
        if (tracked is not null)
        {
            return FilterFiles(repoRoot, tracked, reader: null);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        return FilterFiles(repoRoot, Directory.EnumerateFiles(repoRoot, "*", options), reader: null);
    }

    private static List<(string FullPath, string RelativePath)> FilterFiles(
        string repoRoot,
        IEnumerable<string> candidates,
        GitIndexReader? reader)
    {
        var result = new List<(string FullPath, string RelativePath)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in candidates)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
            if (string.IsNullOrEmpty(rel))
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, rel);
            bool exists = reader is not null ? reader.FileExists(rel) : File.Exists(fullPath);
            if (!exists)
            {
                continue;
            }

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
}

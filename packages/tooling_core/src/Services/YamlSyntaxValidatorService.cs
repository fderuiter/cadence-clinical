using System;
using System.Collections.Generic;
using System.IO;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Native-AOT-safe YAML 1.2 structural and syntax validator.
/// Checks indentation, tabs, quoted scalars and escapes, flow collections, block scalars,
/// mapping separators, and duplicate mapping keys without reflection or external packages.
/// </summary>
public static partial class YamlSyntaxValidatorService
{
    public static int ValidateYaml(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            IReadOnlyList<string> yamlFiles;
            if (preCommit)
            {
                var staged = targetFiles is not null
                    ? (IEnumerable<string>)targetFiles
                    : PreCommitOpenApiService.GetStagedFiles(repoRoot);
                yamlFiles = ResolveTargetFiles(repoRoot, staged, reader);
                if (yamlFiles.Count == 0)
                {
                    return 0;
                }
            }
            else if (targetFiles is not null)
            {
                yamlFiles = ResolveTargetFiles(repoRoot, targetFiles, reader: null);
                if (yamlFiles.Count == 0)
                {
                    return 0;
                }
            }
            else
            {
                yamlFiles = DiscoverAllYamlFiles(repoRoot);
            }

            var errors = new List<string>();
            foreach (string file in yamlFiles)
            {
                string displayPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                IReadOnlyList<string> fileErrors;
                if (reader is not null)
                {
                    fileErrors = reader.TryReadBytes(displayPath, out byte[] stagedBytes)
                        ? ValidateBytes(stagedBytes, displayPath)
                        : [];
                }
                else
                {
                    fileErrors = ValidateFile(file, displayPath);
                }

                foreach (string error in fileErrors)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
            {
                Console.WriteLine("[YAML] Syntax validation failed:");
                foreach (string error in errors)
                {
                    Console.WriteLine($"  - {error}");
                }

                return 1;
            }

            return 0;
        }
    }

    public static IReadOnlyList<string> ValidateFile(string filePath, string? displayPath = null)
    {
        displayPath ??= filePath;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (IOException ex)
        {
            return [$"{displayPath}:1: Could not read file ({ex.Message})."];
        }

        return ValidateBytes(bytes, displayPath);
    }

    public static IReadOnlyList<string> ValidateBytes(byte[] bytes, string displayPath = "<yaml>")
    {
        if (!WorkspacePrimitives.TryDecodeStrictUtf8(bytes, out string content))
        {
            return [$"{displayPath}:1: File is not valid UTF-8."];
        }

        return ValidateContent(content, displayPath);
    }

    public static IReadOnlyList<string> ValidateContent(string content, string displayPath = "<yaml>")
    {
        var errors = new List<string>();
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');

        var stack = new Stack<BlockFrame>();
        var flowStack = new Stack<(char Open, int Line)>();
        char activeQuote = '\0';
        int quoteStartLine = 0;

        bool inBlockScalar = false;
        int blockScalarMinIndent = 0;
        int? blockScalarDetectedIndent = null;

        bool allowChildIndent = true;
        bool lastMappingKeyHadEmptyValue = false;
        int lastMappingKeyIndent = -1;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNo = idx + 1;
            string line = lines[idx];

            if (activeQuote != '\0')
            {
                if (IsDocumentMarkerAtColumnZero(line))
                {
                    errors.Add($"{displayPath}:{quoteStartLine}: Unclosed {(activeQuote == '"' ? "double" : "single")}-quoted string before document marker on line {lineNo}.");
                    return errors;
                }

                if (!ScanQuotedContinuation(line, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors))
                {
                    return errors;
                }

                continue;
            }

            if (inBlockScalar)
            {
                if (line.Trim().Length == 0)
                {
                    if (blockScalarDetectedIndent is int detected && line.Length > detected && line.Contains('\t', StringComparison.Ordinal))
                    {
                        errors.Add($"{displayPath}:{lineNo}: Tab character in blank line indentation inside block scalar.");
                        return errors;
                    }

                    continue;
                }

                int spaceIndent = YamlPrimitives.CountLeadingSpaces(line);
                if (spaceIndent >= blockScalarMinIndent && !IsDocumentMarkerAtColumnZero(line))
                {
                    blockScalarDetectedIndent ??= spaceIndent;
                    if (spaceIndent >= blockScalarDetectedIndent.Value)
                    {
                        continue;
                    }

                    if (spaceIndent > blockScalarMinIndent - 1)
                    {
                        errors.Add($"{displayPath}:{lineNo}: Inconsistent indentation inside block scalar (expected at least {blockScalarDetectedIndent.Value} spaces, found {spaceIndent}).");
                        return errors;
                    }
                }

                inBlockScalar = false;
                blockScalarDetectedIndent = null;
            }

            if (flowStack.Count > 0)
            {
                int leadSpaces = YamlPrimitives.CountLeadingSpaces(line);
                if (leadSpaces < line.Length && line[leadSpaces] == '\t' && line.TrimStart().Length > 0 && !line.TrimStart().StartsWith('#'))
                {
                    errors.Add($"{displayPath}:{lineNo}: Tabs are not allowed for YAML indentation.");
                    return errors;
                }

                if (!ScanExpression(line[leadSpaces..], lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _))
                {
                    return errors;
                }

                if (flowStack.Count == 0)
                {
                    allowChildIndent = false;
                    lastMappingKeyHadEmptyValue = false;
                }

                continue;
            }

            string trimmedStart = line.TrimStart(' ', '\t');
            if (trimmedStart.Length == 0 || trimmedStart.StartsWith('#'))
            {
                continue;
            }

            int indent = YamlPrimitives.CountLeadingSpaces(line);
            if (indent < line.Length && line[indent] == '\t')
            {
                errors.Add($"{displayPath}:{lineNo}: Tabs are not allowed for YAML indentation.");
                return errors;
            }

            if (indent == 0 && line.StartsWith('%'))
            {
                if (stack.Count > 0)
                {
                    errors.Add($"{displayPath}:{lineNo}: YAML directive '%' is only valid before '---' document start.");
                    return errors;
                }

                continue;
            }

            if (indent == 0 && IsDocumentStartMarker(line))
            {
                stack.Clear();
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = false;
                lastMappingKeyIndent = -1;
                string restAfterMarker = line[3..].TrimStart(' ', '\t');
                if (restAfterMarker.Length > 0 && !restAfterMarker.StartsWith('#'))
                {
                    if (StartsBlockScalar(restAfterMarker, displayPath, lineNo, errors, out int explicitIndent))
                    {
                        inBlockScalar = true;
                        blockScalarMinIndent = explicitIndent > 0 ? explicitIndent : 1;
                        blockScalarDetectedIndent = explicitIndent > 0 ? explicitIndent : null;
                    }
                    else if (!ScanExpression(restAfterMarker, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _))
                    {
                        return errors;
                    }
                }

                continue;
            }

            if (indent == 0 && IsDocumentEndMarker(line))
            {
                string restAfterEnd = line[3..].TrimStart(' ', '\t');
                if (restAfterEnd.Length > 0 && !restAfterEnd.StartsWith('#'))
                {
                    errors.Add($"{displayPath}:{lineNo}: Unexpected content after '...' document end marker.");
                    return errors;
                }

                stack.Clear();
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = false;
                lastMappingKeyIndent = -1;
                continue;
            }

            if (!ProcessBlockSegment(
                    line,
                    indent,
                    lineNo,
                    displayPath,
                    stack,
                    flowStack,
                    ref activeQuote,
                    ref quoteStartLine,
                    ref inBlockScalar,
                    ref blockScalarMinIndent,
                    ref blockScalarDetectedIndent,
                    ref allowChildIndent,
                    ref lastMappingKeyHadEmptyValue,
                    ref lastMappingKeyIndent,
                    errors))
            {
                return errors;
            }
        }

        if (activeQuote != '\0')
        {
            errors.Add($"{displayPath}:{quoteStartLine}: Unclosed {(activeQuote == '"' ? "double" : "single")}-quoted string.");
        }
        else if (flowStack.Count > 0)
        {
            var (open, openLine) = flowStack.Peek();
            errors.Add($"{displayPath}:{openLine}: Unclosed flow collection '{open}'.");
        }

        return errors;
    }

    private static bool IsDocumentStartMarker(string line) =>
        line.StartsWith("---", StringComparison.Ordinal) &&
        (line.Length == 3 || line[3] is ' ' or '\t');

    private static bool IsDocumentEndMarker(string line) =>
        line.StartsWith("...", StringComparison.Ordinal) &&
        (line.Length == 3 || line[3] is ' ' or '\t');

    private static bool IsDocumentMarkerAtColumnZero(string line) =>
        IsDocumentStartMarker(line) || IsDocumentEndMarker(line);

    private static List<string> ResolveTargetFiles(
        string repoRoot,
        IEnumerable<string> candidates,
        GitIndexReader? reader)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
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

            if (IsYamlFile(rel) && !IsExcludedDirectory(rel))
            {
                result.Add(fullPath);
            }
        }

        return [.. result];
    }

    private static List<string> DiscoverAllYamlFiles(string repoRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.*", options))
        {
            string rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (IsYamlFile(rel) && !IsExcludedDirectory(rel))
            {
                files.Add(path);
            }
        }

        return [.. files];
    }

    private static bool IsYamlFile(string relativePath) =>
        relativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedDirectory(string relativePath) =>
        relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
}

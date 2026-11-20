using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates that all third-party GitHub Action <c>uses:</c> references in <c>.github/workflows/</c>
/// are pinned to full 40-character hexadecimal commit SHAs with inline version comments.
/// </summary>
public static partial class ActionPinningValidatorService
{
    [GeneratedRegex(@"^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();

    [GeneratedRegex(@"^\s*(?:-\s*)?uses\s*:\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UsesLineRegex();

    public static int ValidateActionPinning(string[]? targetFiles = null, string? repoRoot = null, bool preCommit = false)
    {
        repoRoot ??= FindRepoRoot();

        IReadOnlyList<string> workflowFiles;
        if (preCommit)
        {
            var staged = targetFiles is { Length: > 0 }
                ? (IEnumerable<string>)targetFiles
                : PreCommitOpenApiService.GetStagedFiles(repoRoot);
            workflowFiles = ResolveTargetWorkflowFiles(repoRoot, staged);
            if (workflowFiles.Count == 0)
            {
                return 0;
            }
        }
        else if (targetFiles is not null)
        {
            workflowFiles = ResolveTargetWorkflowFiles(repoRoot, targetFiles);
            if (workflowFiles.Count == 0)
            {
                return 0;
            }
        }
        else
        {
            string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
            if (!Directory.Exists(workflowsDir))
            {
                Console.WriteLine($"Error: Workflows directory '{workflowsDir}' not found.");
                return 1;
            }

            workflowFiles = Directory.GetFiles(workflowsDir, "*.yml")
                .Concat(Directory.GetFiles(workflowsDir, "*.yaml"))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        var allErrors = new List<string>();
        foreach (string workflowFile in workflowFiles)
        {
            string displayPath = Path.GetRelativePath(repoRoot, workflowFile).Replace('\\', '/');
            allErrors.AddRange(CheckFile(workflowFile, displayPath));
        }

        if (allErrors.Count > 0)
        {
            Console.WriteLine(new string('=', 80));
            Console.WriteLine("ACTION PINNING LINTER ERRORS FOUND:");
            Console.WriteLine(new string('=', 80));
            foreach (string error in allErrors)
            {
                Console.WriteLine($"- {error}");
            }

            Console.WriteLine("\n" + new string('-', 80));
            Console.WriteLine("HOW TO FIX ACTION PINNING VULNERABILITIES:");
            Console.WriteLine("1. All third-party GitHub Actions must reference a 40-character commit SHA.");
            Console.WriteLine("2. Include an inline version comment (e.g. '# v4') for human readability.");
            Console.WriteLine("3. To look up the full 40-character commit SHA for an action tag/release, run:");
            Console.WriteLine("     git ls-remote https://github.com/<owner>/<repo>.git refs/tags/<tag>");
            Console.WriteLine("   Or for annotated tags:");
            Console.WriteLine("     git ls-remote https://github.com/<owner>/<repo>.git refs/tags/<tag>^{}");
            Console.WriteLine("Example:");
            Console.WriteLine("   uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7");
            Console.WriteLine(new string('-', 80));
            return 1;
        }

        Console.WriteLine("Success: All third-party GitHub Actions are pinned to 40-character commit SHAs.");
        return 0;
    }

    public static IReadOnlyList<string> CheckFile(string filePath, string? displayPath = null)
    {
        displayPath ??= filePath;
        var errors = new List<string>();
        string[] lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string rawLine = lines[i];
            string stripped = rawLine.Trim();
            if (stripped.Length == 0 || stripped.StartsWith('#'))
            {
                continue;
            }

            var match = UsesLineRegex().Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            string actionExpr = match.Groups[1].Value.Trim();
            if (actionExpr.Length == 0 || actionExpr.StartsWith('#'))
            {
                errors.Add($"{displayPath}:{lineNumber}: Empty 'uses:' reference.\n  Line: {stripped}");
                continue;
            }

            (string actionRef, string comment) = SplitRefAndComment(actionExpr);
            actionRef = Unquote(actionRef);

            if (actionRef.StartsWith('.'))
            {
                continue;
            }

            int atIndex = actionRef.IndexOf('@', StringComparison.Ordinal);
            if (atIndex < 0)
            {
                errors.Add($"{displayPath}:{lineNumber}: Action reference '{actionRef}' is missing '@<commit-sha>'.");
                continue;
            }

            string actionName = actionRef[..atIndex];
            string gitRef = actionRef[(atIndex + 1)..];

            if (!ShaRegex().IsMatch(gitRef))
            {
                errors.Add(
                    $"{displayPath}:{lineNumber}: Action '{actionName}' is pinned to tag/branch '{gitRef}' instead of a 40-character commit SHA.\n" +
                    $"  Line: {stripped}");
            }
            else if (string.IsNullOrWhiteSpace(comment))
            {
                errors.Add(
                    $"{displayPath}:{lineNumber}: Action '{actionName}' uses commit SHA '{gitRef}' but lacks an inline version comment (e.g. '# v4').\n" +
                    $"  Line: {stripped}");
            }
        }

        return errors;
    }

    private static (string ActionRef, string Comment) SplitRefAndComment(string actionExpr)
    {
        char quote = '\0';
        for (int i = 0; i < actionExpr.Length; i++)
        {
            char c = actionExpr[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '#' && i > 0 && char.IsWhiteSpace(actionExpr[i - 1]))
            {
                return (actionExpr[..i].Trim(), actionExpr[(i + 1)..].Trim());
            }
        }

        int bareHash = actionExpr.IndexOf('#', StringComparison.Ordinal);
        if (bareHash >= 0 && quote == '\0')
        {
            return (actionExpr[..bareHash].Trim(), "");
        }

        return (actionExpr.Trim(), "");
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1].Trim();
        }

        return value;
    }

    private static List<string> ResolveTargetWorkflowFiles(string repoRoot, IEnumerable<string> candidates)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in candidates)
        {
            string fullPath = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            if (IsWorkflowFile(rel))
            {
                result.Add(fullPath);
            }
        }

        return [.. result];
    }

    private static bool IsWorkflowFile(string relativePath) =>
        relativePath.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) &&
        (relativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
         relativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) &&
        !relativePath[".github/workflows/".Length..].Contains('/', StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? "";
        }

        return Directory.GetCurrentDirectory();
    }
}

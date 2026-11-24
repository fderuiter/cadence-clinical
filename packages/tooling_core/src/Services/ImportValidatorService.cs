using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates cross-service isolation across <c>apps/&lt;service&gt;</c>: blocks direct <c>using</c> directives
/// (<c>using</c>, <c>global using</c>, <c>using static</c>, <c>using Alias = ...</c>), fully qualified
/// <c>Cadence.Apps.&lt;Sibling&gt;</c> references, and indirect runtime reflection/type-lookup string literals naming
/// sibling <c>Cadence.Apps.&lt;Sibling&gt;</c> namespaces, ratcheted against the shrink-only baseline at
/// <see cref="BaselineRelativePath"/>.
/// </summary>
public static partial class ImportValidatorService
{
    public const string BaselineRelativePath =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt";

    public const string SourceRulePrefix = "SOURCE|";

    private static readonly HashSet<string> ExemptAppFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gateway",
        "apphost",
        "web",
        "subject-portal",
    };

    /// <summary>Validates cross-service boundaries across <c>apps/</c> against the shrink-only baseline.</summary>
    public static int ValidateImports(
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating cross-service architectural import and reflection boundaries...");

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            bool useStagedIndex = preCommit && reader is { HasAuthoritativeIndex: true };
            if (!useStagedIndex && !Directory.Exists(Path.Combine(repoRoot, "apps")))
            {
                Console.WriteLine("✔ Apps directory not found, skipping import validation.");
                return 0;
            }

            if (useStagedIndex && !reader!.Paths.Any(p => p.StartsWith("apps/", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("✔ Apps directory not found in staged index, skipping import validation.");
                return 0;
            }

            var actual = useStagedIndex
                ? ScanStagedSourceViolations(reader!)
                : ScanSourceViolations(repoRoot);
            var baseline = useStagedIndex
                ? ReadStagedSourceBaseline(reader!)
                : ReadSourceBaseline(repoRoot);

            var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
            var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

            var newViolations = actualSet.Except(baselineSet, StringComparer.Ordinal).ToList();
            var staleEntries = baselineSet.Except(actualSet, StringComparer.Ordinal).ToList();

            if (newViolations.Count > 0)
            {
                foreach (string key in newViolations)
                {
                    Console.WriteLine($"[BOUNDARY VIOLATION] {key}");
                }

                Console.WriteLine($"\n[ERROR] Found {newViolations.Count} unbaselined cross-service boundary violation(s).");
                Console.WriteLine(
                    "[REMEDIATION] Route cross-module clinical mutations through IKernelCommandSender in the central " +
                    "kernel command pipeline (ADR-2600) and asynchronous cross-service notifications through " +
                    "Cadence.Contracts events (ADR-2524) rather than importing or reflecting over sibling Cadence.Apps.* types.");
                return 1;
            }

            if (staleEntries.Count > 0)
            {
                foreach (string stale in staleEntries)
                {
                    Console.WriteLine($"[STALE BASELINE] {stale} no longer occurs; remove it from {BaselineRelativePath}.");
                }

                Console.WriteLine($"\n[ERROR] Found {staleEntries.Count} stale entry/entries in {BaselineRelativePath}.");
                return 1;
            }

            Console.WriteLine("✔ Cross-service import and reflection boundaries validated cleanly.");
            return 0;
        }
    }

    /// <summary>
    /// Scans all <c>apps/&lt;service&gt;/**/*.cs</c> files under <paramref name="repoRoot"/> and returns sorted
    /// <c>SOURCE|&lt;service&gt;|&lt;relativePath&gt;|&lt;kind&gt;:&lt;reference&gt;</c> violation keys.
    /// </summary>
    public static IReadOnlyList<string> ScanSourceViolations(string repoRoot)
    {
        string appsDir = Path.Combine(repoRoot, "apps");
        if (!Directory.Exists(appsDir))
        {
            return [];
        }

        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string appDir in Directory.GetDirectories(appsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            string appName = Path.GetFileName(appDir);
            if (ExemptAppFolders.Contains(appName))
            {
                continue;
            }

            var csFiles = Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (string file in csFiles)
            {
                string source;
                try
                {
                    source = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }

                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                foreach (string entry in ScanFileSource(appName, relPath, source))
                {
                    violations.Add(entry);
                }
            }
        }

        return [.. violations];
    }

    /// <summary>
    /// Pure source analyzer for a single file inside <c>apps/&lt;appName&gt;</c>. Detects direct <c>using</c> directives,
    /// qualified <c>Cadence.Apps.&lt;Sibling&gt;</c> references in code, and string literals referencing a sibling
    /// <c>Cadence.Apps.&lt;Sibling&gt;</c> namespace/type (such as reflection <c>GetType</c> / <c>FullName</c> lookups).
    /// </summary>
    public static IReadOnlyList<string> ScanFileSource(string appName, string relativePath, string source)
    {
        if (ExemptAppFolders.Contains(appName) || string.IsNullOrEmpty(source))
        {
            return [];
        }

        string normalizedSelf = NormalizeAppName(appName);
        string normalizedPath = relativePath.Replace('\\', '/');
        var (codeOnly, literals) = ExtractCodeAndStringLiterals(source);

        var results = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in SiblingAppNamespaceRegex().Matches(codeOnly))
        {
            string targetApp = match.Groups["app"].Value;
            if (!NormalizeAppName(targetApp).Equals(normalizedSelf, StringComparison.OrdinalIgnoreCase))
            {
                string fullReference = match.Groups["full"].Value;
                results.Add($"{SourceRulePrefix}{appName}|{normalizedPath}|code:{fullReference}");
            }
        }

        foreach (string literal in literals)
        {
            foreach (Match match in SiblingAppNamespaceRegex().Matches(literal))
            {
                string targetApp = match.Groups["app"].Value;
                if (!NormalizeAppName(targetApp).Equals(normalizedSelf, StringComparison.OrdinalIgnoreCase))
                {
                    string fullReference = match.Groups["full"].Value;
                    results.Add($"{SourceRulePrefix}{appName}|{normalizedPath}|string:{fullReference}");
                }
            }
        }

        return [.. results];
    }

    /// <summary>Reads <c>SOURCE|...</c> baseline entries from <see cref="BaselineRelativePath"/> if present.</summary>
    public static IReadOnlyList<string> ReadSourceBaseline(string repoRoot)
    {
        string fullPath = Path.Combine(repoRoot, BaselineRelativePath);
        if (!File.Exists(fullPath))
        {
            return [];
        }

        return ParseSourceBaselineLines(File.ReadAllLines(fullPath));
    }

    private static IReadOnlyList<string> ScanStagedSourceViolations(GitIndexReader reader)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string relPath in reader.Paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            string normalized = relPath.Replace('\\', '/');
            if (!normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase) ||
                !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                IsExcludedPath(normalized))
            {
                continue;
            }

            string remainder = normalized["apps/".Length..];
            int slashIndex = remainder.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex <= 0)
            {
                continue;
            }

            string appName = remainder[..slashIndex];
            if (ExemptAppFolders.Contains(appName))
            {
                continue;
            }

            if (!reader.TryReadText(relPath, out string source))
            {
                continue;
            }

            foreach (string entry in ScanFileSource(appName, normalized, source))
            {
                violations.Add(entry);
            }
        }

        return [.. violations];
    }

    private static IReadOnlyList<string> ReadStagedSourceBaseline(GitIndexReader reader) =>
        reader.TryReadText(BaselineRelativePath, out string content)
            ? ParseSourceBaselineLines(content.Split('\n'))
            : [];

    private static IReadOnlyList<string> ParseSourceBaselineLines(IEnumerable<string> lines) =>
        lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Where(line => line.StartsWith(SourceRulePrefix, StringComparison.Ordinal))
            .ToList();

    private static (string CodeWithoutCommentsOrStrings, List<string> StringLiterals) ExtractCodeAndStringLiterals(string source)
    {
        var code = new System.Text.StringBuilder(source.Length);
        var literals = new List<string>();
        int i = 0;
        int n = source.Length;

        while (i < n)
        {
            // Line comment //
            if (i + 1 < n && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // Block comment /* ... */
            if (i + 1 < n && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        code.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(n, i + 2);
                code.Append(' ');
                continue;
            }

            // Verbatim string @"..." or $@"..." / @$"..."
            if (source[i] == '@' && i + 1 < n && source[i + 1] == '"')
            {
                i += 2;
                var lit = new System.Text.StringBuilder();
                while (i < n)
                {
                    if (source[i] == '"')
                    {
                        if (i + 1 < n && source[i + 1] == '"')
                        {
                            lit.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    lit.Append(source[i]);
                    i++;
                }

                literals.Add(lit.ToString());
                code.Append("\"\"");
                continue;
            }

            // Regular or raw string "..."
            if (source[i] == '"')
            {
                int quoteCount = 0;
                while (i + quoteCount < n && source[i + quoteCount] == '"')
                {
                    quoteCount++;
                }

                if (quoteCount >= 3)
                {
                    i += quoteCount;
                    var rawLit = new System.Text.StringBuilder();
                    while (i < n)
                    {
                        int closing = 0;
                        while (i + closing < n && source[i + closing] == '"')
                        {
                            closing++;
                        }

                        if (closing >= quoteCount)
                        {
                            i += closing;
                            break;
                        }

                        rawLit.Append(source[i]);
                        i++;
                    }

                    literals.Add(rawLit.ToString());
                    code.Append("\"\"");
                    continue;
                }

                if (quoteCount == 2)
                {
                    i += 2;
                    code.Append("\"\"");
                    continue;
                }

                // Single opening quote
                i++;
                var regularLit = new System.Text.StringBuilder();
                while (i < n && source[i] != '"' && source[i] != '\n')
                {
                    if (source[i] == '\\' && i + 1 < n)
                    {
                        regularLit.Append(source[i + 1]);
                        i += 2;
                        continue;
                    }

                    regularLit.Append(source[i]);
                    i++;
                }

                if (i < n && source[i] == '"')
                {
                    i++;
                }

                literals.Add(regularLit.ToString());
                code.Append("\"\"");
                continue;
            }

            // Character literal '.'
            if (source[i] == '\'')
            {
                code.Append(source[i++]);
                while (i < n && source[i] != '\'' && source[i] != '\n')
                {
                    if (source[i] == '\\' && i + 1 < n)
                    {
                        code.Append(source[i]);
                        code.Append(source[i + 1]);
                        i += 2;
                        continue;
                    }

                    code.Append(source[i++]);
                }

                if (i < n && source[i] == '\'')
                {
                    code.Append(source[i++]);
                }

                continue;
            }

            code.Append(source[i++]);
        }

        return (code.ToString(), literals);
    }

    private static bool IsExcludedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
               || normalized.Contains("/obj/", StringComparison.Ordinal)
               || normalized.Contains("/node_modules/", StringComparison.Ordinal);
    }

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

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string NormalizeAppName(string name) =>
        name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    [GeneratedRegex(
        @"(?<full>Cadence\.Apps\.(?<app>[A-Za-z0-9_]+)(?:\.[A-Za-z0-9_]+)*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SiblingAppNamespaceRegex();
}

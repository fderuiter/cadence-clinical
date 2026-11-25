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
/// sibling <c>Cadence.Apps.&lt;Sibling&gt;</c> namespaces, ratcheted against per-service-pair shards under
/// <see cref="BaselineDirectory"/>.
/// </summary>
public static partial class ImportValidatorService
{
    public const string BaselineDirectory = "baselines/cross-service-isolation";

    public const string LegacyBaselineRelativePath =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt";

    public const string SourceRulePrefix = "SOURCE|";
    public const string IlRulePrefix = "IL|";

    private const string ShardSuffix = ".baseline.txt";

    private static readonly HashSet<string> ExemptAppFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gateway",
        "apphost",
        "web",
        "subject-portal",
    };

    /// <summary>Builds the canonical header lines for a <c>&lt;sourceService&gt;/&lt;targetService&gt;.baseline.txt</c> shard.</summary>
    public static IReadOnlyList<string> BuildShardHeader(string sourceService, string targetService) =>
    [
        $"# Cross-service isolation ratchet baseline shard for {sourceService} -> {targetService} (ImportValidatorService source scan + Mono.Cecil Ldstr / reference IL scan).",
        "# Records pre-existing sibling-service debt. Entries are sorted ordinally and may only shrink.",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check imports or CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj.",
    ];

    public static string GetShardRelativePath(string sourceService, string targetService)
    {
        string normalizedSource = NormalizeAppName(sourceService).ToLowerInvariant();
        string normalizedTarget = NormalizeAppName(targetService).ToLowerInvariant();
        if (!IsSafeServiceToken(normalizedSource) ||
            !IsSafeServiceToken(normalizedTarget) ||
            string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid cross-service baseline owner pair '{sourceService}' -> '{targetService}'.");
        }

        return $"{BaselineDirectory}/{normalizedSource}/{normalizedTarget}{ShardSuffix}";
    }

    public static string GetShardRelativePathForEntry(string entry) =>
        TryResolveOwnerPair(entry, out string sourceService, out string targetService)
            ? GetShardRelativePath(sourceService, targetService)
            : throw new FormatException($"Invalid cross-service isolation baseline entry '{entry}'.");

    public static bool TryResolveOwnerPair(string entry, out string sourceService, out string targetService)
    {
        sourceService = string.Empty;
        targetService = string.Empty;
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        if (entry.StartsWith(SourceRulePrefix, StringComparison.Ordinal))
        {
            string[] parts = entry.Split('|', 4);
            if (parts.Length != 4)
            {
                return false;
            }

            int colonIdx = parts[3].IndexOf(':', StringComparison.Ordinal);
            if (colonIdx <= 0)
            {
                return false;
            }

            string kind = parts[3][..colonIdx];
            if (kind is not ("code" or "string"))
            {
                return false;
            }

            string reference = parts[3][(colonIdx + 1)..];
            var match = SiblingAppNamespaceRegex().Match(reference);
            if (!match.Success)
            {
                return false;
            }

            string src = NormalizeAppName(parts[1]).ToLowerInvariant();
            string dst = NormalizeAppName(match.Groups["app"].Value).ToLowerInvariant();
            if (!IsSafeServiceToken(src) || !IsSafeServiceToken(dst) || string.Equals(src, dst, StringComparison.Ordinal))
            {
                return false;
            }

            sourceService = src;
            targetService = dst;
            return true;
        }

        if (entry.StartsWith(IlRulePrefix, StringComparison.Ordinal))
        {
            string[] parts = entry.Split('|', 3);
            if (parts.Length != 3 || !parts[1].StartsWith("Cadence.Apps.", StringComparison.Ordinal))
            {
                return false;
            }

            int arrowIdx = parts[2].IndexOf("->", StringComparison.Ordinal);
            if (arrowIdx <= 0)
            {
                return false;
            }

            string rawSourceApp = parts[1]["Cadence.Apps.".Length..];
            string targetExpr = parts[2][(arrowIdx + 2)..];
            var match = SiblingAppNamespaceRegex().Match(targetExpr);
            if (!match.Success)
            {
                return false;
            }

            string src = NormalizeAppName(rawSourceApp).ToLowerInvariant();
            string dst = NormalizeAppName(match.Groups["app"].Value).ToLowerInvariant();
            if (!IsSafeServiceToken(src) || !IsSafeServiceToken(dst) || string.Equals(src, dst, StringComparison.Ordinal))
            {
                return false;
            }

            sourceService = src;
            targetService = dst;
            return true;
        }

        return false;
    }

    public static bool TryExtractOwnerPairFromShardPath(
        string shardRelativePath,
        out string sourceService,
        out string targetService)
    {
        sourceService = string.Empty;
        targetService = string.Empty;
        string normalized = BaselineUnitPath.NormalizeRepoRelativePath(shardRelativePath);
        string prefix = BaselineDirectory + "/";

        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !normalized.EndsWith(ShardSuffix, StringComparison.Ordinal) ||
            normalized.Length <= prefix.Length + ShardSuffix.Length)
        {
            return false;
        }

        string remainder = normalized[prefix.Length..^ShardSuffix.Length];
        string[] segments = remainder.Split('/');
        if (segments.Length != 2)
        {
            return false;
        }

        string src = segments[0];
        string dst = segments[1];
        if (!IsSafeServiceToken(src) ||
            !IsSafeServiceToken(dst) ||
            string.Equals(src, dst, StringComparison.Ordinal))
        {
            return false;
        }

        sourceService = src;
        targetService = dst;
        return true;
    }

    /// <summary>Validates cross-service boundaries across <c>apps/</c> against the shrink-only sharded baseline.</summary>
    public static int ValidateImports(
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating cross-service architectural import and reflection boundaries...");

        bool updateBaseline = !preCommit &&
                              Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE") == "1";

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            bool useStagedIndex = preCommit && reader is { HasAuthoritativeIndex: true };

            if (!TryReadAndValidateShards(repoRoot, useStagedIndex ? reader : null, out var entriesByShard))
            {
                return 1;
            }

            bool hasApps = useStagedIndex
                ? reader!.Paths.Any(p => p.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
                : Directory.Exists(Path.Combine(repoRoot, "apps"));

            if (!hasApps && entriesByShard.Count == 0)
            {
                Console.WriteLine(useStagedIndex
                    ? "✔ Apps directory not found in staged index, skipping import validation."
                    : "✔ Apps directory not found, skipping import validation.");
                return 0;
            }

            var actual = useStagedIndex
                ? ScanStagedSourceViolations(reader!)
                : ScanSourceViolations(repoRoot);
            var baseline = entriesByShard
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .SelectMany(kv => kv.Value)
                .Where(line => line.StartsWith(SourceRulePrefix, StringComparison.Ordinal))
                .ToList();

            var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
            var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

            var newViolations = actualSet.Except(baselineSet, StringComparer.Ordinal).ToList();
            var staleEntries = baselineSet.Except(actualSet, StringComparer.Ordinal).ToList();

            if (updateBaseline && staleEntries.Count > 0)
            {
                int pruned = PruneStaleSourceEntriesFromShards(repoRoot, entriesByShard, staleEntries);
                if (pruned > 0)
                {
                    Console.WriteLine(
                        $"✔ Pruned {pruned} stale SOURCE cross-service baseline entr{(pruned == 1 ? "y" : "ies")} under '{BaselineDirectory}/'.");
                }

                staleEntries.Clear();
            }

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
                    Console.WriteLine($"[STALE BASELINE] {stale} no longer occurs; remove it from {GetShardRelativePathForEntry(stale)}.");
                }

                Console.WriteLine(
                    $"\n[ERROR] Found {staleEntries.Count} stale entry/entries under '{BaselineDirectory}/'. " +
                    "Run 'CADENCE_UPDATE_BASELINE=1 pnpm cadence check imports' to prune them.");
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

    /// <summary>Reads <c>SOURCE|...</c> baseline entries across all shards under <see cref="BaselineDirectory"/>.</summary>
    public static IReadOnlyList<string> ReadSourceBaseline(string repoRoot)
    {
        string baselineRoot = Path.Combine(repoRoot, BaselineDirectory);
        if (!Directory.Exists(baselineRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(baselineRoot, "*" + ShardSuffix, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Where(line => line.StartsWith(SourceRulePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryReadAndValidateShards(
        string repoRoot,
        GitIndexReader? reader,
        out Dictionary<string, List<string>> entriesByShard)
    {
        entriesByShard = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        bool hasLegacy = reader is { HasAuthoritativeIndex: true }
            ? reader.FileExists(LegacyBaselineRelativePath)
            : File.Exists(Path.Combine(repoRoot, LegacyBaselineRelativePath));
        if (hasLegacy)
        {
            Console.WriteLine(
                $"[ERROR] Legacy monolithic baseline '{LegacyBaselineRelativePath}' must be removed; use per-service-pair shards under '{BaselineDirectory}/'.");
            return false;
        }

        List<string> shardFiles;
        if (reader is { HasAuthoritativeIndex: true })
        {
            shardFiles = reader.Paths
                .Where(p => p.StartsWith(BaselineDirectory + "/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            string baselineRoot = Path.Combine(repoRoot, BaselineDirectory);
            if (!Directory.Exists(baselineRoot))
            {
                return true;
            }

            shardFiles = Directory.EnumerateFiles(baselineRoot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        foreach (string relPath in shardFiles)
        {
            if (!TryExtractOwnerPairFromShardPath(relPath, out string expectedSource, out string expectedTarget))
            {
                Console.WriteLine(
                    $"[ERROR] Invalid cross-service isolation baseline shard path '{relPath}'; expected '{BaselineDirectory}/<source>/<target>{ShardSuffix}'.");
                return false;
            }

            string[] rawLines;
            if (reader is { HasAuthoritativeIndex: true })
            {
                if (!reader.TryReadLines(relPath, out rawLines))
                {
                    Console.WriteLine($"[ERROR] Baseline shard '{relPath}' could not be read from the Git index.");
                    return false;
                }
            }
            else
            {
                rawLines = File.ReadAllLines(Path.Combine(repoRoot, relPath));
            }

            var entries = rawLines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();

            if (entries.Count == 0)
            {
                Console.WriteLine($"[ERROR] Empty cross-service isolation baseline shard '{relPath}' must be deleted.");
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!TryResolveOwnerPair(entries[i], out string entrySource, out string entryTarget) ||
                    !string.Equals(entrySource, expectedSource, StringComparison.Ordinal) ||
                    !string.Equals(entryTarget, expectedTarget, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"[ERROR] Entry '{entries[i]}' in '{relPath}' does not match owner pair '{expectedSource} -> {expectedTarget}'.");
                    return false;
                }

                if (i > 0 && StringComparer.Ordinal.Compare(entries[i - 1], entries[i]) >= 0)
                {
                    Console.WriteLine(
                        $"[ERROR] Cross-service isolation baseline shard '{relPath}' must be strictly sorted ordinally with no duplicate entries (at '{entries[i]}').");
                    return false;
                }
            }

            entriesByShard[relPath] = entries;
        }

        return true;
    }

    private static int PruneStaleSourceEntriesFromShards(
        string repoRoot,
        Dictionary<string, List<string>> entriesByShard,
        IReadOnlyList<string> staleSourceEntries)
    {
        var staleSet = new HashSet<string>(staleSourceEntries, StringComparer.Ordinal);
        string baselineRootFull = Path.Combine(repoRoot, BaselineDirectory);
        int pruned = 0;

        foreach (var (shardRelPath, existingEntries) in entriesByShard.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var kept = new List<string>(existingEntries.Count);
            foreach (string entry in existingEntries)
            {
                if (staleSet.Contains(entry))
                {
                    pruned++;
                }
                else
                {
                    kept.Add(entry);
                }
            }

            if (kept.Count == existingEntries.Count)
            {
                continue;
            }

            string shardFullPath = Path.Combine(repoRoot, shardRelPath);
            if (kept.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, baselineRootFull);
                }
            }
            else
            {
                _ = TryExtractOwnerPairFromShardPath(shardRelPath, out string src, out string dst);
                kept.Sort(StringComparer.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
                File.WriteAllText(shardFullPath, string.Join('\n', BuildShardHeader(src, dst).Concat(kept)) + "\n");
            }
        }

        return pruned;
    }

    private static bool IsSafeServiceToken(string value) =>
        !string.IsNullOrEmpty(value) && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

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

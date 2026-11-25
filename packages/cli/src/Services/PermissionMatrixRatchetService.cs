using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Security.Rbac;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>
/// Native-AOT-safe static role-to-permission grant inventory ratchet (<c>permission-matrix</c> gate).
/// Inventories <see cref="PermissionMatrix.ROLE_PERMISSIONS"/> alongside literal <c>Grant(...)</c> calls in
/// <c>packages/security/src/Rbac</c> and <c>apps/</c> (or staged blobs in <c>--pre-commit</c>) without dynamic
/// reflection, and ratchets them against per-resource shards under <see cref="BaselineDirectory"/>.
/// </summary>
public static partial class PermissionMatrixRatchetService
{
    public const string BaselineDirectory = "baselines/permission-matrix";

    public const string LegacyBaselineRelativePath =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt";

    private const string ShardSuffix = ".baseline.txt";
    private const string Tag = "[PERMISSION MATRIX]";

    private static readonly Dictionary<string, string> RoleConstantLookup = new(StringComparer.Ordinal)
    {
        ["ROLE_CRA"] = RoleNormalization.ROLE_CRA,
        ["ROLE_DATA_MANAGER"] = RoleNormalization.ROLE_DATA_MANAGER,
        ["ROLE_SITE_INVESTIGATOR"] = RoleNormalization.ROLE_SITE_INVESTIGATOR,
        ["ROLE_AUDITOR"] = RoleNormalization.ROLE_AUDITOR,
        ["ROLE_SPONSOR_ADMIN"] = RoleNormalization.ROLE_SPONSOR_ADMIN,
        ["ROLE_SYSADMIN"] = RoleNormalization.ROLE_SYSADMIN,
        ["ROLE_SPONSOR_DESIGNER"] = RoleNormalization.ROLE_SPONSOR_DESIGNER,
        ["ROLE_SPONSOR_DM"] = RoleNormalization.ROLE_SPONSOR_DM,
        ["ROLE_SPONSOR_MM"] = RoleNormalization.ROLE_SPONSOR_MM,
        ["ROLE_SPONSOR_STATISTICIAN"] = RoleNormalization.ROLE_SPONSOR_STATISTICIAN,
        ["ROLE_INVESTIGATOR"] = RoleNormalization.ROLE_INVESTIGATOR,
        ["ROLE_CRC"] = RoleNormalization.ROLE_CRC,
        ["ROLE_CRA_CANONICAL"] = RoleNormalization.ROLE_CRA_CANONICAL,
        ["ROLE_SUBJECT"] = RoleNormalization.ROLE_SUBJECT,
        ["ROLE_AUDITOR_CANONICAL"] = RoleNormalization.ROLE_AUDITOR_CANONICAL,
        ["ROLE_EXTERNAL_MONITOR"] = RoleNormalization.ROLE_EXTERNAL_MONITOR,
        ["ROLE_REVIEWER"] = RoleNormalization.ROLE_REVIEWER,
        ["ROLE_FINANCE_OFFICER"] = RoleNormalization.ROLE_FINANCE_OFFICER,
        ["ROLE_PHARMACIST"] = RoleNormalization.ROLE_PHARMACIST,
        ["ROLE_UNBLINDED_STATISTICIAN"] = RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
        ["ROLE_IDMC"] = RoleNormalization.ROLE_IDMC,
        ["ROLE_EMERGENCY_UNBLINDER"] = RoleNormalization.ROLE_EMERGENCY_UNBLINDER,
        ["ROLE_PRINCIPAL_INVESTIGATOR"] = RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
        ["ROLE_AUTHORIZED_ER_PHYSICIAN"] = RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["ROLE_LEAD_INVESTIGATOR"] = RoleNormalization.ROLE_LEAD_INVESTIGATOR,
        ["EtmfServiceRole"] = PermissionMatrix.EtmfServiceRole,
        ["CecChairRole"] = PermissionMatrix.CecChairRole,
    };

    /// <summary>Builds the canonical header lines for a <c>baselines/permission-matrix/&lt;resource&gt;.baseline.txt</c> shard.</summary>
    public static IReadOnlyList<string> BuildShardHeader(string resource) =>
    [
        $"# Shrink-only RBAC role-to-permission grant baseline shard for resource '{resource}'.",
        "# Sorted ordinally as <role>|<resource>|<action> (lowercase).",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check permission-matrix or CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj.",
    ];

    public static string GetShardRelativePath(string resource)
    {
        if (!IsSafeResourceToken(resource))
        {
            throw new ArgumentException($"Invalid RBAC resource shard token '{resource}'.", nameof(resource));
        }

        return $"{BaselineDirectory}/{resource}{ShardSuffix}";
    }

    public static string GetShardRelativePathForGrant(string grant) =>
        TryExtractResourceOwner(grant, out string resource)
            ? GetShardRelativePath(resource)
            : throw new FormatException($"Invalid RBAC grant baseline entry '{grant}'.");

    public static bool TryExtractResourceOwner(string grant, out string resource)
    {
        resource = string.Empty;
        if (string.IsNullOrWhiteSpace(grant))
        {
            return false;
        }

        string[] parts = grant.Split('|');
        if (parts.Length != 3 ||
            parts[0].Length == 0 ||
            !IsSafeResourceToken(parts[1]) ||
            parts[2].Length == 0)
        {
            return false;
        }

        resource = parts[1];
        return true;
    }

    public static bool TryExtractResourceFromShardPath(string shardRelativePath, out string resource)
    {
        resource = string.Empty;
        string normalized = BaselineUnitPath.NormalizeRepoRelativePath(shardRelativePath);
        string prefix = BaselineDirectory + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !normalized.EndsWith(ShardSuffix, StringComparison.Ordinal) ||
            normalized.Length <= prefix.Length + ShardSuffix.Length)
        {
            return false;
        }

        string candidate = normalized[prefix.Length..^ShardSuffix.Length];
        if (candidate.Contains('/', StringComparison.Ordinal) || !IsSafeResourceToken(candidate))
        {
            return false;
        }

        resource = candidate;
        return true;
    }

    /// <summary>Writes per-resource RBAC baseline shards under <see cref="BaselineDirectory"/> and deletes empty shards.</summary>
    public static void WriteBaselineShards(string repoRoot, IEnumerable<string> grants)
    {
        string rootFullPath = Path.Combine(repoRoot, BaselineDirectory);
        var grouped = grants
            .GroupBy(GetShardRelativePathForGrant, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        if (Directory.Exists(rootFullPath))
        {
            foreach (string existingFull in Directory.GetFiles(rootFullPath, "*" + ShardSuffix, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, existingFull).Replace('\\', '/');
                if (!grouped.TryGetValue(rel, out var entries) || entries.Count == 0)
                {
                    File.Delete(existingFull);
                    BaselineUnitPath.DeleteEmptyParentDirectories(existingFull, rootFullPath);
                }
            }
        }

        foreach (var (shardRel, entries) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (entries.Count == 0)
            {
                continue;
            }

            _ = TryExtractResourceFromShardPath(shardRel, out string resource);
            string fullPath = Path.Combine(repoRoot, shardRel);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, string.Join('\n', BuildShardHeader(resource).Concat(entries)) + "\n");
        }
    }

    /// <summary>
    /// Verifies static role-to-permission grants against <see cref="BaselineDirectory"/> shards in <paramref name="repoRoot"/>.
    /// Returns <c>0</c> when clean and <c>1</c> when the baseline is missing, contains stale/unsorted/empty shards, or misses new grants.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        bool updateBaseline = !preCommit &&
                              Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE") == "1";

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        Dictionary<string, List<string>> entriesByShard;
        IReadOnlyList<string> actual;
        using (ownedReader)
        {
            bool useStagedIndex = preCommit && reader is { HasAuthoritativeIndex: true };
            if (!TryReadAndValidateShards(repoRoot, useStagedIndex ? reader : null, out entriesByShard))
            {
                return 1;
            }

            actual = InventoryGrants(repoRoot, preCommit, useStagedIndex ? reader : null);
        }

        var baseline = entriesByShard
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .SelectMany(kv => kv.Value)
            .ToList();

        var (added, stale) = Compare(actual, baseline);
        if (updateBaseline && stale.Count > 0)
        {
            int pruned = PruneStaleGrantsFromShards(repoRoot, entriesByShard, stale);
            if (pruned > 0)
            {
                Console.WriteLine($"{Tag} ✔ Pruned {pruned} stale RBAC grant(s) under '{BaselineDirectory}/'.");
            }

            stale = [];
        }

        if (added.Count == 0 && stale.Count == 0)
        {
            Console.WriteLine($"{Tag} ✔ {actual.Count} static role-to-permission grant(s) match '{BaselineDirectory}/'.");
            return 0;
        }

        if (added.Count > 0)
        {
            Console.WriteLine($"{Tag} [ERROR] {added.Count} unbaselined role-to-permission grant(s) not in '{BaselineDirectory}/':");
            foreach (string grant in added.Take(25))
            {
                Console.WriteLine($"  + {grant}");
            }

            if (added.Count > 25)
            {
                Console.WriteLine($"  ... and {added.Count - 25} more");
            }

            Console.WriteLine(
                $"{Tag} Remediation: Evaluate clinical authority and capability grants through the central kernel " +
                "policy evaluator (AuthorityEvaluator / Capability grants in the kernel command pipeline, " +
                "CLINICAL_KERNEL_SPECIFICATION.md §4 and ADR-2600) rather than adding static role-to-permission " +
                "entries to PermissionMatrix.");
        }

        if (stale.Count > 0)
        {
            Console.WriteLine(
                $"{Tag} [ERROR] {stale.Count} stale baseline grant(s) no longer exist in PermissionMatrix; " +
                $"run 'CADENCE_UPDATE_BASELINE=1 pnpm cadence check permission-matrix' to prune them from '{BaselineDirectory}/':");
            foreach (string grant in stale.Take(25))
            {
                Console.WriteLine($"  - {grant}");
            }

            if (stale.Count > 25)
            {
                Console.WriteLine($"  ... and {stale.Count - 25} more");
            }
        }

        return 1;
    }

    /// <summary>Returns the canonical sorted <c>role|resource|action</c> triples from <see cref="PermissionMatrix.ROLE_PERMISSIONS"/>.</summary>
    public static IReadOnlyList<string> GetRuntimeGrants()
    {
        var grants = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (role, resources) in PermissionMatrix.ROLE_PERMISSIONS)
        {
            string normalizedRole = role.Trim().ToLowerInvariant();
            foreach (var (resource, actions) in resources)
            {
                string normalizedResource = resource.Trim().ToLowerInvariant();
                foreach (string action in actions)
                {
                    string normalizedAction = action.Trim().ToLowerInvariant();
                    grants.Add($"{normalizedRole}|{normalizedResource}|{normalizedAction}");
                }
            }
        }

        return [.. grants];
    }

    /// <summary>
    /// Inventories runtime <see cref="PermissionMatrix.ROLE_PERMISSIONS"/> unioned with any literal <c>Grant(...)</c>
    /// statements found in <c>packages/security/src/Rbac</c> or <c>apps/</c> under <paramref name="repoRoot"/> so
    /// uncompiled source edits or shadow grants fail immediately even before rebuilding the Native AOT CLI binary.
    /// </summary>
    public static IReadOnlyList<string> InventoryGrants(
        string repoRoot,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        var grants = new SortedSet<string>(GetRuntimeGrants(), StringComparer.Ordinal);
        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            if (preCommit && reader is { HasAuthoritativeIndex: true })
            {
                foreach (string relPath in reader.Paths)
                {
                    if (!IsRbacScanCandidate(relPath))
                    {
                        continue;
                    }

                    if (reader.TryReadText(relPath, out string content))
                    {
                        foreach (string grant in ExtractDirectSourceGrants(content))
                        {
                            grants.Add(grant);
                        }
                    }
                }

                return [.. grants];
            }
        }

        foreach (string dirName in new[] { Path.Combine(repoRoot, "packages", "security", "src", "Rbac"), Path.Combine(repoRoot, "apps") })
        {
            if (!Directory.Exists(dirName))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dirName, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string grant in ExtractDirectSourceGrants(content))
                {
                    grants.Add(grant);
                }
            }
        }

        return [.. grants];
    }

    /// <summary>Extracts literal and <c>RoleNormalization.ROLE_*</c> <c>Grant(role, resource, actions...)</c> triples from C# source.</summary>
    public static IReadOnlyList<string> ExtractDirectSourceGrants(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var discovered = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in GrantCallRegex().Matches(source))
        {
            string? role = ResolveRoleToken(match.Groups["role"].Value);
            if (string.IsNullOrEmpty(role))
            {
                continue;
            }

            string resource = match.Groups["resource"].Value.Trim().ToLowerInvariant();
            string actionsList = match.Groups["actions"].Value;
            foreach (Match actionMatch in QuotedLiteralRegex().Matches(actionsList))
            {
                string action = actionMatch.Groups["val"].Value.Trim().ToLowerInvariant();
                if (action.Length > 0)
                {
                    discovered.Add($"{role}|{resource}|{action}");
                }
            }
        }

        return [.. discovered];
    }

    /// <summary>Compares actual grants against baseline grants.</summary>
    public static (IReadOnlyList<string> Added, IReadOnlyList<string> Stale) Compare(
        IEnumerable<string> actual,
        IEnumerable<string> baseline)
    {
        var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
        var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return (
            [.. actualSet.Except(baselineSet, StringComparer.Ordinal)],
            [.. baselineSet.Except(actualSet, StringComparer.Ordinal)]);
    }

    public static IReadOnlyList<string> ParseBaselineLines(string content) =>
        [.. content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

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
                $"{Tag} [ERROR] Legacy monolithic baseline '{LegacyBaselineRelativePath}' must be removed; use per-resource shards under '{BaselineDirectory}/'.");
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
            string rootFullPath = Path.Combine(repoRoot, BaselineDirectory);
            if (!Directory.Exists(rootFullPath))
            {
                Console.WriteLine($"{Tag} [ERROR] Required baseline directory '{BaselineDirectory}' was not found.");
                return false;
            }

            shardFiles = Directory.EnumerateFiles(rootFullPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        if (shardFiles.Count == 0)
        {
            Console.WriteLine($"{Tag} [ERROR] Required baseline directory '{BaselineDirectory}' contained no shards.");
            return false;
        }

        foreach (string relPath in shardFiles)
        {
            if (!TryExtractResourceFromShardPath(relPath, out string expectedResource))
            {
                Console.WriteLine(
                    $"{Tag} [ERROR] Invalid permission-matrix baseline shard path '{relPath}'; expected '{BaselineDirectory}/<resource>{ShardSuffix}'.");
                return false;
            }

            string content;
            if (reader is { HasAuthoritativeIndex: true })
            {
                if (!reader.TryReadText(relPath, out content))
                {
                    Console.WriteLine($"{Tag} [ERROR] Baseline shard '{relPath}' could not be read from the Git index.");
                    return false;
                }
            }
            else
            {
                content = File.ReadAllText(Path.Combine(repoRoot, relPath));
            }

            var entries = ParseBaselineLines(content).ToList();
            if (entries.Count == 0)
            {
                Console.WriteLine($"{Tag} [ERROR] Empty permission-matrix baseline shard '{relPath}' must be deleted.");
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!TryExtractResourceOwner(entries[i], out string entryResource) ||
                    !string.Equals(entryResource, expectedResource, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"{Tag} [ERROR] Entry '{entries[i]}' in '{relPath}' belongs in resource shard '{entryResource}'.");
                    return false;
                }

                if (i > 0 && StringComparer.Ordinal.Compare(entries[i - 1], entries[i]) >= 0)
                {
                    Console.WriteLine(
                        $"{Tag} [ERROR] Permission-matrix baseline shard '{relPath}' must be strictly sorted ordinally with no duplicate entries (at '{entries[i]}').");
                    return false;
                }
            }

            entriesByShard[relPath] = entries;
        }

        return true;
    }

    private static int PruneStaleGrantsFromShards(
        string repoRoot,
        Dictionary<string, List<string>> entriesByShard,
        IReadOnlyList<string> staleGrants)
    {
        var staleSet = new HashSet<string>(staleGrants, StringComparer.Ordinal);
        string rootFullPath = Path.Combine(repoRoot, BaselineDirectory);
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
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, rootFullPath);
                }
            }
            else
            {
                _ = TryExtractResourceFromShardPath(shardRelPath, out string resource);
                kept.Sort(StringComparer.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
                File.WriteAllText(shardFullPath, string.Join('\n', BuildShardHeader(resource).Concat(kept)) + "\n");
            }
        }

        return pruned;
    }

    private static bool IsSafeResourceToken(string token) =>
        !string.IsNullOrEmpty(token) &&
        token.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-');

    private static bool IsRbacScanCandidate(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
               (normalized.StartsWith("packages/security/src/Rbac/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveRoleToken(string rawRoleToken)
    {
        string trimmed = rawRoleToken.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Trim().ToLowerInvariant();
        }

        int dotIndex = trimmed.LastIndexOf('.');
        string symbol = dotIndex >= 0 ? trimmed[(dotIndex + 1)..] : trimmed;
        return RoleConstantLookup.TryGetValue(symbol, out string? resolved)
            ? resolved.ToLowerInvariant()
            : null;
    }

    [GeneratedRegex(
        @"\bGrant\s*\(\s*(?<role>""[^""]+""|(?:RoleNormalization\.|PermissionMatrix\.)?[A-Za-z0-9_]+)\s*,\s*""(?<resource>[^""]+)""\s*,\s*(?<actions>(?:""[^""]+""\s*,?\s*)+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex GrantCallRegex();

    [GeneratedRegex(@"""(?<val>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteralRegex();
}

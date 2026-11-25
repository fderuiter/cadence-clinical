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
/// reflection, and rejects any unbaselined grant addition or unremoved stale grant against
/// <see cref="BaselineRelativePath"/>.
/// </summary>
public static partial class PermissionMatrixRatchetService
{
    public const string BaselineRelativePath =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt";

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
        ["ROLE_TRIAL_COORDINATOR"] = RoleNormalization.ROLE_TRIAL_COORDINATOR,
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
        ["CtmsServiceRole"] = PermissionMatrix.CtmsServiceRole,
        ["KnowledgeServiceRole"] = PermissionMatrix.KnowledgeServiceRole,
        ["CecChairRole"] = PermissionMatrix.CecChairRole,
    };

    /// <summary>
    /// Verifies static role-to-permission grants against <see cref="BaselineRelativePath"/> in <paramref name="repoRoot"/>.
    /// Returns <c>0</c> when clean and <c>1</c> when the baseline is missing, contains stale entries, or misses new grants.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        string? baselineContent = ReadBaselineContent(repoRoot, preCommit, indexReader);
        if (baselineContent is null)
        {
            Console.WriteLine($"{Tag} [ERROR] Required baseline '{BaselineRelativePath}' was not found.");
            return 1;
        }

        var baseline = ParseBaselineLines(baselineContent);
        var actual = InventoryGrants(repoRoot, preCommit, indexReader);

        var (added, stale) = Compare(actual, baseline);
        if (added.Count == 0 && stale.Count == 0)
        {
            Console.WriteLine($"{Tag} ✔ {actual.Count} static role-to-permission grant(s) match {BaselineRelativePath}.");
            return 0;
        }

        if (added.Count > 0)
        {
            Console.WriteLine($"{Tag} [ERROR] {added.Count} unbaselined role-to-permission grant(s) not in {BaselineRelativePath}:");
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
            Console.WriteLine($"{Tag} [ERROR] {stale.Count} stale baseline grant(s) no longer exist in PermissionMatrix; remove them from {BaselineRelativePath}:");
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

    private static string? ReadBaselineContent(string repoRoot, bool preCommit, GitIndexReader? indexReader)
    {
        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            if (preCommit && reader is { HasAuthoritativeIndex: true })
            {
                return reader.TryReadText(BaselineRelativePath, out string stagedText)
                    ? stagedText
                    : null;
            }
        }

        string fullPath = Path.Combine(repoRoot, BaselineRelativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

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

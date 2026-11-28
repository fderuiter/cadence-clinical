using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>
/// The <c>baseline-ratchet</c> gate (#5318): a ratchet baseline may only shrink. Every tracked ratchet baseline (the
/// kernel-boundary shards, kernel-rule-coverage shards, <c>baselines/deid/*.baseline.txt</c> shards,
/// <c>baselines/secrets/*.secrets.baseline.json</c> shards, and any standalone <c>*.baseline.txt</c> or
/// <c>*-baseline.txt</c> file) is aggregated per gate as one multiset across every commit in <c>compareBase..HEAD</c>
/// and in the working tree. Any commit that grows a baseline gate must carry a file-and-count-scoped
/// <c>Baseline-Growth: &lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c> trailer, and dirty working-tree baseline growth
/// is always rejected. In <c>--pre-commit</c>, staged baseline growth is compared against <c>HEAD</c> (and
/// <c>MERGE_HEAD</c> during a merge) and admitted only when <c>CADENCE_BASELINE_GROWTH</c> supplies matching
/// <c>&lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c> approval(s).
/// </summary>
public static partial class BaselineRatchetService
{
    /// <summary>The ref a branch is compared with, as in <c>cadence ci</c>.</summary>
    public const string DefaultBaseRef = GitRevisionScopeService.DefaultBaseRef;

    /// <summary>Fallback local branch ref used when <c>origin/main</c> is absent.</summary>
    public const string FallbackLocalBaseRef = "refs/heads/main";

    /// <summary>The commit trailer that admits baseline growth on a commit.</summary>
    public const string TrailerKey = "Baseline-Growth";

    /// <summary>Admits staged baseline growth in the pre-commit hook, before the commit (and its trailer) exists.</summary>
    public const string GrowthEnvironmentVariable = "CADENCE_BASELINE_GROWTH";

    public const string KernelBoundariesGate = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries";
    public const string KernelRuleCoverageGate = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage";
    public const string DeidGate = BaselinesRootPrefix + "deid";
    public const string SecretsGate = SecretsBaselineCleanerService.SecretsDirectory;

    private const string BaselinesRootPrefix = "baselines/";
    private const string Tag = "[BASELINE RATCHET]";
    private const string LegacySecretsBaseline = ".secrets.baseline";
    private const string LegacyDeidBaseline = ".deid-baseline.txt";
    private const string LegacyKernelBoundariesBaseline = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt";
    private const string LegacyKernelRuleCoverageBaseline = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt";
    private const int MaxListed = 20;

    /// <summary>One baseline file or gate that gained entries.</summary>
    /// <param name="Path">The baseline shard or gate path, relative to the repository root.</param>
    /// <param name="Added">The entries it gained, once per added occurrence.</param>
    public sealed record Growth(string Path, IReadOnlyList<string> Added)
    {
        /// <summary>Individual shard paths within the gate whose file-level additions contributed to <see cref="Added"/>.</summary>
        public IReadOnlyList<string> GrownShards { get; init; } = [];

        /// <summary>Per-shard net added entry counts for <see cref="GrownShards"/>.</summary>
        public IReadOnlyDictionary<string, int> GrownShardCounts { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>One commit's unadmitted or admitted baseline growth.</summary>
    public sealed record CommitGrowthViolation(string CommitSha, Growth Growth);

    /// <summary>Whether <paramref name="path"/> names a ratchet baseline file or shard the gate checks.</summary>
    public static bool IsRatchetBaseline(string path)
    {
        string normalized = NormalizeBaselinePath(path);
        string name = Path.GetFileName(normalized);
        return name.Equals(LegacySecretsBaseline, StringComparison.Ordinal)
            || (normalized.StartsWith(SecretsGate + "/", StringComparison.Ordinal)
                && name.EndsWith(SecretsBaselineCleanerService.ShardSuffix, StringComparison.Ordinal))
            || name.EndsWith(".baseline.txt", StringComparison.Ordinal)
            || name.EndsWith("-baseline.txt", StringComparison.Ordinal);
    }

    /// <summary>Maps a baseline file or shard path to its owning gate key for multiset aggregation.</summary>
    public static string GetGateKey(string path)
    {
        string normalized = NormalizeBaselinePath(path);
        if (normalized.Equals(LegacyKernelBoundariesBaseline, StringComparison.Ordinal) ||
            normalized.Equals(KernelBoundariesGate, StringComparison.Ordinal) ||
            normalized.StartsWith(KernelBoundariesGate + "/", StringComparison.Ordinal))
        {
            return KernelBoundariesGate;
        }

        if (normalized.Equals(LegacyKernelRuleCoverageBaseline, StringComparison.Ordinal) ||
            normalized.Equals(KernelRuleCoverageGate, StringComparison.Ordinal) ||
            normalized.StartsWith(KernelRuleCoverageGate + "/", StringComparison.Ordinal))
        {
            return KernelRuleCoverageGate;
        }

        if (normalized.Equals(LegacyDeidBaseline, StringComparison.Ordinal) ||
            normalized.Equals(DeidGate, StringComparison.Ordinal) ||
            normalized.StartsWith(DeidGate + "/", StringComparison.Ordinal))
        {
            return DeidGate;
        }

        if (normalized.Equals(LegacySecretsBaseline, StringComparison.Ordinal) ||
            normalized.Equals(SecretsGate, StringComparison.Ordinal) ||
            normalized.StartsWith(SecretsGate + "/", StringComparison.Ordinal))
        {
            return SecretsGate;
        }

        return normalized;
    }

    public static bool IsKnownGateKey(string path)
    {
        string normalized = NormalizeBaselinePath(path);
        return normalized is KernelBoundariesGate or KernelRuleCoverageGate or DeidGate or SecretsGate;
    }

    /// <summary>
    /// Parses a file-and-count-scoped <c>Baseline-Growth</c> trailer value of the form
    /// <c>&lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c>. Rejects unscoped reasons, count-less trailers, and
    /// non-positive counts.
    /// </summary>
    public static bool TryParseTrailer(
        string? rawTrailerValue,
        out string baselinePath,
        out int approvedCount,
        out string reason)
    {
        baselinePath = "";
        approvedCount = 0;
        reason = "";
        if (string.IsNullOrWhiteSpace(rawTrailerValue))
        {
            return false;
        }

        string trimmed = rawTrailerValue.Trim();
        int separator = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        string left = trimmed[..separator].Trim();
        string candidateReason = trimmed[(separator + 3)..].Trim();
        if (left.Length == 0 || candidateReason.Length == 0)
        {
            return false;
        }

        int countSeparator = left.LastIndexOf(' ');
        if (countSeparator <= 0)
        {
            return false;
        }

        string countToken = left[(countSeparator + 1)..].Trim();
        if (countToken.Length < 2 ||
            countToken[0] != '+' ||
            !int.TryParse(countToken.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedCount) ||
            parsedCount <= 0)
        {
            return false;
        }

        string candidatePath = NormalizeBaselinePath(left[..countSeparator]);
        if (candidatePath.Length == 0 ||
            candidatePath.Contains(' ', StringComparison.Ordinal) ||
            (!IsRatchetBaseline(candidatePath) && !IsKnownGateKey(candidatePath)))
        {
            return false;
        }

        baselinePath = candidatePath;
        approvedCount = parsedCount;
        reason = candidateReason;
        return true;
    }

    /// <summary>
    /// The entries of a baseline: one per non-blank line that is not a <c>#</c> comment, or, for
    /// <c>.secrets.baseline</c> / <c>*.secrets.baseline.json</c>, one <c>filename|type|hashed_secret</c> per allowlisted secret.
    /// </summary>
    /// <exception cref="JsonException">A secrets baseline JSON file is not valid JSON.</exception>
    public static IReadOnlyList<string> Entries(string path, string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        string name = Path.GetFileName(path);
        if (name.Equals(LegacySecretsBaseline, StringComparison.Ordinal) ||
            name.EndsWith(SecretsBaselineCleanerService.ShardSuffix, StringComparison.Ordinal))
        {
            return SecretEntries(content);
        }

        return [.. content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];
    }

    /// <summary>
    /// The entries in <paramref name="after"/> that <paramref name="before"/> does not have, counted as a multiset: a
    /// reordered list adds nothing, and a duplicated entry adds one.
    /// </summary>
    public static IReadOnlyList<string> Additions(IEnumerable<string> before, IEnumerable<string> after)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string entry in before)
        {
            remaining[entry] = remaining.GetValueOrDefault(entry) + 1;
        }

        var added = new List<string>();
        foreach (string entry in after)
        {
            if (remaining.TryGetValue(entry, out int count) && count > 0)
            {
                remaining[entry] = count - 1;
            }
            else
            {
                added.Add(entry);
            }
        }

        added.Sort(StringComparer.Ordinal);
        return added;
    }

    /// <summary>
    /// Checks every ratchet baseline: in <c>--pre-commit</c>, compares staged copies against <c>HEAD</c> and honours
    /// file-and-count-scoped <c>CADENCE_BASELINE_GROWTH</c>; otherwise rejects dirty working-tree growth and requires
    /// every commit in <c>compareBase..HEAD</c> that grows a baseline gate to carry a
    /// <c>Baseline-Growth: &lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c> trailer matching the added count for that
    /// gate. Falls back from <c>origin/main</c> to <c>refs/heads/main</c> when <c>origin/main</c> is absent, and fails
    /// closed when neither ref exists.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit,
        string? baseRef = DefaultBaseRef,
        Func<string, string?>? environment = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(baseRef);

        try
        {
            if (preCommit)
            {
                var stagedGrowth = FindStagedGrowthAgainstHead(repoRoot);
                return EvaluatePreCommit(stagedGrowth, environment(GrowthEnvironmentVariable));
            }

            var eventScope = GitRevisionScopeService.ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef);
            string? headCommit = eventScope.HeadCommit;
            if (eventScope.Kind == GitEventScopeKind.UnprotectedTargetRef)
            {
                Console.WriteLine($"{Tag} Unsupported target ref '{eventScope.GitRef}' for event '{eventScope.EventName}'; failing closed.");
                return 1;
            }

            if (eventScope.Kind == GitEventScopeKind.TargetRef)
            {
                if (string.IsNullOrEmpty(headCommit))
                {
                    Console.WriteLine($"{Tag} HEAD commit could not be resolved for target ref '{eventScope.GitRef}'; failing closed.");
                    return 1;
                }

                var targetDirtyGrowth = FindDirtyWorkingTreeGrowth(repoRoot, headCommit);
                if (targetDirtyGrowth.Count > 0)
                {
                    PrintDirtyGrowthFailure(targetDirtyGrowth);
                    return 1;
                }

                Console.WriteLine($"{Tag} No uncommitted ratchet baseline growth at {eventScope.GitRef}.");
                return 0;
            }

            string? compareBase;
            string scopeLabel;

            if (eventScope.Kind == GitEventScopeKind.PushCommitRange && !string.IsNullOrEmpty(eventScope.CompareBase))
            {
                compareBase = eventScope.CompareBase;
                scopeLabel = compareBase[..Math.Min(12, compareBase.Length)];
            }
            else if (eventScope.Kind == GitEventScopeKind.NewBranchPush)
            {
                if (GitRevisionScopeService.IsBranchHeadRef(eventScope.GitRef))
                {
                    compareBase = eventScope.CompareBase;
                    scopeLabel = eventScope.EffectiveBaseRef;
                }
                else
                {
                    compareBase = ResolveBranchCompareBaseWithFallback(repoRoot, effectiveBaseRef, out scopeLabel);
                }
            }
            else
            {
                compareBase = ResolveBranchCompareBaseWithFallback(repoRoot, effectiveBaseRef, out scopeLabel);
            }

            if (string.IsNullOrEmpty(compareBase))
            {
                string fallbackNote = string.Equals(effectiveBaseRef, DefaultBaseRef, StringComparison.Ordinal)
                    ? $" (and fallback '{FallbackLocalBaseRef}')"
                    : "";
                Console.WriteLine($"{Tag} {effectiveBaseRef}{fallbackNote} not found or shares no history with HEAD, so the baselines cannot be compared.");
                Console.WriteLine("  Fetch it with full history (actions/checkout 'fetch-depth: 0', or 'git fetch origin main').");
                return 1;
            }

            var dirtyGrowth = FindDirtyWorkingTreeGrowth(repoRoot, headCommit);
            if (dirtyGrowth.Count > 0)
            {
                PrintDirtyGrowthFailure(dirtyGrowth);
                return 1;
            }

            var (violations, admitted) = EvaluateCommitRange(repoRoot, compareBase);
            if (violations.Count > 0)
            {
                PrintCommitViolations(violations, scopeLabel);
                return 1;
            }

            if (admitted.Count > 0)
            {
                Console.WriteLine($"{Tag} Baseline growth admitted by per-commit '{TrailerKey}:' trailers:");
                foreach (var (commitSha, path, count, reason) in admitted)
                {
                    string shortSha = commitSha[..Math.Min(12, commitSha.Length)];
                    Console.WriteLine($"  {shortSha}: {TrailerKey}: {path} +{count} - {reason}");
                }

                return 0;
            }

            Console.WriteLine($"{Tag} No ratchet baseline grew since {scopeLabel}.");
            return 0;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"{Tag} Secrets baseline JSON is not valid: {ex.Message}");
            return 1;
        }
    }

    private static string? ResolveBranchCompareBaseWithFallback(string repoRoot, string effectiveBaseRef, out string scopeLabel)
    {
        string? primary = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
        if (!string.IsNullOrEmpty(primary))
        {
            scopeLabel = effectiveBaseRef;
            return primary;
        }

        if (string.Equals(effectiveBaseRef, DefaultBaseRef, StringComparison.Ordinal))
        {
            string? localMain = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, FallbackLocalBaseRef);
            if (!string.IsNullOrEmpty(localMain))
            {
                scopeLabel = FallbackLocalBaseRef;
                return localMain;
            }
        }

        scopeLabel = effectiveBaseRef;
        return null;
    }

    private static List<string> SecretEntries(string content)
    {
        using var document = JsonDocument.Parse(content);
        var entries = new List<string>();
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Object)
        {
            return entries;
        }

        foreach (var file in results.EnumerateObject())
        {
            if (file.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var secret in file.Value.EnumerateArray())
            {
                entries.Add($"{file.Name}|{Property(secret, "type")}|{Property(secret, "hashed_secret")}");
            }
        }

        return entries;
    }

    private static string Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? ReadWorkingTree(string repoRoot, string path)
    {
        string full = Path.Combine(repoRoot, path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static string NormalizeBaselinePath(string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}

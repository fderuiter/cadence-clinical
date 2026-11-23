using System;
using System.Collections.Generic;
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
/// and in the working tree. Any commit that grows a baseline gate must carry a
/// <c>Baseline-Growth: &lt;path&gt; - &lt;reason&gt;</c> trailer, and dirty working-tree baseline growth is always
/// rejected. In <c>--pre-commit</c>, staged baseline growth is compared against <c>HEAD</c> (and <c>MERGE_HEAD</c>
/// during a merge) and admitted only when <c>CADENCE_BASELINE_GROWTH</c> is set.
/// </summary>
public static class BaselineRatchetService
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
    /// Parses a <c>Baseline-Growth</c> trailer value of the form <c>&lt;path&gt; - &lt;reason&gt;</c>.
    /// </summary>
    public static bool TryParseTrailer(string? rawTrailerValue, out string baselinePath, out string reason)
    {
        baselinePath = "";
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

        string candidatePath = NormalizeBaselinePath(trimmed[..separator]);
        string candidateReason = trimmed[(separator + 3)..].Trim();
        if (candidatePath.Length == 0 ||
            candidateReason.Length == 0 ||
            (!IsRatchetBaseline(candidatePath) && !IsKnownGateKey(candidatePath)))
        {
            return false;
        }

        baselinePath = candidatePath;
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
    /// <c>CADENCE_BASELINE_GROWTH</c>; otherwise rejects dirty working-tree growth and requires every commit in
    /// <c>compareBase..HEAD</c> that grows a baseline gate to carry a <c>Baseline-Growth: &lt;path&gt; - &lt;reason&gt;</c>
    /// trailer. Falls back from <c>origin/main</c> to <c>refs/heads/main</c> when <c>origin/main</c> is absent, and fails
    /// closed when neither ref exists.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit,
        string? baseRef = DefaultBaseRef,
        Func<string, string?>? environment = null,
        string? pushBefore = null)
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

            string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");
            string? compareBase;
            string scopeLabel;

            if (!string.IsNullOrWhiteSpace(pushBefore))
            {
                string? validPushBase = GitRevisionScopeService.TryResolveValidPushBase(repoRoot, pushBefore, headCommit);
                if (validPushBase is not null)
                {
                    compareBase = validPushBase;
                    scopeLabel = validPushBase[..Math.Min(12, validPushBase.Length)];
                }
                else
                {
                    compareBase = ResolveBranchCompareBaseWithFallback(repoRoot, effectiveBaseRef, out scopeLabel) ?? headCommit;
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
                foreach (var (commitSha, path, reason) in admitted)
                {
                    string shortSha = commitSha[..Math.Min(12, commitSha.Length)];
                    Console.WriteLine($"  {shortSha}: {TrailerKey}: {path} - {reason}");
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

    private static int EvaluatePreCommit(IReadOnlyList<Growth> stagedGrowth, string? stagedReason)
    {
        if (stagedGrowth.Count == 0)
        {
            Console.WriteLine($"{Tag} No staged ratchet baseline grew against HEAD.");
            return 0;
        }

        string summary = string.Join(", ", stagedGrowth.Select(g => $"{g.Path} +{g.Added.Count}"));
        if (TryAdmitStagedGrowth(stagedGrowth, stagedReason, out var reasonByPath))
        {
            Console.WriteLine($"{Tag} {GrowthEnvironmentVariable} admits the staged baseline growth ({summary}).");
            Console.WriteLine("  Include these trailers in this commit's message ('cadence check' and CI require them per file):");
            foreach (string path in stagedGrowth.Select(file => file.Path))
            {
                Console.WriteLine($"    {TrailerKey}: {path} - {reasonByPath[path]}");
            }

            return 0;
        }

        Console.WriteLine($"{Tag} These staged ratchet baselines grew relative to HEAD, but a baseline may only shrink:");
        PrintGrowthList(stagedGrowth);
        Console.WriteLine("  Fix the new violations instead of recording them. If the debt must be accepted:");
        foreach (string path in stagedGrowth.Select(file => file.Path))
        {
            Console.WriteLine($"    git commit --trailer \"{TrailerKey}: {path} - <reason>\"");
        }

        Console.WriteLine($"    with {GrowthEnvironmentVariable}=\"<reason>\" set for the pre-commit hook.");
        return 1;
    }

    private static bool TryAdmitStagedGrowth(
        IReadOnlyList<Growth> stagedGrowth,
        string? stagedReason,
        out Dictionary<string, string> reasonByPath)
    {
        reasonByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(stagedReason))
        {
            return false;
        }

        var lines = stagedReason
            .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parsedScoped = new Dictionary<string, string>(StringComparer.Ordinal);
        bool anyScoped = false;
        foreach (string line in lines)
        {
            if (TryParseTrailer(line, out string path, out string reason))
            {
                anyScoped = true;
                parsedScoped[path] = reason;
            }
        }

        if (anyScoped)
        {
            foreach (var growth in stagedGrowth)
            {
                string path = growth.Path;
                string gateKey = GetGateKey(path);
                if (parsedScoped.TryGetValue(path, out string? reason) ||
                    parsedScoped.TryGetValue(gateKey, out reason))
                {
                    reasonByPath[path] = reason;
                    continue;
                }

                if (growth.GrownShards.Count > 1 &&
                    growth.GrownShards.All(parsedScoped.ContainsKey))
                {
                    reasonByPath[path] = string.Join("; ", growth.GrownShards.Select(s => parsedScoped[s]).Distinct(StringComparer.Ordinal));
                    continue;
                }

                return false;
            }

            return true;
        }

        string fallbackReason = stagedReason.Trim();
        foreach (string path in stagedGrowth.Select(growth => growth.Path))
        {
            reasonByPath[path] = fallbackReason;
        }

        return true;
    }

    private static string[] ResolveActiveParents(string repoRoot, string? headCommit)
    {
        string? mergeHead = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "MERGE_HEAD");
        if (string.IsNullOrEmpty(headCommit))
        {
            return string.IsNullOrEmpty(mergeHead) ? [] : [mergeHead];
        }

        if (string.IsNullOrEmpty(mergeHead) || string.Equals(headCommit, mergeHead, StringComparison.Ordinal))
        {
            return [headCommit];
        }

        return [headCommit, mergeHead];
    }

    private static List<Growth> FindStagedGrowthAgainstHead(string repoRoot)
    {
        string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");
        string[] parents = ResolveActiveParents(repoRoot, headCommit);
        var paths = CollectRatchetPathsFromCommits(repoRoot, parents);

        foreach (string path in GitRevisionScopeService.GetStagedFiles(repoRoot, includeDeletions: true))
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        foreach (string path in GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-files", "-z") ?? [])
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        return ComputeAggregatedGrowth(
            paths,
            gatePaths => MaxGateEntriesAcrossParents(repoRoot, parents, gatePaths),
            path => MaxGateEntriesAcrossParents(repoRoot, parents, [path]),
            path => GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", path));
    }

    private static List<Growth> FindDirtyWorkingTreeGrowth(string repoRoot, string? headCommit)
    {
        string[] parents = ResolveActiveParents(repoRoot, headCommit);
        var paths = CollectRatchetPathsFromCommits(repoRoot, parents);

        foreach (string path in GitRevisionScopeService.TryGetTrackedFiles(repoRoot) ?? [])
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        foreach (string path in GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: true, includeUntracked: true))
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        var workingGrowth = ComputeAggregatedGrowth(
            paths,
            gatePaths => MaxGateEntriesAcrossParents(repoRoot, parents, gatePaths),
            path => MaxGateEntriesAcrossParents(repoRoot, parents, [path]),
            path => ReadWorkingTree(repoRoot, path));
        var stagedGrowth = ComputeAggregatedGrowth(
            paths,
            gatePaths => MaxGateEntriesAcrossParents(repoRoot, parents, gatePaths),
            path => MaxGateEntriesAcrossParents(repoRoot, parents, [path]),
            path => GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", path));

        var allGrowthPaths = workingGrowth.Select(g => g.Path)
            .Concat(stagedGrowth.Select(g => g.Path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var workDict = workingGrowth.ToDictionary(g => g.Path, g => g.Added, StringComparer.Ordinal);
        var stagedDict = stagedGrowth.ToDictionary(g => g.Path, g => g.Added, StringComparer.Ordinal);

        var merged = new List<Growth>();
        foreach (string path in allGrowthPaths)
        {
            var combined = CombinePerEntryMax(
                workDict.GetValueOrDefault(path) ?? [],
                stagedDict.GetValueOrDefault(path) ?? []);
            if (combined.Count > 0)
            {
                merged.Add(new Growth(path, combined));
            }
        }

        return merged;
    }

    private static SortedSet<string> CollectRatchetPathsFromCommits(string repoRoot, IReadOnlyList<string> commits)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string commit in commits)
        {
            foreach (string path in GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-tree", "-r", "--name-only", "-z", commit) ?? [])
            {
                string normalized = NormalizeBaselinePath(path);
                if (IsRatchetBaseline(normalized))
                {
                    paths.Add(normalized);
                }
            }
        }

        return paths;
    }

    private static (List<CommitGrowthViolation> Violations, List<(string CommitSha, string Path, string Reason)> Admitted) EvaluateCommitRange(
        string repoRoot,
        string compareBase)
    {
        var violations = new List<CommitGrowthViolation>();
        var admitted = new List<(string CommitSha, string Path, string Reason)>();

        foreach (string commit in GitRevisionScopeService.GetCommitRange(repoRoot, compareBase, "HEAD"))
        {
            var commitGrowth = FindCommitGrowth(repoRoot, commit);
            if (commitGrowth.Count == 0)
            {
                continue;
            }

            var trailers = ParseCommitTrailers(repoRoot, commit);
            foreach (var fileGrowth in commitGrowth)
            {
                if (TryResolveAdmittingReasons(trailers, fileGrowth, out var matchedAdmissions))
                {
                    foreach (var (matchedKey, reason) in matchedAdmissions)
                    {
                        admitted.Add((commit, matchedKey, reason));
                    }
                }
                else
                {
                    violations.Add(new CommitGrowthViolation(commit, fileGrowth));
                }
            }
        }

        return (violations, admitted);
    }

    private static bool TryResolveAdmittingReasons(
        Dictionary<string, List<string>> trailers,
        Growth fileGrowth,
        out List<(string Path, string Reason)> matchedAdmissions)
    {
        if (trailers.TryGetValue(fileGrowth.Path, out var exactReasons) && exactReasons.Count > 0)
        {
            matchedAdmissions = [.. exactReasons.Select(r => (fileGrowth.Path, r))];
            return true;
        }

        string gateKey = GetGateKey(fileGrowth.Path);
        if (trailers.TryGetValue(gateKey, out var gateReasons) && gateReasons.Count > 0)
        {
            matchedAdmissions = [.. gateReasons.Select(r => (gateKey, r))];
            return true;
        }

        if (fileGrowth.GrownShards.Count > 1 &&
            fileGrowth.GrownShards.All(shard => trailers.TryGetValue(shard, out var shardReasons) && shardReasons.Count > 0))
        {
            matchedAdmissions = [.. fileGrowth.GrownShards.SelectMany(shard => trailers[shard].Select(r => (shard, r)))];
            return true;
        }

        matchedAdmissions = [];
        return false;
    }

    private static List<Growth> FindCommitGrowth(string repoRoot, string commit)
    {
        string? revLine = GitRevisionScopeService.RunGitSingleLine(repoRoot, "rev-list", "--parents", "-n", "1", commit);
        if (string.IsNullOrEmpty(revLine))
        {
            return [];
        }

        string[] parts = revLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] parents = parts.Length > 1 ? parts[1..] : [];

        IEnumerable<string> changedCandidates = parents.Length == 0
            ? GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-tree", "-r", "--name-only", "-z", commit) ?? []
            : GitRevisionScopeService.GetChangedFilesBetweenCommits(repoRoot, parents[0], commit, includeDeletions: true);

        var touchedGates = changedCandidates
            .Select(NormalizeBaselinePath)
            .Where(IsRatchetBaseline)
            .Select(GetGateKey)
            .ToHashSet(StringComparer.Ordinal);
        if (touchedGates.Count == 0)
        {
            return [];
        }

        var allGatePaths = CollectRatchetPathsFromCommits(repoRoot, [.. parents, commit])
            .Where(p => touchedGates.Contains(GetGateKey(p)))
            .ToList();

        return ComputeAggregatedGrowth(
            allGatePaths,
            gatePaths => MaxGateEntriesAcrossParents(repoRoot, parents, gatePaths),
            path => MaxGateEntriesAcrossParents(repoRoot, parents, [path]),
            path => GitRevisionScopeService.ReadFileAtRevision(repoRoot, commit, path));
    }

    private static List<Growth> ComputeAggregatedGrowth(
        IEnumerable<string> candidatePaths,
        Func<IReadOnlyList<string>, List<string>> readBeforeGateEntries,
        Func<string, List<string>> readBeforeFileEntries,
        Func<string, string?> readAfterFileContent)
    {
        var growth = new List<Growth>();
        var groupedByGate = candidatePaths
            .Distinct(StringComparer.Ordinal)
            .GroupBy(GetGateKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var gateGroup in groupedByGate)
        {
            string gateKey = gateGroup.Key;
            var gatePaths = gateGroup.Order(StringComparer.Ordinal).ToList();
            var beforeGateEntries = readBeforeGateEntries(gatePaths);

            var afterEntriesByPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var afterGateEntries = new List<string>();
            foreach (string path in gatePaths)
            {
                var fileEntries = Entries(path, readAfterFileContent(path));
                afterEntriesByPath[path] = fileEntries;
                afterGateEntries.AddRange(fileEntries);
            }

            var gateAdded = Additions(beforeGateEntries, afterGateEntries);
            if (gateAdded.Count == 0)
            {
                continue;
            }

            if (!IsKnownGateKey(gateKey))
            {
                growth.Add(new Growth(gatePaths[0], gateAdded) { GrownShards = [gatePaths[0]] });
                continue;
            }

            var gateAddedSet = gateAdded.ToHashSet(StringComparer.Ordinal);
            var grownShards = gatePaths
                .Where(path => Additions(readBeforeFileEntries(path), afterEntriesByPath[path]).Any(gateAddedSet.Contains))
                .ToList();
            string reportedPath = grownShards.Count == 1
                ? grownShards[0]
                : gateKey;
            growth.Add(new Growth(reportedPath, gateAdded) { GrownShards = grownShards });
        }

        return growth;
    }

    private static List<string> MaxGateEntriesAcrossParents(
        string repoRoot,
        string[] parents,
        IReadOnlyList<string> gatePaths)
    {
        if (parents.Length == 0 || gatePaths.Count == 0)
        {
            return [];
        }

        if (parents.Length == 1)
        {
            var singleParentEntries = new List<string>();
            foreach (string path in gatePaths)
            {
                singleParentEntries.AddRange(Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, parents[0], path)));
            }

            return singleParentEntries;
        }

        var maxCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string parent in parents)
        {
            var parentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string path in gatePaths)
            {
                foreach (string entry in Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, parent, path)))
                {
                    parentCounts[entry] = parentCounts.GetValueOrDefault(entry) + 1;
                }
            }

            foreach (var (entry, count) in parentCounts)
            {
                if (count > maxCounts.GetValueOrDefault(entry))
                {
                    maxCounts[entry] = count;
                }
            }
        }

        var combined = new List<string>();
        foreach (var (entry, count) in maxCounts)
        {
            for (int i = 0; i < count; i++)
            {
                combined.Add(entry);
            }
        }

        return combined;
    }

    private static List<string> CombinePerEntryMax(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (second.Count == 0)
        {
            return [.. first];
        }

        if (first.Count == 0)
        {
            return [.. second];
        }

        var maxCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in first.GroupBy(x => x, StringComparer.Ordinal))
        {
            maxCounts[group.Key] = group.Count();
        }

        foreach (var group in second.GroupBy(x => x, StringComparer.Ordinal))
        {
            int count = group.Count();
            if (count > maxCounts.GetValueOrDefault(group.Key))
            {
                maxCounts[group.Key] = count;
            }
        }

        return [.. maxCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))];
    }

    private static Dictionary<string, List<string>> ParseCommitTrailers(string repoRoot, string commit)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var rawLines = GitRevisionScopeService.RunGitLines(
            repoRoot,
            "log",
            "-1",
            $"--format=%(trailers:key={TrailerKey},valueonly)",
            commit) ?? [];

        foreach (string line in rawLines)
        {
            if (!TryParseTrailer(line, out string path, out string reason))
            {
                continue;
            }

            if (!result.TryGetValue(path, out var reasons))
            {
                reasons = [];
                result[path] = reasons;
            }

            reasons.Add(reason);
        }

        return result;
    }

    private static void PrintDirtyGrowthFailure(IReadOnlyList<Growth> dirtyGrowth)
    {
        Console.WriteLine($"{Tag} Dirty working-tree or uncommitted baseline growth is not allowed; baselines may only shrink:");
        PrintGrowthList(dirtyGrowth);
        Console.WriteLine("  Revert the uncommitted baseline entries or commit intentional debt with a per-file trailer:");
        foreach (string path in dirtyGrowth.Select(file => file.Path))
        {
            Console.WriteLine($"    git commit --trailer \"{TrailerKey}: {path} - <reason>\"");
        }
    }

    private static void PrintCommitViolations(IReadOnlyList<CommitGrowthViolation> violations, string scopeLabel)
    {
        Console.WriteLine($"{Tag} Commits since {scopeLabel} grew ratchet baselines without a matching '{TrailerKey}: <path> - <reason>' trailer:");
        foreach (var violation in violations)
        {
            string shortSha = violation.CommitSha[..Math.Min(12, violation.CommitSha.Length)];
            var file = violation.Growth;
            Console.WriteLine($"  {shortSha} -> {file.Path}: {file.Added.Count} new entr{(file.Added.Count == 1 ? "y" : "ies")}");
            foreach (string entry in file.Added.Take(MaxListed))
            {
                Console.WriteLine($"    + {entry}");
            }

            if (file.Added.Count > MaxListed)
            {
                Console.WriteLine($"    ... and {file.Added.Count - MaxListed} more");
            }
        }

        Console.WriteLine("  Fix the violations instead of recording them. If debt must be accepted, each commit that grows a baseline");
        Console.WriteLine($"  must carry '{TrailerKey}: <path> - <reason>' for every baseline file or gate it grows.");
    }

    private static void PrintGrowthList(IReadOnlyList<Growth> growth)
    {
        foreach (var file in growth)
        {
            Console.WriteLine($"  {file.Path}: {file.Added.Count} new entr{(file.Added.Count == 1 ? "y" : "ies")}");
            foreach (string entry in file.Added.Take(MaxListed))
            {
                Console.WriteLine($"    + {entry}");
            }

            if (file.Added.Count > MaxListed)
            {
                Console.WriteLine($"    ... and {file.Added.Count - MaxListed} more");
            }
        }
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

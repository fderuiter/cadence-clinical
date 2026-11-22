using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>
/// The <c>baseline-ratchet</c> gate (#5318): a ratchet baseline may only shrink. Every tracked ratchet baseline (the
/// kernel-boundary baseline, <c>.deid-baseline.txt</c>, <c>.secrets.baseline</c> and any other <c>*.baseline.txt</c> or
/// <c>*-baseline.txt</c> file) is checked across every commit in <c>compareBase..HEAD</c> and in the working tree.
/// Any commit that grows a baseline must carry a <c>Baseline-Growth: &lt;path&gt; - &lt;reason&gt;</c> trailer for each
/// baseline file it grew, and dirty working-tree baseline growth is always rejected. In <c>--pre-commit</c>, staged
/// baseline growth is compared against <c>HEAD</c> and admitted only when <c>CADENCE_BASELINE_GROWTH</c> is set.
/// </summary>
public static class BaselineRatchetService
{
    /// <summary>The ref a branch is compared with, as in <c>cadence ci</c>.</summary>
    public const string DefaultBaseRef = GitRevisionScopeService.DefaultBaseRef;

    /// <summary>The commit trailer that admits baseline growth on a commit.</summary>
    public const string TrailerKey = "Baseline-Growth";

    /// <summary>Admits staged baseline growth in the pre-commit hook, before the commit (and its trailer) exists.</summary>
    public const string GrowthEnvironmentVariable = "CADENCE_BASELINE_GROWTH";

    private const string Tag = "[BASELINE RATCHET]";
    private const string SecretsBaseline = ".secrets.baseline";
    private const int MaxListed = 20;

    /// <summary>One baseline file that gained entries.</summary>
    /// <param name="Path">The file, relative to the repository root.</param>
    /// <param name="Added">The entries it gained, once per added occurrence.</param>
    public sealed record Growth(string Path, IReadOnlyList<string> Added);

    /// <summary>One commit's unadmitted or admitted baseline growth.</summary>
    public sealed record CommitGrowthViolation(string CommitSha, Growth Growth);

    /// <summary>Whether <paramref name="path"/> names a ratchet baseline the gate checks.</summary>
    public static bool IsRatchetBaseline(string path)
    {
        string name = Path.GetFileName(NormalizeBaselinePath(path));
        return name.Equals(SecretsBaseline, StringComparison.Ordinal)
            || name.EndsWith(".baseline.txt", StringComparison.Ordinal)
            || name.EndsWith("-baseline.txt", StringComparison.Ordinal);
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
        if (candidatePath.Length == 0 || candidateReason.Length == 0 || !IsRatchetBaseline(candidatePath))
        {
            return false;
        }

        baselinePath = candidatePath;
        reason = candidateReason;
        return true;
    }

    /// <summary>
    /// The entries of a baseline: one per non-blank line that is not a <c>#</c> comment, or, for
    /// <c>.secrets.baseline</c>, one <c>filename|type|hashed_secret</c> per allowlisted secret.
    /// </summary>
    /// <exception cref="JsonException"><c>.secrets.baseline</c> is not valid JSON.</exception>
    public static IReadOnlyList<string> Entries(string path, string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        if (Path.GetFileName(path).Equals(SecretsBaseline, StringComparison.Ordinal))
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
    /// <c>compareBase..HEAD</c> that grows a baseline file to carry a <c>Baseline-Growth: &lt;path&gt; - &lt;reason&gt;</c>
    /// trailer for that file. Without the base ref the check is skipped locally and fails in CI (<c>CI</c> or
    /// <c>GITHUB_ACTIONS</c> set).
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
                    compareBase = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef) ?? headCommit;
                    scopeLabel = effectiveBaseRef;
                }
            }
            else
            {
                compareBase = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
                scopeLabel = effectiveBaseRef;
            }

            if (string.IsNullOrEmpty(compareBase))
            {
                if (IsCi(environment))
                {
                    Console.WriteLine($"{Tag} {effectiveBaseRef} not found or shares no history with HEAD, so the baselines cannot be compared.");
                    Console.WriteLine("  Fetch it with full history (actions/checkout 'fetch-depth: 0', or 'git fetch origin main').");
                    return 1;
                }

                Console.WriteLine($"{Tag} Skipped: {effectiveBaseRef} not found or shares no history with HEAD. Run 'git fetch origin main' to check the baselines.");
                return 0;
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
            Console.WriteLine($"{Tag} {SecretsBaseline} is not valid JSON: {ex.Message}");
            return 1;
        }
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
            foreach (string path in stagedGrowth.Select(growth => growth.Path))
            {
                if (!parsedScoped.TryGetValue(path, out string? reason))
                {
                    return false;
                }

                reasonByPath[path] = reason;
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

    private static List<Growth> FindStagedGrowthAgainstHead(string repoRoot)
    {
        string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(headCommit))
        {
            foreach (string path in GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-tree", "-r", "--name-only", "-z", headCommit) ?? [])
            {
                string normalized = NormalizeBaselinePath(path);
                if (IsRatchetBaseline(normalized))
                {
                    paths.Add(normalized);
                }
            }
        }

        foreach (string path in GitRevisionScopeService.GetStagedFiles(repoRoot, includeDeletions: false))
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        var growth = new List<Growth>();
        foreach (string path in paths)
        {
            string? before = string.IsNullOrEmpty(headCommit)
                ? null
                : GitRevisionScopeService.ReadFileAtRevision(repoRoot, headCommit, path);
            string? after = GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", path);
            var added = Additions(Entries(path, before), Entries(path, after));
            if (added.Count > 0)
            {
                growth.Add(new Growth(path, added));
            }
        }

        return growth;
    }

    private static List<Growth> FindDirtyWorkingTreeGrowth(string repoRoot, string? headCommit)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(headCommit))
        {
            foreach (string path in GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-tree", "-r", "--name-only", "-z", headCommit) ?? [])
            {
                string normalized = NormalizeBaselinePath(path);
                if (IsRatchetBaseline(normalized))
                {
                    paths.Add(normalized);
                }
            }
        }

        foreach (string path in GitRevisionScopeService.TryGetTrackedFiles(repoRoot) ?? [])
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        foreach (string path in GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: false, includeUntracked: true))
        {
            string normalized = NormalizeBaselinePath(path);
            if (IsRatchetBaseline(normalized))
            {
                paths.Add(normalized);
            }
        }

        var growth = new List<Growth>();
        foreach (string path in paths)
        {
            var headEntries = string.IsNullOrEmpty(headCommit)
                ? []
                : Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, headCommit, path));

            var workingAdded = Additions(headEntries, Entries(path, ReadWorkingTree(repoRoot, path)));
            var stagedAdded = Additions(headEntries, Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", path)));

            var combined = CombinePerEntryMax(workingAdded, stagedAdded);
            if (combined.Count > 0)
            {
                growth.Add(new Growth(path, combined));
            }
        }

        return growth;
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
                if (trailers.TryGetValue(fileGrowth.Path, out var reasons) && reasons.Count > 0)
                {
                    foreach (string reason in reasons)
                    {
                        admitted.Add((commit, fileGrowth.Path, reason));
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

    private static List<Growth> FindCommitGrowth(string repoRoot, string commit)
    {
        string? revLine = GitRevisionScopeService.RunGitSingleLine(repoRoot, "rev-list", "--parents", "-n", "1", commit);
        if (string.IsNullOrEmpty(revLine))
        {
            return [];
        }

        string[] parts = revLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] parents = parts.Length > 1 ? parts[1..] : [];

        IEnumerable<string> rawCandidates;
        if (parents.Length == 0)
        {
            rawCandidates = GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-tree", "-r", "--name-only", "-z", commit) ?? [];
        }
        else
        {
            rawCandidates = GitRevisionScopeService.GetChangedFilesBetweenCommits(repoRoot, parents[0], commit, includeDeletions: false);
        }

        var baselinePaths = rawCandidates
            .Select(NormalizeBaselinePath)
            .Where(IsRatchetBaseline)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var growth = new List<Growth>();
        foreach (string path in baselinePaths)
        {
            var afterEntries = Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, commit, path));
            var beforeEntries = MaxEntriesAcrossParents(repoRoot, parents, path);
            var added = Additions(beforeEntries, afterEntries);
            if (added.Count > 0)
            {
                growth.Add(new Growth(path, added));
            }
        }

        return growth;
    }

    private static List<string> MaxEntriesAcrossParents(string repoRoot, string[] parents, string path)
    {
        if (parents.Length == 0)
        {
            return [];
        }

        if (parents.Length == 1)
        {
            return [.. Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, parents[0], path))];
        }

        var maxCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string parent in parents)
        {
            var parentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string entry in Entries(path, GitRevisionScopeService.ReadFileAtRevision(repoRoot, parent, path)))
            {
                parentCounts[entry] = parentCounts.GetValueOrDefault(entry) + 1;
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
        Console.WriteLine($"  must carry '{TrailerKey}: <path> - <reason>' for every baseline file it grows.");
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

    private static bool IsCi(Func<string, string?> environment) =>
        IsTrue(environment("CI")) || IsTrue(environment("GITHUB_ACTIONS"));

    private static bool IsTrue(string? value) =>
        value is not null && (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");

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

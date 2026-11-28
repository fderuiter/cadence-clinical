using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

public static partial class BaselineRatchetService
{
    private static (List<CommitGrowthViolation> Violations, List<(string CommitSha, string Path, int Count, string Reason)> Admitted) EvaluateCommitRange(
        string repoRoot,
        string compareBase)
    {
        var rawViolations = new List<CommitGrowthViolation>();
        var admitted = new List<(string CommitSha, string Path, int Count, string Reason)>();

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
                    foreach (var (matchedKey, count, reason) in matchedAdmissions)
                    {
                        admitted.Add((commit, matchedKey, count, reason));
                    }
                }
                else
                {
                    rawViolations.Add(new CommitGrowthViolation(commit, fileGrowth));
                }
            }
        }

        if (rawViolations.Count == 0)
        {
            return (rawViolations, admitted);
        }

        return (FilterUnremediatedViolations(repoRoot, compareBase, rawViolations), admitted);
    }

    private static List<CommitGrowthViolation> FilterUnremediatedViolations(
        string repoRoot,
        string compareBase,
        List<CommitGrowthViolation> rawViolations)
    {
        var allCandidatePaths = CollectRatchetPathsFromCommits(repoRoot, [compareBase, "HEAD"]);
        var netAddedByGate = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string gateKey in rawViolations.Select(v => GetGateKey(v.Growth.Path)).Distinct(StringComparer.Ordinal))
        {
            var gatePaths = allCandidatePaths
                .Where(p => string.Equals(GetGateKey(p), gateKey, StringComparison.Ordinal))
                .ToList();
            var baseEntries = MaxGateEntriesAcrossParents(repoRoot, [compareBase], gatePaths);
            var headEntries = MaxGateEntriesAcrossParents(repoRoot, ["HEAD"], gatePaths);
            netAddedByGate[gateKey] = Additions(baseEntries, headEntries).ToHashSet(StringComparer.Ordinal);
        }

        var unremediated = new List<CommitGrowthViolation>();
        foreach (var violation in rawViolations)
        {
            var netHeadAdded = netAddedByGate[GetGateKey(violation.Growth.Path)];
            var survivingAdded = violation.Growth.Added.Where(netHeadAdded.Contains).ToList();
            if (survivingAdded.Count > 0)
            {
                unremediated.Add(new CommitGrowthViolation(
                    violation.CommitSha,
                    violation.Growth with { Added = survivingAdded }));
            }
        }

        return unremediated;
    }

    private static bool TryResolveAdmittingReasons(
        Dictionary<string, List<(int Count, string Reason)>> trailers,
        Growth fileGrowth,
        out List<(string Path, int Count, string Reason)> matchedAdmissions)
    {
        int expectedCount = fileGrowth.Added.Count;
        if (trailers.TryGetValue(fileGrowth.Path, out var exactEntries) &&
            exactEntries.Count > 0 &&
            exactEntries.Sum(e => e.Count) == expectedCount)
        {
            matchedAdmissions = [.. exactEntries.Select(e => (fileGrowth.Path, e.Count, e.Reason))];
            return true;
        }

        string gateKey = GetGateKey(fileGrowth.Path);
        if (!string.Equals(gateKey, fileGrowth.Path, StringComparison.Ordinal) &&
            trailers.TryGetValue(gateKey, out var gateEntries) &&
            gateEntries.Count > 0 &&
            gateEntries.Sum(e => e.Count) == expectedCount)
        {
            matchedAdmissions = [.. gateEntries.Select(e => (gateKey, e.Count, e.Reason))];
            return true;
        }

        if (fileGrowth.GrownShards.Count > 1 &&
            fileGrowth.GrownShards.All(shard => trailers.TryGetValue(shard, out var shardEntries) && shardEntries.Count > 0) &&
            fileGrowth.GrownShards.Sum(shard => trailers[shard].Sum(e => e.Count)) == expectedCount &&
            (fileGrowth.GrownShardCounts.Values.Sum() != expectedCount ||
             fileGrowth.GrownShards.All(shard => trailers[shard].Sum(e => e.Count) == fileGrowth.GrownShardCounts.GetValueOrDefault(shard))))
        {
            matchedAdmissions = [.. fileGrowth.GrownShards.SelectMany(shard => trailers[shard].Select(e => (shard, e.Count, e.Reason)))];
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

    private static Dictionary<string, List<(int Count, string Reason)>> ParseCommitTrailers(string repoRoot, string commit)
    {
        var result = new Dictionary<string, List<(int Count, string Reason)>>(StringComparer.Ordinal);
        var rawLines = GitRevisionScopeService.RunGitLines(
            repoRoot,
            "log",
            "-1",
            $"--format=%(trailers:key={TrailerKey},valueonly)",
            commit) ?? [];

        foreach (string line in rawLines)
        {
            if (!TryParseTrailer(line, out string path, out int count, out string reason))
            {
                continue;
            }

            if (!result.TryGetValue(path, out var entries))
            {
                entries = [];
                result[path] = entries;
            }

            entries.Add((count, reason));
        }

        return result;
    }

    private static void PrintCommitViolations(IReadOnlyList<CommitGrowthViolation> violations, string scopeLabel)
    {
        Console.WriteLine($"{Tag} Commits since {scopeLabel} grew ratchet baselines without a matching '{TrailerKey}: <path> +<count> - <reason>' trailer:");
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
        Console.WriteLine($"  must carry '{TrailerKey}: <path> +<count> - <reason>' matching the exact added entry count for every baseline file or gate it grows.");
    }
}

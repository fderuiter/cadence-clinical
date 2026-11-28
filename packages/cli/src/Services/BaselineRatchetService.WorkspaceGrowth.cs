using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

public static partial class BaselineRatchetService
{
    private static int EvaluatePreCommit(IReadOnlyList<Growth> stagedGrowth, string? stagedApproval)
    {
        if (stagedGrowth.Count == 0)
        {
            Console.WriteLine($"{Tag} No staged ratchet baseline grew against HEAD.");
            return 0;
        }

        string summary = string.Join(", ", stagedGrowth.Select(g => $"{g.Path} +{g.Added.Count}"));
        if (TryAdmitStagedGrowth(stagedGrowth, stagedApproval, out var matchedAdmissions))
        {
            Console.WriteLine($"{Tag} {GrowthEnvironmentVariable} admits the staged baseline growth ({summary}).");
            Console.WriteLine("  Include these trailers in this commit's message ('cadence check' and CI require them per file and count):");
            foreach (var (path, count, reason) in matchedAdmissions)
            {
                Console.WriteLine($"    {TrailerKey}: {path} +{count} - {reason}");
            }

            return 0;
        }

        Console.WriteLine($"{Tag} These staged ratchet baselines grew relative to HEAD, but a baseline may only shrink:");
        PrintGrowthList(stagedGrowth);
        Console.WriteLine("  Fix the new violations instead of recording them. If the debt must be accepted:");
        foreach (var file in stagedGrowth)
        {
            Console.WriteLine($"    git commit --trailer \"{TrailerKey}: {file.Path} +{file.Added.Count} - <reason>\"");
        }

        string exampleEnv = string.Join("; ", stagedGrowth.Select(f => $"{f.Path} +{f.Added.Count} - <reason>"));
        Console.WriteLine($"    with {GrowthEnvironmentVariable}=\"{exampleEnv}\" set for the pre-commit hook.");
        return 1;
    }

    private static bool TryAdmitStagedGrowth(
        IReadOnlyList<Growth> stagedGrowth,
        string? stagedApproval,
        out List<(string Path, int Count, string Reason)> matchedAdmissions)
    {
        matchedAdmissions = [];
        if (string.IsNullOrWhiteSpace(stagedApproval))
        {
            return false;
        }

        var lines = stagedApproval
            .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var parsedByPath = new Dictionary<string, List<(int Count, string Reason)>>(StringComparer.Ordinal);
        string trailerPrefix = $"{TrailerKey}:";
        foreach (string rawLine in lines)
        {
            string line = rawLine.StartsWith(trailerPrefix, StringComparison.OrdinalIgnoreCase)
                ? rawLine[trailerPrefix.Length..].Trim()
                : rawLine;
            if (!TryParseTrailer(line, out string path, out int count, out string reason))
            {
                return false;
            }

            if (!parsedByPath.TryGetValue(path, out var list))
            {
                list = [];
                parsedByPath[path] = list;
            }

            list.Add((count, reason));
        }

        foreach (var growth in stagedGrowth)
        {
            if (!TryResolveAdmittingReasons(parsedByPath, growth, out var growthAdmissions))
            {
                matchedAdmissions.Clear();
                return false;
            }

            matchedAdmissions.AddRange(growthAdmissions);
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
                growth.Add(new Growth(gatePaths[0], gateAdded)
                {
                    GrownShards = [gatePaths[0]],
                    GrownShardCounts = new Dictionary<string, int>(StringComparer.Ordinal) { [gatePaths[0]] = gateAdded.Count },
                });
                continue;
            }

            var gateAddedSet = gateAdded.ToHashSet(StringComparer.Ordinal);
            var grownShardCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string path in gatePaths)
            {
                int shardNetAdded = Additions(readBeforeFileEntries(path), afterEntriesByPath[path]).Count(gateAddedSet.Contains);
                if (shardNetAdded > 0)
                {
                    grownShardCounts[path] = shardNetAdded;
                }
            }

            var grownShards = grownShardCounts.Keys.Order(StringComparer.Ordinal).ToList();
            string reportedPath = grownShards.Count == 1
                ? grownShards[0]
                : gateKey;
            growth.Add(new Growth(reportedPath, gateAdded)
            {
                GrownShards = grownShards,
                GrownShardCounts = grownShardCounts,
            });
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

    private static void PrintDirtyGrowthFailure(IReadOnlyList<Growth> dirtyGrowth)
    {
        Console.WriteLine($"{Tag} Dirty working-tree or uncommitted baseline growth is not allowed; baselines may only shrink:");
        PrintGrowthList(dirtyGrowth);
        Console.WriteLine("  Revert the uncommitted baseline entries or commit intentional debt with a file-and-count-scoped trailer:");
        foreach (var file in dirtyGrowth)
        {
            Console.WriteLine($"    git commit --trailer \"{TrailerKey}: {file.Path} +{file.Added.Count} - <reason>\"");
        }
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
}

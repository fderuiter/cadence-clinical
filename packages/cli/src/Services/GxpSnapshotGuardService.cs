using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Compliance;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>
/// The <c>gxp-snapshot</c> gate (ADR-2532, #5151): a feature branch or unauthorized push must not change the generated
/// RTM, the IQ/OQ/PQ report or the archived run reports. Only <c>gxp snapshot</c>, run with
/// <c>CADENCE_GXP_SNAPSHOT=1</c> by the integration owner, may.
/// </summary>
public static class GxpSnapshotGuardService
{
    /// <summary>The ref a branch is compared with, as in <c>cadence ci</c>.</summary>
    public const string DefaultBaseRef = GitRevisionScopeService.DefaultBaseRef;

    /// <summary>
    /// Checks staged files (<paramref name="preCommit"/>), uncommitted working-tree drift on protected
    /// <c>workflow_dispatch</c> and <c>refs/tags/v*</c> target refs, a pushed commit range (<paramref name="pushBefore"/>
    /// or <c>merge-base(origin/main, HEAD)..HEAD</c> on newly created <c>refs/heads/release/*</c> branches), or everything
    /// the branch changed since <paramref name="baseRef"/> (filtering out verbatim <paramref name="baseRef"/> snapshots
    /// merged into a feature branch). Returns 0 when no snapshot path changed or snapshot mode is on, and 1 otherwise.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit,
        string? baseRef = DefaultBaseRef,
        bool? snapshotMode = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null)
    {
        bool snapshot = snapshotMode ?? GxpSnapshotPolicy.IsSnapshotMode();
        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(baseRef);

        // Taking a snapshot: CI compares the documents it regenerated with the ones staged for the snapshot, ignoring
        // the evidence block, and puts the staged copy back when only that block moved.
        if (snapshot && !preCommit)
        {
            Console.WriteLine($"[GXP SNAPSHOT] {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: comparing the regenerated documents with the snapshot.");
            return GxpDocsDiffService.Compare(repoRoot, restoreWhenEquivalent: true);
        }

        if (preCommit)
        {
            IReadOnlyList<string> stagedChanged = GitRevisionScopeService.RunGitNullTerminated(repoRoot, "diff", "--cached", "--name-only", "-z") ?? [];
            var stagedDiffersFromBase = GitRevisionScopeService.TryGetPathsDifferingFromRevision(repoRoot, effectiveBaseRef, cached: true);
            if (stagedDiffersFromBase is not null)
            {
                var differing = new HashSet<string>(stagedDiffersFromBase, StringComparer.Ordinal);
                stagedChanged = [.. stagedChanged.Where(differing.Contains)];
            }

            return Evaluate(stagedChanged, snapshot, "staged");
        }

        var eventScope = GitRevisionScopeService.ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef);
        if (eventScope.Kind == GitEventScopeKind.UnprotectedTargetRef)
        {
            Console.WriteLine($"[GXP SNAPSHOT] [ERROR] Unsupported target ref '{eventScope.GitRef}' for event '{eventScope.EventName}'.");
            return 1;
        }

        if (eventScope.Kind == GitEventScopeKind.TargetRef)
        {
            if (string.IsNullOrEmpty(eventScope.HeadCommit))
            {
                Console.WriteLine("[GXP SNAPSHOT] [ERROR] Could not resolve HEAD commit.");
                return 1;
            }

            var uncommittedDrift = GitRevisionScopeService.GetUncommittedChangesAtHead(repoRoot, includeDeletions: true, includeUntracked: true);
            return Evaluate(uncommittedDrift, snapshot, "uncommitted at HEAD");
        }

        if (eventScope.Kind == GitEventScopeKind.PushCommitRange && !string.IsNullOrEmpty(eventScope.CompareBase))
        {
            var pushedChanged = GitRevisionScopeService.CollectChangedFilesSinceCommit(
                repoRoot,
                eventScope.CompareBase,
                includeDeletions: true,
                includeUntracked: false);
            string shortSha = eventScope.CompareBase[..Math.Min(12, eventScope.CompareBase.Length)];
            return Evaluate(pushedChanged, snapshot, $"changed since {shortSha}");
        }

        if (eventScope.Kind == GitEventScopeKind.NewBranchPush && GitRevisionScopeService.IsBranchHeadRef(eventScope.GitRef))
        {
            if (string.IsNullOrEmpty(eventScope.CompareBase))
            {
                Console.WriteLine(
                    $"[GXP SNAPSHOT] [ERROR] '{eventScope.EffectiveBaseRef}' (and fallback '{GitRevisionScopeService.FallbackLocalMainRef}') " +
                    $"not found or shares no history with HEAD on new branch push '{eventScope.GitRef}'.");
                return 1;
            }

            var newBranchChanged = GitRevisionScopeService.CollectChangedFilesSinceCommit(
                repoRoot,
                eventScope.CompareBase,
                includeDeletions: true,
                includeUntracked: true);
            string shortMergeBase = eventScope.CompareBase[..Math.Min(12, eventScope.CompareBase.Length)];
            return Evaluate(newBranchChanged, snapshot, $"changed since {shortMergeBase}");
        }

        string? mergeBase = eventScope.CompareBase ?? GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
        IReadOnlyList<string> changed = mergeBase is null
            ? GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: true, includeUntracked: false)
            : GitRevisionScopeService.CollectChangedFilesSinceCommit(repoRoot, mergeBase, includeDeletions: true, includeUntracked: false);
        if (mergeBase is null)
        {
            Console.WriteLine($"[GXP SNAPSHOT] {effectiveBaseRef} not found; checking uncommitted changes only.");
        }

        // A path whose content is exactly baseRef's is the baseline's snapshot coming in (for example when a feature branch
        // merges main after the integration owner took a snapshot): only a copy that differs from baseRef is a branch edit.
        var differsFromBase = GitRevisionScopeService.TryGetPathsDifferingFromRevision(repoRoot, effectiveBaseRef, cached: false);
        if (differsFromBase is not null)
        {
            var differing = new HashSet<string>(differsFromBase, StringComparer.Ordinal);
            changed = [.. changed.Where(differing.Contains)];
        }

        return Evaluate(changed, snapshot, $"changed since {effectiveBaseRef}");
    }

    /// <summary>
    /// Evaluates fail-closed CI provenance for <c>CADENCE_GXP_SNAPSHOT=1</c>: same-repository owner pull requests,
    /// <c>merge_group</c> runs, and reviewed <c>refs/heads/main</c> or <c>refs/heads/release/*</c> pushes carrying a
    /// canonical <see cref="GxpSnapshotPolicy.SnapshotTrailer"/> commit trailer in the evaluated commit range.
    /// </summary>
    public static bool AuthorizeCiSnapshot(
        string repoRoot,
        string? eventName,
        string? gitRef,
        string? baseRef = DefaultBaseRef,
        string? pushBefore = null,
        string? repositoryFullName = null,
        string? repositoryOwner = null,
        string? pullRequestHeadRepoFullName = null,
        string? pullRequestUserLogin = null)
    {
        if (!GitRevisionScopeService.HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        string trimmedEvent = eventName.Trim();
        string trimmedRef = (gitRef ?? string.Empty).Trim();
        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(baseRef);
        string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");
        if (string.IsNullOrEmpty(headCommit))
        {
            return false;
        }

        string? revisionRange = null;
        if (string.Equals(trimmedEvent, "pull_request", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(repositoryFullName) ||
                string.IsNullOrWhiteSpace(repositoryOwner) ||
                !string.Equals(pullRequestHeadRepoFullName?.Trim(), repositoryFullName.Trim(), StringComparison.Ordinal) ||
                !string.Equals(pullRequestUserLogin?.Trim(), repositoryOwner.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            if (GitRevisionScopeService.TryResolveCommitSha(repoRoot, effectiveBaseRef) is not null)
            {
                revisionRange = $"{effectiveBaseRef}..HEAD";
            }
        }
        else if (string.Equals(trimmedEvent, "merge_group", StringComparison.OrdinalIgnoreCase))
        {
            if (GitRevisionScopeService.TryResolveCommitSha(repoRoot, effectiveBaseRef) is not null)
            {
                revisionRange = $"{effectiveBaseRef}..HEAD";
            }
        }
        else if (string.Equals(trimmedEvent, "push", StringComparison.OrdinalIgnoreCase) &&
                 (GitRevisionScopeService.IsMainBranchRef(trimmedRef) || GitRevisionScopeService.IsReleaseBranchRef(trimmedRef)))
        {
            string? validPushBase = GitRevisionScopeService.TryResolveValidPushBase(repoRoot, pushBefore, headCommit);
            if (!string.IsNullOrEmpty(validPushBase))
            {
                revisionRange = $"{validPushBase}..HEAD";
            }
            else if (GitRevisionScopeService.IsReleaseBranchRef(trimmedRef) && GitRevisionScopeService.IsAllZeroSha(pushBefore))
            {
                string? mergeBase = GitRevisionScopeService.TryResolveCompareBaseWithLocalMainFallback(repoRoot, DefaultBaseRef, out _);
                if (!string.IsNullOrEmpty(mergeBase))
                {
                    revisionRange = $"{mergeBase}..HEAD";
                }
            }
        }

        return !string.IsNullOrEmpty(revisionRange) &&
               GitRevisionScopeService.HasCommitTrailer(
                   repoRoot,
                   revisionRange,
                   GxpSnapshotPolicy.SnapshotTrailerKey,
                   GxpSnapshotPolicy.SnapshotTrailerValue);
    }

    /// <summary>The decision, separated from git so it can be tested.</summary>
    public static int Evaluate(IEnumerable<string> changedPaths, bool snapshotMode, string scope)
    {
        var touched = GxpSnapshotPolicy.SnapshotPaths(changedPaths);
        if (touched.Count == 0)
        {
            Console.WriteLine("[GXP SNAPSHOT] No generated GxP snapshot is changed.");
            return 0;
        }

        if (snapshotMode)
        {
            Console.WriteLine($"[GXP SNAPSHOT] {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: admitting a new snapshot ({touched.Count} path(s)).");
            return 0;
        }

        Console.WriteLine($"[GXP SNAPSHOT] These generated documents are {scope}, but a feature branch never commits them (ADR-2532):");
        foreach (string path in touched.Take(20))
        {
            Console.WriteLine($"  {path}");
        }

        Console.WriteLine("  Restore them with `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.");
        Console.WriteLine("  Validate your tags and results with `pnpm cadence gxp sync`; the integration owner takes the next snapshot with");
        Console.WriteLine($"  `{GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1 pnpm cadence gxp snapshot`.");
        return 1;
    }
}

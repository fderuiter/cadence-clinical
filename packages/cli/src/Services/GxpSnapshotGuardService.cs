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
    /// Checks staged files (<paramref name="preCommit"/>), a pushed commit range (<paramref name="pushBefore"/> on
    /// <c>main</c> and <c>release/*</c> pushes), or everything the branch changed since <paramref name="baseRef"/>
    /// (filtering out verbatim <paramref name="baseRef"/> snapshots merged into a feature branch). Returns 0 when no
    /// snapshot path changed or snapshot mode is on, and 1 otherwise.
    /// </summary>
    public static int Check(
        string repoRoot,
        bool preCommit,
        string? baseRef = DefaultBaseRef,
        bool? snapshotMode = null,
        string? pushBefore = null)
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

        if (!string.IsNullOrWhiteSpace(pushBefore))
        {
            string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");
            string? validPushBase = GitRevisionScopeService.TryResolveValidPushBase(repoRoot, pushBefore, headCommit);
            if (validPushBase is not null)
            {
                var pushedChanged = GitRevisionScopeService.CollectChangedFilesSinceCommit(
                    repoRoot,
                    validPushBase,
                    includeDeletions: true,
                    includeUntracked: false);
                string shortSha = validPushBase[..Math.Min(12, validPushBase.Length)];
                return Evaluate(pushedChanged, snapshot, $"changed since {shortSha}");
            }
        }

        string? mergeBase = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
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

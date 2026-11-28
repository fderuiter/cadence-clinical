using System;
using System.Collections.Generic;
using System.IO;

namespace Cadence.ToolingCore.Services;

/// <summary>How <see cref="GitRevisionScopeService"/> resolved the candidate file or commit scope.</summary>
public enum GitScopeKind
{
    /// <summary>Caller supplied an explicit file list.</summary>
    ExplicitFiles,

    /// <summary>Pre-commit hook staged index changes (<c>--pre-commit</c>).</summary>
    Staged,

    /// <summary>Push commit range (<c>--push-before &lt;sha&gt;..HEAD</c>).</summary>
    PushRange,

    /// <summary>Branch diff against the merge base with <c>--base &lt;ref&gt;</c>.</summary>
    BranchDiff,

    /// <summary>Fallback to all Git-tracked files (<c>git ls-files -z</c>).</summary>
    TrackedFiles,

    /// <summary>Directory is not inside a Git repository.</summary>
    NonGitDirectory,
}

/// <summary>How <see cref="GitRevisionScopeService.ResolveEventScope"/> classifies the active local or CI Git event and ref.</summary>
public enum GitEventScopeKind
{
    /// <summary>Local branch check, <c>pull_request</c>, or <c>merge_group</c> evaluated against <c>origin/&lt;base&gt;</c>.</summary>
    BranchDiff,

    /// <summary><c>push</c> (or explicit <c>--push-before</c>) with a valid ancestor <c>BEFORE_SHA</c> (<c>&lt;before&gt;..HEAD</c>).</summary>
    PushCommitRange,

    /// <summary>40-zero (or non-ancestor) <c>BEFORE_SHA</c> push on <c>refs/heads/*</c>, evaluated across <c>merge-base(origin/main, HEAD)..HEAD</c>.</summary>
    NewBranchPush,

    /// <summary>Protected-ref <c>workflow_dispatch</c> (<c>refs/heads/main</c>, <c>refs/heads/release/*</c>, <c>refs/tags/v*</c>) or <c>refs/tags/v*</c> push targeting <c>HEAD</c> without comparing against <c>origin/main</c>.</summary>
    TargetRef,

    /// <summary><c>workflow_dispatch</c> or non-branch push on an unprotected ref; quality gates must fail closed.</summary>
    UnprotectedTargetRef,
}

/// <summary>Resolved CI event and target-ref context for revision-scoped quality gates.</summary>
/// <param name="Kind">Classification of the active local or CI revision scope.</param>
/// <param name="EventName">Normalized event name (such as <c>pull_request</c>, <c>merge_group</c>, <c>push</c>, <c>workflow_dispatch</c>, or empty locally).</param>
/// <param name="GitRef">Normalized Git ref (such as <c>refs/heads/main</c>, <c>refs/heads/release/1.0</c>, or <c>refs/tags/v1.0.1</c>).</param>
/// <param name="EffectiveBaseRef">Normalized comparison base ref label.</param>
/// <param name="CompareBase">Resolved comparison base commit SHA when applicable, or <c>null</c>.</param>
/// <param name="HeadCommit">Resolved <c>HEAD</c> commit SHA, or <c>null</c> in an unborn or non-Git directory.</param>
/// <param name="IsCiBranchEvent"><c>true</c> when <paramref name="EventName"/> is <c>pull_request</c> or <c>merge_group</c>.</param>
public sealed record GitEventScope(
    GitEventScopeKind Kind,
    string EventName,
    string GitRef,
    string EffectiveBaseRef,
    string? CompareBase,
    string? HeadCommit,
    bool IsCiBranchEvent = false);

/// <summary>
/// Resolved Git revision scope for quality gates across pre-commit hooks, local branch checks, pull requests, and pushes.
/// </summary>
/// <param name="Kind">Which scope mode produced <paramref name="Files"/>.</param>
/// <param name="CompareBase">
/// Resolved base commit SHA when comparing against a branch base ref or ancestor push SHA; <c>null</c> when no valid
/// ancestor commit exists.
/// </param>
/// <param name="HeadCommit">Resolved <c>HEAD</c> commit SHA, or <c>null</c> in an unborn or non-Git directory.</param>
/// <param name="EffectiveBaseRef">Normalized base ref name (defaults to <c>origin/main</c>).</param>
/// <param name="Files">Normalized repository-relative paths in the resolved scope.</param>
/// <param name="UsedTrackedFallback">
/// <c>true</c> when a branch or push scope fell back to <c>git ls-files -z</c> (for example when <c>HEAD</c> equals
/// <c>baseRef</c>, <c>baseRef</c> is unavailable, or <c>pushBefore</c> is all zeros, a tag, or a non-ancestor).
/// </param>
public sealed record GitRevisionScope(
    GitScopeKind Kind,
    string? CompareBase,
    string? HeadCommit,
    string EffectiveBaseRef,
    IReadOnlyList<string> Files,
    bool UsedTrackedFallback = false);

/// <summary>Outcome of fetching a remote base ref before merge-readiness or local CI execution.</summary>
public sealed record GitFetchResult(
    bool Succeeded,
    string RemoteName,
    string RemoteBranch,
    string TrackingRef,
    string ErrorMessage);

/// <summary>Outcome of <c>git merge-tree --write-tree</c> between a base ref and <c>HEAD</c>.</summary>
public sealed record GitMergeTreeResult(
    bool Succeeded,
    bool HasConflicts,
    string EffectiveBaseRef,
    string? BaseCommit,
    string? HeadCommit,
    string? MergeBaseCommit,
    int BehindCount,
    string? TreeOid,
    IReadOnlyList<string> ConflictingFiles,
    string? ErrorMessage);

/// <summary>Outcome of synthesizing a temporary merged commit object from <see cref="GitMergeTreeResult"/>.</summary>
public sealed record GitSynthesizedMergeResult(
    GitMergeTreeResult MergeTree,
    string? MergeCommitSha);

/// <summary>Outcome of synthesizing an ephemeral commit from uncommitted working-tree and index changes.</summary>
public sealed record GitSnapshotCommitResult(
    bool Succeeded,
    string? HeadCommit,
    string? TreeOid,
    string? SnapshotCommitSha,
    string? ErrorMessage);

/// <summary>Disposable detached Git worktree allocated under <see cref="Path.GetTempPath"/>.</summary>
public sealed class DetachedGitWorktree : IDisposable
{
    private bool _disposed;

    public DetachedGitWorktree(string originRepoRoot, string worktreePath, string commitSha, string? pinnedRef = null)
    {
        OriginRepoRoot = originRepoRoot;
        WorktreePath = worktreePath;
        CommitSha = commitSha;
        PinnedRef = string.IsNullOrWhiteSpace(pinnedRef) ? null : pinnedRef.Trim();
    }

    /// <summary>The primary repository root that owns the shared Git object store and refs.</summary>
    public string OriginRepoRoot { get; }

    /// <summary>The temporary worktree directory under <see cref="Path.GetTempPath"/>.</summary>
    public string WorktreePath { get; }

    /// <summary>The commit SHA checked out in detached <c>HEAD</c> mode.</summary>
    public string CommitSha { get; }

    /// <summary>Temporary <c>refs/cadence-ci/&lt;runId&gt;</c> ref pinning synthesized commits while the worktree is active.</summary>
    public string? PinnedRef { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = GitRevisionScopeService.RunGitCaptured(OriginRepoRoot, "worktree", "remove", "--force", WorktreePath);

        if (Directory.Exists(WorktreePath))
        {
            try
            {
                foreach (var file in new DirectoryInfo(WorktreePath).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                Directory.Delete(WorktreePath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup when child processes release handles asynchronously.
            }
        }

        _ = GitRevisionScopeService.RunGitCaptured(OriginRepoRoot, "worktree", "prune");
        GitRevisionScopeService.DeleteTemporaryRef(OriginRepoRoot, PinnedRef);
    }
}

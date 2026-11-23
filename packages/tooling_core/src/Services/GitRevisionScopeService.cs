using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

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

/// <summary>
/// Centralizes Git diff and revision range resolution for <c>Cadence.Cli</c> and <c>Cadence.ToolingCore</c> quality gates.
/// </summary>
public static class GitRevisionScopeService
{
    /// <summary>Default base ref used when <c>--base</c> is omitted.</summary>
    public const string DefaultBaseRef = "origin/main";

    /// <summary>Fallback local <c>main</c> branch ref used when <c>origin/main</c> is unavailable.</summary>
    public const string FallbackLocalMainRef = "refs/heads/main";

    /// <summary>GitHub Actions environment variable naming the triggering event (<c>pull_request</c>, <c>merge_group</c>, <c>push</c>, <c>workflow_dispatch</c>).</summary>
    public const string GitHubEventNameVariable = "GITHUB_EVENT_NAME";

    /// <summary>GitHub Actions environment variable naming the full checked-out ref (<c>refs/heads/*</c>, <c>refs/tags/v*</c>, <c>refs/pull/*/merge</c>).</summary>
    public const string GitHubRefVariable = "GITHUB_REF";

    /// <summary>40-zero Git object ID emitted by GitHub Actions as <c>github.event.before</c> on tag and newly created branch pushes.</summary>
    public const string AllZeroSha = "0000000000000000000000000000000000000000";

    /// <summary>Ref namespace used to pin synthesized snapshot and merge commits during an active <c>cadence ci</c> run.</summary>
    public const string TemporaryRefPrefix = "refs/cadence-ci/";

    /// <summary>Creates a unique temporary ref name under <see cref="TemporaryRefPrefix"/>.</summary>
    public static string CreateTemporaryRefName() => $"{TemporaryRefPrefix}{Guid.NewGuid():N}";

    /// <summary>Normalizes a nullable <c>--base</c> option to <see cref="DefaultBaseRef"/> when omitted.</summary>
    public static string NormalizeBaseRef(string? baseRef) =>
        string.IsNullOrWhiteSpace(baseRef) ? DefaultBaseRef : baseRef.Trim();

    /// <summary>Whether <paramref name="gitRef"/> is <c>refs/heads/main</c>.</summary>
    public static bool IsMainBranchRef(string? gitRef) =>
        !string.IsNullOrWhiteSpace(gitRef) &&
        string.Equals(gitRef.Trim(), FallbackLocalMainRef, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="gitRef"/> is a release branch ref (<c>refs/heads/release/*</c>).</summary>
    public static bool IsReleaseBranchRef(string? gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            return false;
        }

        const string prefix = "refs/heads/release/";
        string trimmed = gitRef.Trim();
        return trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.Length > prefix.Length;
    }

    /// <summary>Whether <paramref name="gitRef"/> is a release version tag ref (<c>refs/tags/v*</c>).</summary>
    public static bool IsVersionTagRef(string? gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            return false;
        }

        const string prefix = "refs/tags/v";
        string trimmed = gitRef.Trim();
        return trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.Length > prefix.Length;
    }

    /// <summary>Whether <paramref name="gitRef"/> is a branch head ref (<c>refs/heads/*</c>).</summary>
    public static bool IsBranchHeadRef(string? gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            return false;
        }

        const string prefix = "refs/heads/";
        string trimmed = gitRef.Trim();
        return trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.Length > prefix.Length;
    }

    /// <summary>Whether <paramref name="gitRef"/> is an allowed protected dispatch/release target (<c>refs/heads/main</c>, <c>refs/heads/release/*</c>, or <c>refs/tags/v*</c>).</summary>
    public static bool IsProtectedRef(string? gitRef) =>
        IsMainBranchRef(gitRef) || IsReleaseBranchRef(gitRef) || IsVersionTagRef(gitRef);

    /// <summary>Whether <paramref name="candidate"/> is a non-empty all-zero Git SHA (such as <see cref="AllZeroSha"/>).</summary>
    public static bool IsAllZeroSha(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && IsAllZeros(candidate.Trim());

    /// <summary>
    /// Resolves CI event and target-ref scope from explicit arguments or <paramref name="environment"/>
    /// (<see cref="GitHubEventNameVariable"/> and <see cref="GitHubRefVariable"/>), distinguishing branch diffs
    /// (<c>pull_request</c>, <c>merge_group</c>, local runs), ancestor push commit ranges, new branch pushes
    /// (40-zero <c>BEFORE_SHA</c> on <c>refs/heads/*</c>), and immutable/protected target refs (<c>workflow_dispatch</c>
    /// and <c>refs/tags/v*</c>).
    /// </summary>
    public static GitEventScope ResolveEventScope(
        string repoRoot,
        string? baseRef = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null,
        Func<string, string?>? environment = null)
    {
        string effectiveEvent = (eventName ?? environment?.Invoke(GitHubEventNameVariable) ?? string.Empty).Trim();
        string effectiveRef = (gitRef ?? environment?.Invoke(GitHubRefVariable) ?? string.Empty).Trim();
        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        string? headCommit = HasGitRepository(repoRoot) ? TryResolveCommitSha(repoRoot, "HEAD") : null;

        if (string.Equals(effectiveEvent, "workflow_dispatch", StringComparison.OrdinalIgnoreCase))
        {
            return IsProtectedRef(effectiveRef)
                ? new GitEventScope(GitEventScopeKind.TargetRef, "workflow_dispatch", effectiveRef, effectiveBaseRef, headCommit, headCommit)
                : new GitEventScope(GitEventScopeKind.UnprotectedTargetRef, "workflow_dispatch", effectiveRef, effectiveBaseRef, null, headCommit);
        }

        if (string.Equals(effectiveEvent, "pull_request", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveEvent, "merge_group", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedCiEvent = effectiveEvent.ToLowerInvariant();
            string? ciCompareBase = TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
            return new GitEventScope(
                GitEventScopeKind.BranchDiff,
                normalizedCiEvent,
                effectiveRef,
                effectiveBaseRef,
                ciCompareBase,
                headCommit,
                IsCiBranchEvent: true);
        }

        bool isPushEvent = string.Equals(effectiveEvent, "push", StringComparison.OrdinalIgnoreCase);
        bool hasPushBefore = !string.IsNullOrWhiteSpace(pushBefore);
        if (IsVersionTagRef(effectiveRef) && (isPushEvent || hasPushBefore || string.IsNullOrEmpty(effectiveEvent)))
        {
            string tagEvent = string.IsNullOrEmpty(effectiveEvent) ? "push" : effectiveEvent.ToLowerInvariant();
            return new GitEventScope(GitEventScopeKind.TargetRef, tagEvent, effectiveRef, effectiveBaseRef, headCommit, headCommit);
        }

        if (isPushEvent || hasPushBefore)
        {
            string pushEventLabel = string.IsNullOrEmpty(effectiveEvent) ? "push" : effectiveEvent.ToLowerInvariant();
            string? validPushBase = TryResolveValidPushBase(repoRoot, pushBefore, headCommit);
            if (!string.IsNullOrEmpty(validPushBase))
            {
                return new GitEventScope(GitEventScopeKind.PushCommitRange, pushEventLabel, effectiveRef, effectiveBaseRef, validPushBase, headCommit);
            }

            if (!string.IsNullOrEmpty(effectiveRef) && !IsBranchHeadRef(effectiveRef))
            {
                return new GitEventScope(GitEventScopeKind.UnprotectedTargetRef, pushEventLabel, effectiveRef, effectiveBaseRef, null, headCommit);
            }

            string? newBranchBase = TryResolveCompareBaseWithLocalMainFallback(repoRoot, IsBranchHeadRef(effectiveRef) ? DefaultBaseRef : effectiveBaseRef, out string resolvedLabel);
            return new GitEventScope(GitEventScopeKind.NewBranchPush, pushEventLabel, effectiveRef, resolvedLabel, newBranchBase, headCommit);
        }

        string? localCompareBase = TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
        return new GitEventScope(GitEventScopeKind.BranchDiff, effectiveEvent, effectiveRef, effectiveBaseRef, localCompareBase, headCommit);
    }

    /// <summary>
    /// Resolves <c>merge-base(&lt;baseRef&gt;, HEAD)</c>, falling back to <see cref="FallbackLocalMainRef"/> when
    /// <paramref name="baseRef"/> is <see cref="DefaultBaseRef"/> and <c>origin/main</c> is absent.
    /// </summary>
    public static string? TryResolveCompareBaseWithLocalMainFallback(string repoRoot, string? baseRef, out string resolvedLabel)
    {
        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        string? primary = TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
        if (!string.IsNullOrEmpty(primary))
        {
            resolvedLabel = effectiveBaseRef;
            return primary;
        }

        if (string.Equals(effectiveBaseRef, DefaultBaseRef, StringComparison.Ordinal))
        {
            string? localMain = TryResolveBranchCompareBase(repoRoot, FallbackLocalMainRef);
            if (!string.IsNullOrEmpty(localMain))
            {
                resolvedLabel = FallbackLocalMainRef;
                return localMain;
            }
        }

        resolvedLabel = effectiveBaseRef;
        return null;
    }

    /// <summary>
    /// Returns all uncommitted changes against <c>HEAD</c> (staged index diffs plus working-tree modifications, deletions,
    /// and untracked files when requested), without comparing against any remote branch ref.
    /// </summary>
    public static List<string> GetUncommittedChangesAtHead(string repoRoot, bool includeDeletions = true, bool includeUntracked = true)
    {
        if (!HasGitRepository(repoRoot))
        {
            return [];
        }

        var combined = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string staged in GetStagedFiles(repoRoot, includeDeletions))
        {
            combined.Add(staged);
        }

        foreach (string working in GetWorkingTreeChanges(repoRoot, includeDeletions, includeUntracked))
        {
            combined.Add(working);
        }

        return [.. combined];
    }

    /// <summary>
    /// Returns <c>true</c> when at least one commit in <paramref name="revisionRange"/> carries a canonical Git trailer
    /// whose key matches <paramref name="trailerKey"/> and whose value matches <paramref name="expectedValue"/>.
    /// </summary>
    public static bool HasCommitTrailer(string repoRoot, string revisionRange, string trailerKey, string expectedValue)
    {
        if (string.IsNullOrWhiteSpace(revisionRange) ||
            string.IsNullOrWhiteSpace(trailerKey) ||
            string.IsNullOrWhiteSpace(expectedValue) ||
            !HasGitRepository(repoRoot))
        {
            return false;
        }

        var lines = RunGitLines(repoRoot, "log", $"--format=%(trailers:key={trailerKey},valueonly,unfold)", revisionRange);
        if (lines is null || lines.Count == 0)
        {
            return false;
        }

        return lines.Any(line => string.Equals(line.Trim(), expectedValue, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="repoRoot"/> contains a <c>.git</c> directory or worktree gitfile.</summary>
    public static bool HasGitRepository(string repoRoot) =>
        Directory.Exists(Path.Combine(repoRoot, ".git")) || File.Exists(Path.Combine(repoRoot, ".git"));

    /// <summary>
    /// Returns staged index paths (<c>--diff-filter=ACMR</c> by default, or all staged changes when
    /// <paramref name="includeDeletions"/> is <c>true</c>), excluding verbatim <c>MERGE_HEAD</c> files during a merge.
    /// </summary>
    public static List<string> GetStagedFiles(string repoRoot, bool includeDeletions = false)
    {
        if (!HasGitRepository(repoRoot))
        {
            return [];
        }

        string[] diffArgs = includeDeletions
            ? ["diff", "--cached", "--name-only", "-z"]
            : ["diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z"];

        var staged = RunGitNullTerminated(repoRoot, diffArgs);
        if (staged is null || staged.Count == 0)
        {
            return [];
        }

        // During a merge commit, `git diff --cached` against HEAD lists every file brought in from MERGE_HEAD even when its
        // staged content matches MERGE_HEAD verbatim. Keep only paths that also differ from MERGE_HEAD.
        string? mergeHead = TryResolveRevision(repoRoot, "MERGE_HEAD");
        if (!string.IsNullOrEmpty(mergeHead))
        {
            string[] mergeDiffArgs = includeDeletions
                ? ["diff", "--cached", "--name-only", "-z", mergeHead]
                : ["diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z", mergeHead];

            var differsFromMergeHead = RunGitNullTerminated(repoRoot, mergeDiffArgs);
            if (differsFromMergeHead is not null)
            {
                var differing = new HashSet<string>(differsFromMergeHead, StringComparer.Ordinal);
                staged = [.. staged.Where(differing.Contains)];
            }
        }

        return NormalizePaths(staged);
    }

    /// <summary>Returns all Git-tracked files via <c>git ls-files -z</c>, or <c>null</c> when Git is unavailable.</summary>
    public static List<string>? TryGetTrackedFiles(string repoRoot)
    {
        if (!HasGitRepository(repoRoot))
        {
            return null;
        }

        var tracked = RunGitNullTerminated(repoRoot, "ls-files", "-z");
        return tracked is null ? null : NormalizePaths(tracked);
    }

    /// <summary>
    /// Resolves the candidate files to scan for file-based quality gates (<c>detect-secrets</c>, <c>deid</c>, <c>openapi</c>).
    /// Falls back to <c>git ls-files -z</c> when <c>HEAD</c> equals the compare base with a clean working tree, when
    /// <paramref name="baseRef"/> is missing, or when <paramref name="pushBefore"/> is all zeros, a tag, equal to
    /// <c>HEAD</c>, or not an ancestor of <c>HEAD</c>.
    /// </summary>
    public static GitRevisionScope ResolveFileScanScope(
        string repoRoot,
        string[]? explicitFiles = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null,
        Func<string, string?>? environment = null)
    {
        string effectiveBaseRef = NormalizeBaseRef(baseRef);

        if (preCommit)
        {
            var stagedFiles = explicitFiles is not null
                ? NormalizeExplicitPaths(repoRoot, explicitFiles)
                : GetStagedFiles(repoRoot, includeDeletions: false);
            return new GitRevisionScope(
                GitScopeKind.Staged,
                TryResolveBranchCompareBase(repoRoot, effectiveBaseRef),
                TryResolveCommitSha(repoRoot, "HEAD"),
                effectiveBaseRef,
                stagedFiles);
        }

        if (explicitFiles is not null)
        {
            return new GitRevisionScope(
                GitScopeKind.ExplicitFiles,
                null,
                TryResolveCommitSha(repoRoot, "HEAD"),
                effectiveBaseRef,
                NormalizeExplicitPaths(repoRoot, explicitFiles));
        }

        if (!HasGitRepository(repoRoot))
        {
            return new GitRevisionScope(GitScopeKind.NonGitDirectory, null, null, effectiveBaseRef, []);
        }

        var eventScope = ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef, environment);
        string? headCommit = eventScope.HeadCommit;

        if (eventScope.Kind is GitEventScopeKind.TargetRef or GitEventScopeKind.UnprotectedTargetRef)
        {
            var trackedTarget = TryGetTrackedFiles(repoRoot) ?? [];
            return new GitRevisionScope(
                GitScopeKind.TrackedFiles,
                null,
                headCommit,
                effectiveBaseRef,
                trackedTarget,
                UsedTrackedFallback: true);
        }

        if (eventScope.Kind == GitEventScopeKind.PushCommitRange && !string.IsNullOrEmpty(eventScope.CompareBase))
        {
            var pushFiles = CollectChangedFilesSinceCommit(repoRoot, eventScope.CompareBase, includeDeletions: false, includeUntracked: false);
            return new GitRevisionScope(
                GitScopeKind.PushRange,
                eventScope.CompareBase,
                headCommit,
                effectiveBaseRef,
                pushFiles);
        }

        if (eventScope.Kind == GitEventScopeKind.NewBranchPush)
        {
            if (IsBranchHeadRef(eventScope.GitRef) &&
                !string.IsNullOrEmpty(eventScope.CompareBase) &&
                !string.Equals(eventScope.CompareBase, headCommit, StringComparison.Ordinal))
            {
                var newBranchFiles = CollectChangedFilesSinceCommit(repoRoot, eventScope.CompareBase, includeDeletions: false, includeUntracked: false);
                return new GitRevisionScope(
                    GitScopeKind.BranchDiff,
                    eventScope.CompareBase,
                    headCommit,
                    eventScope.EffectiveBaseRef,
                    newBranchFiles);
            }

            var trackedFallback = TryGetTrackedFiles(repoRoot) ?? [];
            return new GitRevisionScope(
                GitScopeKind.TrackedFiles,
                null,
                headCommit,
                effectiveBaseRef,
                trackedFallback,
                UsedTrackedFallback: true);
        }

        string? compareBase = eventScope.CompareBase;
        if (string.IsNullOrEmpty(compareBase) || string.Equals(compareBase, headCommit, StringComparison.Ordinal))
        {
            var tracked = TryGetTrackedFiles(repoRoot) ?? [];
            return new GitRevisionScope(
                GitScopeKind.TrackedFiles,
                compareBase,
                headCommit,
                effectiveBaseRef,
                tracked,
                UsedTrackedFallback: true);
        }

        var branchFiles = CollectChangedFilesSinceCommit(repoRoot, compareBase, includeDeletions: false, includeUntracked: false);
        return new GitRevisionScope(
            GitScopeKind.BranchDiff,
            compareBase,
            headCommit,
            effectiveBaseRef,
            branchFiles);
    }

    /// <summary>
    /// Resolves changed files since <paramref name="baseRef"/> (or <paramref name="pushBefore"/> when given), returning
    /// <c>null</c> whenever the caller should fall back to <see cref="TryGetTrackedFiles"/> (non-Git directory, missing base
    /// ref, zero/tag/non-ancestor <paramref name="pushBefore"/>, target ref, or clean <c>HEAD == compareBase</c>).
    /// </summary>
    public static List<string>? TryGetChangedFilesSinceBase(
        string repoRoot,
        string baseRef = DefaultBaseRef,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null)
    {
        var scope = ResolveFileScanScope(
            repoRoot,
            explicitFiles: null,
            preCommit: false,
            baseRef: baseRef,
            pushBefore: pushBefore,
            eventName: eventName,
            gitRef: gitRef);

        return scope.Kind is GitScopeKind.BranchDiff or GitScopeKind.PushRange
            ? [.. scope.Files]
            : null;
    }

    /// <summary>
    /// Resolves <paramref name="pushBefore"/> to a full commit SHA only when it is non-zero, not a tag ref or tag object,
    /// a valid commit object, a strict ancestor of <paramref name="headCommit"/> (not equal to <c>HEAD</c>).
    /// </summary>
    public static string? TryResolveValidPushBase(string repoRoot, string? pushBefore, string? headCommit = null)
    {
        if (string.IsNullOrWhiteSpace(pushBefore) || !HasGitRepository(repoRoot))
        {
            return null;
        }

        string candidate = pushBefore.Trim();
        if (IsAllZeros(candidate) || IsTagReference(repoRoot, candidate))
        {
            return null;
        }

        string? objectType = RunGitSingleLine(repoRoot, "cat-file", "-t", candidate);
        if (!string.Equals(objectType, "commit", StringComparison.Ordinal))
        {
            return null;
        }

        string? resolved = TryResolveCommitSha(repoRoot, candidate);
        headCommit ??= TryResolveCommitSha(repoRoot, "HEAD");
        if (string.IsNullOrEmpty(resolved) ||
            string.IsNullOrEmpty(headCommit) ||
            string.Equals(resolved, headCommit, StringComparison.Ordinal))
        {
            return null;
        }

        return RunGitZeroExit(repoRoot, "merge-base", "--is-ancestor", resolved, headCommit)
            ? resolved
            : null;
    }

    /// <summary>
    /// Resolves the branch comparison base commit against <paramref name="baseRef"/> (honouring <c>MERGE_HEAD</c> when
    /// merging <paramref name="baseRef"/> into the current branch).
    /// </summary>
    public static string? TryResolveBranchCompareBase(string repoRoot, string? baseRef = null)
    {
        if (!HasGitRepository(repoRoot))
        {
            return null;
        }

        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        string? mergeHead = TryResolveRevision(repoRoot, "MERGE_HEAD");
        if (!string.IsNullOrEmpty(mergeHead) &&
            RunGitZeroExit(repoRoot, "merge-base", "--is-ancestor", effectiveBaseRef, mergeHead))
        {
            string? resolvedBase = TryResolveCommitSha(repoRoot, effectiveBaseRef);
            if (!string.IsNullOrEmpty(resolvedBase))
            {
                return resolvedBase;
            }
        }

        return RunGitSingleLine(repoRoot, "merge-base", effectiveBaseRef, "HEAD");
    }

    /// <summary>
    /// Splits a remote-tracking base ref (such as <c>origin/main</c>, <c>main</c>, or <c>refs/remotes/origin/release/1.0</c>)
    /// into its remote name, remote branch name, and local tracking ref (<c>refs/remotes/&lt;remote&gt;/&lt;branch&gt;</c>).
    /// Returns <c>false</c> for commit SHAs, relative revisions (such as <c>HEAD~1</c>), and tag references.
    /// </summary>
    public static bool TrySplitRemoteRef(
        string repoRoot,
        string? baseRef,
        out string remoteName,
        out string remoteBranch,
        out string trackingRef)
    {
        remoteName = string.Empty;
        remoteBranch = string.Empty;
        trackingRef = string.Empty;

        string rawNormalized = NormalizeBaseRef(baseRef);
        if (IsLocalNonBranchRevision(repoRoot, rawNormalized))
        {
            return false;
        }

        bool explicitRemoteRef = false;
        bool explicitHeadRef = false;
        string normalized = rawNormalized;
        if (normalized.StartsWith("refs/remotes/", StringComparison.Ordinal))
        {
            explicitRemoteRef = true;
            normalized = normalized["refs/remotes/".Length..];
        }
        else if (normalized.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            explicitHeadRef = true;
            normalized = normalized["refs/heads/".Length..];
        }

        if (normalized.Length == 0)
        {
            return false;
        }

        var remotes = HasGitRepository(repoRoot)
            ? (RunGitLines(repoRoot, "remote") ?? []).OrderByDescending(r => r.Length).ToList()
            : [];

        if (!explicitHeadRef)
        {
            foreach (string configuredRemote in remotes)
            {
                string prefix = configuredRemote + "/";
                if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
                {
                    remoteName = configuredRemote;
                    remoteBranch = normalized[prefix.Length..];
                    trackingRef = $"refs/remotes/{remoteName}/{remoteBranch}";
                    return true;
                }
            }

            int slashIndex = normalized.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex > 0 && slashIndex < normalized.Length - 1)
            {
                bool matchesBranchOnOrigin = !explicitRemoteRef
                    && remotes.Contains("origin", StringComparer.Ordinal)
                    && HasGitRepository(repoRoot)
                    && (RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/heads/{normalized}")
                        || RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/remotes/origin/{normalized}"));
                if (!matchesBranchOnOrigin)
                {
                    remoteName = normalized[..slashIndex];
                    remoteBranch = normalized[(slashIndex + 1)..];
                    trackingRef = $"refs/remotes/{remoteName}/{remoteBranch}";
                    return true;
                }
            }
        }

        if (remotes.Contains("origin", StringComparer.Ordinal))
        {
            remoteName = "origin";
            remoteBranch = normalized;
            trackingRef = $"refs/remotes/origin/{remoteBranch}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fetches the remote branch backing <paramref name="baseRef"/> into its local remote-tracking ref without tags, or resolves
    /// commit SHAs, relative revisions (<c>HEAD~1</c>), and local tag refs directly without invoking <c>git fetch</c>.
    /// Fails closed when <paramref name="repoRoot"/> is not a Git repository, the revision/remote cannot be resolved, or <c>git fetch</c> fails.
    /// </summary>
    public static GitFetchResult FetchBaseRef(string repoRoot, string? baseRef = null)
    {
        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        if (!HasGitRepository(repoRoot))
        {
            return new GitFetchResult(false, string.Empty, string.Empty, effectiveBaseRef, $"'{repoRoot}' is not a Git repository.");
        }

        if (IsLocalNonBranchRevision(repoRoot, effectiveBaseRef))
        {
            string? localCommit = TryResolveCommitSha(repoRoot, effectiveBaseRef);
            if (string.IsNullOrEmpty(localCommit))
            {
                return new GitFetchResult(
                    false,
                    string.Empty,
                    string.Empty,
                    effectiveBaseRef,
                    $"Cannot resolve local Git revision '{effectiveBaseRef}' in '{repoRoot}'.");
            }

            string resolvedRef = localCommit;
            if (IsTagReference(repoRoot, effectiveBaseRef))
            {
                resolvedRef = effectiveBaseRef.StartsWith("refs/tags/", StringComparison.Ordinal)
                    ? effectiveBaseRef
                    : $"refs/tags/{effectiveBaseRef}";
            }

            return new GitFetchResult(true, string.Empty, string.Empty, resolvedRef, string.Empty);
        }

        if (!TrySplitRemoteRef(repoRoot, effectiveBaseRef, out string remoteName, out string remoteBranch, out string trackingRef))
        {
            return new GitFetchResult(
                false,
                string.Empty,
                string.Empty,
                effectiveBaseRef,
                $"Cannot determine a Git remote and branch to fetch for base ref '{effectiveBaseRef}'.");
        }

        var (exitCode, stdout, stderr) = RunGitCaptured(
            repoRoot,
            "fetch",
            "--no-tags",
            remoteName,
            $"+refs/heads/{remoteBranch}:{trackingRef}");

        if (exitCode == 0)
        {
            return new GitFetchResult(true, remoteName, remoteBranch, trackingRef, string.Empty);
        }

        string details;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            details = stderr.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(stdout))
        {
            details = stdout.Trim();
        }
        else
        {
            details = $"git fetch exited with code {exitCode}.";
        }

        return new GitFetchResult(false, remoteName, remoteBranch, trackingRef, details);
    }

    /// <summary>
    /// Returns how many commits <paramref name="head"/> is behind <paramref name="baseRef"/> (<c>git rev-list --count &lt;head&gt;..&lt;baseRef&gt;</c>),
    /// or <c>null</c> when either revision cannot be resolved.
    /// </summary>
    public static int? TryGetBehindCount(string repoRoot, string? baseRef = null, string head = "HEAD")
    {
        if (!HasGitRepository(repoRoot))
        {
            return null;
        }

        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        string? rawCount = RunGitSingleLine(repoRoot, "rev-list", "--count", $"{head}..{effectiveBaseRef}");
        return int.TryParse(rawCount, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : null;
    }

    /// <summary>
    /// Evaluates whether <paramref name="head"/> merges cleanly with <paramref name="baseRef"/> using
    /// <c>git merge-tree --write-tree --name-only --no-messages -z</c> without mutating the index or working tree.
    /// </summary>
    public static GitMergeTreeResult PreviewMergeTree(string repoRoot, string? baseRef = null, string head = "HEAD")
    {
        string effectiveBaseRef = NormalizeBaseRef(baseRef);
        if (!HasGitRepository(repoRoot))
        {
            return new GitMergeTreeResult(
                false,
                false,
                effectiveBaseRef,
                null,
                null,
                null,
                0,
                null,
                [],
                $"'{repoRoot}' is not a Git repository.");
        }

        string? headCommit = TryResolveCommitSha(repoRoot, head);
        if (string.IsNullOrEmpty(headCommit))
        {
            return new GitMergeTreeResult(
                false,
                false,
                effectiveBaseRef,
                null,
                null,
                null,
                0,
                null,
                [],
                $"Cannot resolve '{head}' commit in '{repoRoot}'.");
        }

        string? baseCommit = TryResolveCommitSha(repoRoot, effectiveBaseRef);
        if (string.IsNullOrEmpty(baseCommit))
        {
            return new GitMergeTreeResult(
                false,
                false,
                effectiveBaseRef,
                null,
                headCommit,
                null,
                0,
                null,
                [],
                $"Base ref '{effectiveBaseRef}' was not found. Run 'git fetch origin main' first.");
        }

        string? mergeBase = RunGitSingleLine(repoRoot, "merge-base", baseCommit, headCommit);
        if (string.IsNullOrEmpty(mergeBase))
        {
            return new GitMergeTreeResult(
                false,
                false,
                effectiveBaseRef,
                baseCommit,
                headCommit,
                null,
                0,
                null,
                [],
                $"Base ref '{effectiveBaseRef}' shares no common history with '{head}'.");
        }

        int behindCount = TryGetBehindCount(repoRoot, baseCommit, headCommit) ?? 0;
        var (exitCode, stdout, stderr) = RunGitCaptured(
            repoRoot,
            "merge-tree",
            "--write-tree",
            "--name-only",
            "--no-messages",
            "-z",
            baseCommit,
            headCommit);

        string[] parts = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? treeOid = parts.FirstOrDefault();

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(treeOid))
        {
            return new GitMergeTreeResult(
                true,
                false,
                effectiveBaseRef,
                baseCommit,
                headCommit,
                mergeBase,
                behindCount,
                treeOid,
                [],
                null);
        }

        if (exitCode == 1)
        {
            var conflicts = NormalizePaths(parts.Skip(1));
            string conflictSummary = conflicts.Count > 0 ? string.Join(", ", conflicts) : "(unknown paths)";
            return new GitMergeTreeResult(
                false,
                true,
                effectiveBaseRef,
                baseCommit,
                headCommit,
                mergeBase,
                behindCount,
                treeOid,
                conflicts,
                $"Merge conflicts detected between '{effectiveBaseRef}' and '{head}': {conflictSummary}");
        }

        string error = !string.IsNullOrWhiteSpace(stderr)
            ? stderr.Trim()
            : $"git merge-tree failed with exit code {exitCode}.";
        return new GitMergeTreeResult(
            false,
            false,
            effectiveBaseRef,
            baseCommit,
            headCommit,
            mergeBase,
            behindCount,
            treeOid,
            [],
            error);
    }

    /// <summary>
    /// Copies <c>.git/index</c> to a temporary <c>GIT_INDEX_FILE</c>, stages all working-tree changes via <c>git add -A</c>,
    /// and synthesizes an ephemeral snapshot commit on <paramref name="head"/> without mutating the caller checkout or index.
    /// </summary>
    public static GitSnapshotCommitResult SynthesizeWorkingTreeSnapshot(
        string repoRoot,
        string? pinnedRef = null,
        string head = "HEAD")
    {
        if (!HasGitRepository(repoRoot))
        {
            return new GitSnapshotCommitResult(false, null, null, null, $"'{repoRoot}' is not a Git repository.");
        }

        string? headCommit = TryResolveCommitSha(repoRoot, head);
        if (string.IsNullOrEmpty(headCommit))
        {
            return new GitSnapshotCommitResult(false, null, null, null, $"Cannot resolve '{head}' commit in '{repoRoot}'.");
        }

        string? gitIndexPath = RunGitSingleLine(repoRoot, "rev-parse", "--git-path", "index");
        string sourceIndexPath;
        if (string.IsNullOrWhiteSpace(gitIndexPath))
        {
            sourceIndexPath = Path.Combine(repoRoot, ".git", "index");
        }
        else
        {
            sourceIndexPath = Path.IsPathRooted(gitIndexPath)
                ? gitIndexPath
                : Path.Combine(repoRoot, gitIndexPath);
        }

        string tempIndexPath = Path.Combine(Path.GetTempPath(), $"cadence-ci-index-{Guid.NewGuid():N}");
        var indexEnv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_INDEX_FILE"] = tempIndexPath,
        };

        try
        {
            if (File.Exists(sourceIndexPath))
            {
                File.Copy(sourceIndexPath, tempIndexPath, overwrite: true);
            }

            var (addExit, addOut, addErr) = RunGitCapturedWithEnv(repoRoot, indexEnv, "add", "-A");
            if (addExit != 0)
            {
                string detail = FormatGitFailure("git add -A", addExit, addOut, addErr);
                return new GitSnapshotCommitResult(false, headCommit, null, null, detail);
            }

            var (treeExit, treeOut, treeErr) = RunGitCapturedWithEnv(repoRoot, indexEnv, "write-tree");
            string? treeOid = FirstNonEmptyLine(treeOut);
            if (treeExit != 0 || string.IsNullOrWhiteSpace(treeOid))
            {
                string detail = FormatGitFailure("git write-tree", treeExit, treeOut, treeErr);
                return new GitSnapshotCommitResult(false, headCommit, null, null, detail);
            }

            var (commitExit, snapshotSha, commitErr) = CreateCommitObject(
                repoRoot,
                treeOid,
                "Synthesize uncommitted working-tree snapshot",
                headCommit);
            if (commitExit != 0 || string.IsNullOrWhiteSpace(snapshotSha))
            {
                return new GitSnapshotCommitResult(false, headCommit, treeOid, null, commitErr);
            }

            if (!string.IsNullOrWhiteSpace(pinnedRef) && !TryPinTemporaryRef(repoRoot, pinnedRef, snapshotSha, out string? pinError))
            {
                return new GitSnapshotCommitResult(false, headCommit, treeOid, null, pinError);
            }

            return new GitSnapshotCommitResult(true, headCommit, treeOid, snapshotSha, null);
        }
        finally
        {
            DeleteFileBestEffort(tempIndexPath);
            DeleteFileBestEffort(tempIndexPath + ".lock");
        }
    }

    /// <summary>
    /// Synthesizes a merged commit object via <c>git merge-tree --write-tree</c> and <c>git commit-tree</c> when
    /// <paramref name="head"/> is behind <paramref name="baseRef"/>, or reuses <paramref name="head"/> directly when it
    /// already incorporates <paramref name="baseRef"/>. Leaves caller branch refs, index, and working tree untouched,
    /// optionally pinning the resulting commit under <paramref name="pinnedRef"/>.
    /// </summary>
    public static GitSynthesizedMergeResult SynthesizeMergedCommit(
        string repoRoot,
        string? baseRef = null,
        string head = "HEAD",
        string? pinnedRef = null)
    {
        var preview = PreviewMergeTree(repoRoot, baseRef, head);
        if (!preview.Succeeded ||
            string.IsNullOrEmpty(preview.BaseCommit) ||
            string.IsNullOrEmpty(preview.HeadCommit) ||
            string.IsNullOrEmpty(preview.TreeOid))
        {
            return new GitSynthesizedMergeResult(preview, null);
        }

        string commitSha;
        if (preview.BehindCount == 0 || string.Equals(preview.BaseCommit, preview.HeadCommit, StringComparison.Ordinal))
        {
            commitSha = preview.HeadCommit;
        }
        else
        {
            var (exitCode, createdSha, errorDetail) = CreateCommitObject(
                repoRoot,
                preview.TreeOid,
                $"Synthesize merged CI tree ({preview.EffectiveBaseRef} + {head})",
                preview.BaseCommit,
                preview.HeadCommit);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(createdSha))
            {
                return new GitSynthesizedMergeResult(preview with { Succeeded = false, ErrorMessage = errorDetail }, null);
            }

            commitSha = createdSha;
        }

        if (!string.IsNullOrWhiteSpace(pinnedRef) && !TryPinTemporaryRef(repoRoot, pinnedRef, commitSha, out string? pinError))
        {
            return new GitSynthesizedMergeResult(preview with { Succeeded = false, ErrorMessage = pinError }, null);
        }

        return new GitSynthesizedMergeResult(preview, commitSha);
    }

    /// <summary>
    /// Pins <paramref name="commitSha"/> under <paramref name="refName"/> with <c>core.logAllRefUpdates=false</c> so synthesized
    /// commits stay reachable without creating reflog entries.
    /// </summary>
    public static bool TryPinTemporaryRef(string repoRoot, string refName, string commitSha, out string? errorMessage)
    {
        errorMessage = null;
        var (exitCode, stdout, stderr) = RunGitCaptured(
            repoRoot,
            "-c",
            "core.logAllRefUpdates=false",
            "update-ref",
            refName,
            commitSha);
        if (exitCode == 0)
        {
            return true;
        }

        errorMessage = FormatGitFailure($"git update-ref {refName}", exitCode, stdout, stderr);
        return false;
    }

    /// <summary>Deletes a temporary Git ref with <c>core.logAllRefUpdates=false</c> when present.</summary>
    public static void DeleteTemporaryRef(string repoRoot, string? refName)
    {
        if (string.IsNullOrWhiteSpace(refName) || !HasGitRepository(repoRoot))
        {
            return;
        }

        _ = RunGitCaptured(
            repoRoot,
            "-c",
            "core.logAllRefUpdates=false",
            "update-ref",
            "-d",
            refName.Trim());
    }

    /// <summary>
    /// Creates a detached temporary Git worktree under <see cref="Path.GetTempPath"/> checked out at <paramref name="commitSha"/>
    /// and pins <paramref name="commitSha"/> under <c>refs/cadence-ci/&lt;runId&gt;</c> until <see cref="DetachedGitWorktree.Dispose"/> runs.
    /// </summary>
    public static bool TryCreateDetachedWorktree(
        string repoRoot,
        string commitSha,
        out DetachedGitWorktree? worktree,
        out string? errorMessage,
        string directoryPrefix = "cadence-ci-worktree",
        string? pinnedRef = null)
    {
        worktree = null;
        errorMessage = null;

        if (!HasGitRepository(repoRoot))
        {
            errorMessage = $"'{repoRoot}' is not a Git repository.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            errorMessage = "Commit SHA for detached worktree must not be empty.";
            return false;
        }

        string runId = $"{Guid.NewGuid():N}";
        string effectivePinnedRef = string.IsNullOrWhiteSpace(pinnedRef)
            ? $"{TemporaryRefPrefix}{runId}"
            : pinnedRef.Trim();
        if (!TryPinTemporaryRef(repoRoot, effectivePinnedRef, commitSha, out errorMessage))
        {
            return false;
        }

        string tempWorktreePath = Path.Combine(Path.GetTempPath(), $"{directoryPrefix}-{runId}");
        var (exitCode, stdout, stderr) = RunGitCaptured(
            repoRoot,
            "worktree",
            "add",
            "--detach",
            tempWorktreePath,
            commitSha);

        if (exitCode != 0)
        {
            errorMessage = FormatGitFailure("git worktree add", exitCode, stdout, stderr);
            DeleteTemporaryRef(repoRoot, effectivePinnedRef);

            if (Directory.Exists(tempWorktreePath))
            {
                try
                {
                    Directory.Delete(tempWorktreePath, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup of partially created worktree directory.
                }
            }

            return false;
        }

        worktree = new DetachedGitWorktree(repoRoot, tempWorktreePath, commitSha, effectivePinnedRef);
        return true;
    }

    /// <summary>Resolves a Git revision to its commit SHA (<c>rev-parse --verify -q &lt;rev&gt;^{commit}</c>).</summary>
    public static string? TryResolveCommitSha(string repoRoot, string revision) =>
        HasGitRepository(repoRoot)
            ? RunGitSingleLine(repoRoot, "rev-parse", "--verify", "-q", $"{revision}^{{commit}}")
            : null;

    /// <summary>Resolves a Git revision via <c>rev-parse --verify -q</c>.</summary>
    public static string? TryResolveRevision(string repoRoot, string revision) =>
        HasGitRepository(repoRoot)
            ? RunGitSingleLine(repoRoot, "rev-parse", "--verify", "-q", revision)
            : null;

    /// <summary>
    /// Collects changed paths between <paramref name="compareBase"/> and the working tree / index (plus untracked files when
    /// <paramref name="includeUntracked"/> is <c>true</c>).
    /// </summary>
    public static List<string> CollectChangedFilesSinceCommit(
        string repoRoot,
        string compareBase,
        bool includeDeletions = true,
        bool includeUntracked = false)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(compareBase))
        {
            return [];
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        string[] workingDiffArgs = includeDeletions
            ? ["diff", "--name-only", "-z", compareBase]
            : ["diff", "--name-only", "--diff-filter=d", "-z", compareBase];
        string[] cachedDiffArgs = includeDeletions
            ? ["diff", "--cached", "--name-only", "-z", compareBase]
            : ["diff", "--cached", "--name-only", "--diff-filter=d", "-z", compareBase];

        foreach (string path in RunGitNullTerminated(repoRoot, workingDiffArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        foreach (string path in RunGitNullTerminated(repoRoot, cachedDiffArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        if (includeUntracked)
        {
            foreach (string path in RunGitNullTerminated(repoRoot, "ls-files", "--others", "--exclude-standard", "-z") ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        changed.Remove(string.Empty);
        return [.. changed];
    }

    /// <summary>Collects paths changed between <paramref name="fromCommit"/> and <paramref name="toCommit"/>.</summary>
    public static List<string> GetChangedFilesBetweenCommits(
        string repoRoot,
        string fromCommit,
        string toCommit,
        bool includeDeletions = true)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(fromCommit) || string.IsNullOrWhiteSpace(toCommit))
        {
            return [];
        }

        string[] args = includeDeletions
            ? ["diff", "--name-only", "-z", fromCommit, toCommit]
            : ["diff", "--name-only", "--diff-filter=d", "-z", fromCommit, toCommit];

        return NormalizePaths(RunGitNullTerminated(repoRoot, args) ?? []);
    }

    /// <summary>Collects uncommitted staged, unstaged, and untracked paths in <paramref name="repoRoot"/>.</summary>
    public static List<string> GetWorkingTreeChanges(string repoRoot, bool includeDeletions = true, bool includeUntracked = true)
    {
        if (!HasGitRepository(repoRoot))
        {
            return [];
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        string? headCommit = TryResolveCommitSha(repoRoot, "HEAD");

        if (!string.IsNullOrEmpty(headCommit))
        {
            string[] headDiffArgs = includeDeletions
                ? ["diff", "--name-only", "-z", "HEAD"]
                : ["diff", "--name-only", "--diff-filter=d", "-z", "HEAD"];
            foreach (string path in RunGitNullTerminated(repoRoot, headDiffArgs) ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        string[] stagedArgs = includeDeletions
            ? ["diff", "--cached", "--name-only", "-z"]
            : ["diff", "--cached", "--name-only", "--diff-filter=d", "-z"];
        foreach (string path in RunGitNullTerminated(repoRoot, stagedArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        if (includeUntracked)
        {
            foreach (string path in RunGitNullTerminated(repoRoot, "ls-files", "--others", "--exclude-standard", "-z") ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        changed.Remove(string.Empty);
        return [.. changed];
    }

    /// <summary>Lists commits in <c>&lt;compareBase&gt;..&lt;head&gt;</c> in chronological order (<c>--reverse</c>).</summary>
    public static List<string> GetCommitRange(string repoRoot, string compareBase, string head = "HEAD")
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(compareBase))
        {
            return [];
        }

        return RunGitLines(repoRoot, "rev-list", "--reverse", $"{compareBase}..{head}") ?? [];
    }

    /// <summary>Reads file content at <c>&lt;revision&gt;:&lt;relativePath&gt;</c> (use empty revision for staged index <c>:&lt;path&gt;</c>).</summary>
    public static string? ReadFileAtRevision(string repoRoot, string revision, string relativePath)
    {
        if (!HasGitRepository(repoRoot))
        {
            return null;
        }

        string normalized = NormalizePath(relativePath);
        string spec = string.IsNullOrEmpty(revision) ? $":{normalized}" : $"{revision}:{normalized}";
        return RunGitRaw(repoRoot, "show", spec);
    }

    /// <summary>Lists paths differing between the working tree (or staged index when <paramref name="cached"/> is true) and <paramref name="revision"/>.</summary>
    public static List<string>? TryGetPathsDifferingFromRevision(string repoRoot, string revision, bool cached)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(revision))
        {
            return null;
        }

        string[] args = cached
            ? ["diff", "--cached", "--name-only", "-z", revision]
            : ["diff", "--name-only", "-z", revision];
        var raw = RunGitNullTerminated(repoRoot, args);
        return raw is null ? null : NormalizePaths(raw);
    }

    /// <summary>Whether <paramref name="value"/> consists solely of <c>'0'</c> characters (such as Git's 40-zero null SHA).</summary>
    public static bool IsAllZeros(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().All(c => c == '0');

    /// <summary>Whether <paramref name="revision"/> names a tag reference (<c>refs/tags/*</c> or a local tag name).</summary>
    public static bool IsTagReference(string repoRoot, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        string trimmed = revision.Trim();
        if (trimmed.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            return true;
        }

        return HasGitRepository(repoRoot) &&
               RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/tags/{trimmed}");
    }

    /// <summary>Whether <paramref name="revision"/> is a relative, peeled, or special Git revision rather than a branch ref.</summary>
    public static bool IsRelativeOrSpecialRevision(string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        string trimmed = revision.Trim();
        if (trimmed is "HEAD" or "@")
        {
            return true;
        }

        if (trimmed.IndexOfAny(['~', '^', ':']) >= 0 || trimmed.Contains("@{", StringComparison.Ordinal))
        {
            return true;
        }

        return trimmed.StartsWith("refs/", StringComparison.Ordinal)
            && !trimmed.StartsWith("refs/remotes/", StringComparison.Ordinal)
            && !trimmed.StartsWith("refs/heads/", StringComparison.Ordinal);
    }

    public static List<string>? RunGitNullTerminated(string repoRoot, params string[] args)
    {
        string? output = RunGitRaw(repoRoot, args);
        if (output is null)
        {
            return null;
        }

        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    public static List<string>? RunGitLines(string repoRoot, params string[] args)
    {
        string? output = RunGitRaw(repoRoot, args);
        if (output is null)
        {
            return null;
        }

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public static string? RunGitSingleLine(string repoRoot, params string[] args) =>
        RunGitLines(repoRoot, args)?.FirstOrDefault();

    public static bool RunGitZeroExit(string repoRoot, params string[] args) =>
        RunGitRaw(repoRoot, args) is not null;

    public static string? RunGitRaw(string repoRoot, params string[] args)
    {
        var (exitCode, stdout, _) = RunGitCaptured(repoRoot, args);
        return exitCode == 0 ? stdout : null;
    }

    public static (int ExitCode, string Stdout, string Stderr) RunGitCaptured(string repoRoot, params string[] args) =>
        RunGitCapturedWithEnv(repoRoot, environment: null, args);

    public static (int ExitCode, string Stdout, string Stderr) RunGitCapturedWithEnv(
        string repoRoot,
        IReadOnlyDictionary<string, string>? environment,
        params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, string.Empty, "Failed to start git process.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = stderrTask.GetAwaiter().GetResult();
            process.WaitForExit();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static bool IsLocalNonBranchRevision(string repoRoot, string normalized)
    {
        if (IsTagReference(repoRoot, normalized) || IsRelativeOrSpecialRevision(normalized))
        {
            return true;
        }

        if (!IsHexRevisionCandidate(normalized))
        {
            return false;
        }

        if (!HasGitRepository(repoRoot))
        {
            return normalized.Length is 40 or 64;
        }

        if (RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/heads/{normalized}")
            || RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/remotes/origin/{normalized}"))
        {
            return false;
        }

        return normalized.Length is 40 or 64 || TryResolveCommitSha(repoRoot, normalized) is not null;
    }

    private static bool IsHexRevisionCandidate(string value) =>
        value.Length is >= 4 and <= 64 && !IsAllZeros(value) && value.All(Uri.IsHexDigit);

    private static (int ExitCode, string? CommitSha, string ErrorMessage) CreateCommitObject(
        string repoRoot,
        string treeOid,
        string message,
        params string[] parentCommits)
    {
        var args = new List<string>
        {
            "-c",
            "user.name=Cadence CI",
            "-c",
            "user.email=ci@example.invalid", // deid-ignore: synthetic local git commit-tree identity
            "-c",
            "commit.gpgsign=false",
            "commit-tree",
            treeOid,
        };
        foreach (string parent in parentCommits)
        {
            args.Add("-p");
            args.Add(parent);
        }

        args.Add("-m");
        args.Add(message);

        var (exitCode, stdout, stderr) = RunGitCaptured(repoRoot, [.. args]);
        string? commitSha = FirstNonEmptyLine(stdout);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(commitSha))
        {
            return (exitCode == 0 ? 1 : exitCode, null, FormatGitFailure("git commit-tree", exitCode, stdout, stderr));
        }

        return (0, commitSha, string.Empty);
    }

    private static string? FirstNonEmptyLine(string? text) =>
        text?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string FormatGitFailure(string commandLabel, int exitCode, string stdout, string stderr)
    {
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return stderr.Trim();
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }

        return $"{commandLabel} exited with code {exitCode}.";
    }

    private static void DeleteFileBestEffort(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of temporary index file.
        }
    }

    private static List<string> NormalizeExplicitPaths(string repoRoot, IEnumerable<string> explicitFiles)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in explicitFiles)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
            string rel = NormalizePath(Path.GetRelativePath(repoRoot, full));
            if (rel.Length > 0 && !rel.StartsWith("../", StringComparison.Ordinal) && rel != "..")
            {
                normalized.Add(rel);
            }
        }

        return [.. normalized];
    }

    private static List<string> NormalizePaths(IEnumerable<string> rawPaths)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in rawPaths)
        {
            string rel = NormalizePath(raw);
            if (rel.Length > 0)
            {
                normalized.Add(rel);
            }
        }

        return [.. normalized];
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Centralizes Git diff and revision range resolution for <c>Cadence.Cli</c> and <c>Cadence.ToolingCore</c> quality gates.
/// </summary>
public static partial class GitRevisionScopeService
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
}

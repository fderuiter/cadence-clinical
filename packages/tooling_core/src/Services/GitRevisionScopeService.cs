using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

/// <summary>
/// Centralizes Git diff and revision range resolution for <c>Cadence.Cli</c> and <c>Cadence.ToolingCore</c> quality gates.
/// </summary>
public static class GitRevisionScopeService
{
    /// <summary>Default base ref used when <c>--base</c> is omitted.</summary>
    public const string DefaultBaseRef = "origin/main";

    /// <summary>Normalizes a nullable <c>--base</c> option to <see cref="DefaultBaseRef"/> when omitted.</summary>
    public static string NormalizeBaseRef(string? baseRef) =>
        string.IsNullOrWhiteSpace(baseRef) ? DefaultBaseRef : baseRef.Trim();

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
        string? pushBefore = null)
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

        string? headCommit = TryResolveCommitSha(repoRoot, "HEAD");

        if (!string.IsNullOrWhiteSpace(pushBefore))
        {
            string? pushBase = TryResolveValidPushBase(repoRoot, pushBefore, headCommit);
            if (pushBase is null)
            {
                var tracked = TryGetTrackedFiles(repoRoot) ?? [];
                return new GitRevisionScope(
                    GitScopeKind.TrackedFiles,
                    null,
                    headCommit,
                    effectiveBaseRef,
                    tracked,
                    UsedTrackedFallback: true);
            }

            var pushFiles = CollectChangedFilesSinceCommit(repoRoot, pushBase, includeDeletions: false, includeUntracked: false);
            return new GitRevisionScope(
                GitScopeKind.PushRange,
                pushBase,
                headCommit,
                effectiveBaseRef,
                pushFiles);
        }

        string? compareBase = TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
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
    /// ref, zero/tag/non-ancestor <paramref name="pushBefore"/>, or clean <c>HEAD == compareBase</c>).
    /// </summary>
    public static List<string>? TryGetChangedFilesSinceBase(
        string repoRoot,
        string baseRef = DefaultBaseRef,
        string? pushBefore = null)
    {
        var scope = ResolveFileScanScope(
            repoRoot,
            explicitFiles: null,
            preCommit: false,
            baseRef: baseRef,
            pushBefore: pushBefore);

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
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            _ = stderrTask.GetAwaiter().GetResult();
            process.WaitForExit();
            return process.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
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

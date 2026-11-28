using System;
using System.Globalization;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class GitRevisionScopeService
{
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

        string details = FormatGitFailure("git fetch", exitCode, stdout, stderr);
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
}

using System;
using System.Collections.Generic;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static partial class GitRevisionScopeService
{
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
}

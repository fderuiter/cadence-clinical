using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class GitRevisionScopeServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"git-scope-tests-{Guid.NewGuid():N}");

    public GitRevisionScopeServiceTests()
    {
        Directory.CreateDirectory(_dir);
        Git("init", "-q", "-b", "main");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_dir))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ResolveFileScanScope_OnCleanMain_FallsBackToTrackedFiles()
    {
        Write("apps/gateway/Program.cs", "class Program {}\n");
        Write("packages/ui/src/Button.vue", "<template><button /></template>\n");
        Commit("initial main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir);

        scope.Kind.Should().Be(GitScopeKind.TrackedFiles);
        scope.UsedTrackedFallback.Should().BeTrue();
        scope.Files.Should().Equal("apps/gateway/Program.cs", "packages/ui/src/Button.vue");
    }

    [Fact]
    public void ResolveFileScanScope_OnReleaseTargetBranch_DiffsAgainstCustomBaseRef()
    {
        Write("apps/gateway/Program.cs", "class Program {}\n");
        Commit("initial main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "-b", "release/1.0");
        Write("apps/release-only.cs", "class ReleaseOnly {}\n");
        Commit("release baseline");
        Git("update-ref", "refs/remotes/origin/release/1.0", "HEAD");

        Git("checkout", "-q", "-b", "hotfix/patch");
        Write("apps/hotfix.cs", "class Hotfix {}\n");
        Commit("hotfix commit");

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir, baseRef: "origin/release/1.0");

        scope.Kind.Should().Be(GitScopeKind.BranchDiff);
        scope.UsedTrackedFallback.Should().BeFalse();
        scope.Files.Should().Equal("apps/hotfix.cs");
    }

    [Fact]
    public void ResolveFileScanScope_OnMultiCommitPush_IncludesAllCommitsSincePushBefore()
    {
        Write("apps/base.cs", "class Base {}\n");
        Commit("base");
        string beforeSha = Git("rev-parse", "HEAD").Trim();

        Write("apps/commit1.cs", "class First {}\n");
        Commit("first pushed commit");

        Write("apps/commit2.vue", "<template><div /></template>\n");
        Commit("second pushed commit");

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir, pushBefore: beforeSha);

        scope.Kind.Should().Be(GitScopeKind.PushRange);
        scope.CompareBase.Should().Be(beforeSha);
        scope.Files.Should().Equal("apps/commit1.cs", "apps/commit2.vue");
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("lightweight-tag")]
    [InlineData("annotated-tag")]
    [InlineData("non-ancestor")]
    public void ResolveFileScanScope_WhenPushBeforeIsZeroTagOrNonAncestor_FallsBackToTrackedFiles(string mode)
    {
        Write("apps/one.cs", "class One {}\n");
        Commit("c1");
        Git("tag", "v1.0.0");
        Git("tag", "-a", "v1.0.1", "-m", "annotated release");
        string annotatedTagObject = Git("rev-parse", "v1.0.1").Trim();

        Git("checkout", "-q", "-b", "side");
        Write("apps/side.cs", "class Side {}\n");
        Commit("side commit");
        string nonAncestorSha = Git("rev-parse", "HEAD").Trim();

        Git("checkout", "-q", "main");
        Write("apps/two.cs", "class Two {}\n");
        Commit("c2");

        string pushBefore = mode switch
        {
            "0000000000000000000000000000000000000000" => mode,
            "lightweight-tag" => "v1.0.0",
            "annotated-tag" => annotatedTagObject,
            _ => nonAncestorSha,
        };

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir, pushBefore: pushBefore);

        scope.Kind.Should().Be(GitScopeKind.TrackedFiles);
        scope.UsedTrackedFallback.Should().BeTrue();
        scope.Files.Should().Equal("apps/one.cs", "apps/two.cs");
    }

    [Fact]
    public void ResolveFileScanScope_WhenBaseRefMissingInShallowClone_FallsBackToTrackedFiles()
    {
        Write("apps/tracked.cs", "class Tracked {}\n");
        Commit("only commit without origin/main");

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir, baseRef: "origin/main");

        scope.Kind.Should().Be(GitScopeKind.TrackedFiles);
        scope.UsedTrackedFallback.Should().BeTrue();
        scope.Files.Should().Equal("apps/tracked.cs");
    }

    [Fact]
    public void ResolveFileScanScope_InPreCommit_ReturnsStagedFilesOnly()
    {
        Write("apps/committed.cs", "class Committed {}\n");
        Commit("init");

        Write("apps/staged.cs", "class Staged {}\n");
        Git("add", "apps/staged.cs");
        Write("apps/unstaged.cs", "class Unstaged {}\n");

        var scope = GitRevisionScopeService.ResolveFileScanScope(_dir, preCommit: true);

        scope.Kind.Should().Be(GitScopeKind.Staged);
        scope.Files.Should().Equal("apps/staged.cs");
    }

    [Fact]
    public void FetchBaseRef_AndTryGetBehindCount_TrackUpstreamCommitsFromOrigin()
    {
        string remoteDir = Path.Combine(Path.GetTempPath(), $"git-scope-remote-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(remoteDir);
            RunGitIn(remoteDir, "init", "-q", "--bare", "-b", "main");

            Write("apps/base.cs", "class Base {}\n");
            Commit("initial main");
            Git("remote", "add", "origin", remoteDir);
            Git("push", "-q", "-u", "origin", "main");

            Git("checkout", "-q", "-b", "feature");
            Write("apps/feature.cs", "class Feature {}\n");
            Commit("feature commit");

            string peerDir = Path.Combine(Path.GetTempPath(), $"git-scope-peer-{Guid.NewGuid():N}");
            try
            {
                RunGitIn(Path.GetTempPath(), "clone", "-q", remoteDir, peerDir);
                File.WriteAllText(Path.Combine(peerDir, "apps", "upstream.cs"), "class Upstream {}\n");
                RunGitIn(peerDir, "add", "-A");
                RunGitIn(peerDir, "commit", "-q", "-m", "upstream commit");
                RunGitIn(peerDir, "push", "-q", "origin", "main");
            }
            finally
            {
                DeleteDirectorySafe(peerDir);
            }

            var fetch = GitRevisionScopeService.FetchBaseRef(_dir, "origin/main");
            fetch.Succeeded.Should().BeTrue(fetch.ErrorMessage);
            fetch.RemoteName.Should().Be("origin");
            fetch.RemoteBranch.Should().Be("main");

            GitRevisionScopeService.TryGetBehindCount(_dir, "origin/main").Should().Be(1);
        }
        finally
        {
            DeleteDirectorySafe(remoteDir);
        }
    }

    [Fact]
    public void PreviewMergeTree_SynthesizeMergedCommit_AndDetachedWorktree_HandleCleanAndConflictingBranches()
    {
        Write("apps/shared.cs", "class Shared { public const int A = 1; }\n");
        Commit("base");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "-b", "feature");
        Write("apps/feature.cs", "class Feature {}\n");
        Commit("feature work");

        Git("checkout", "-q", "main");
        Write("apps/upstream.cs", "class Upstream {}\n");
        Commit("upstream work");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "feature");
        Write("apps/uncommitted.txt", "dirty checkout\n");

        var preview = GitRevisionScopeService.PreviewMergeTree(_dir, "origin/main");
        preview.Succeeded.Should().BeTrue(preview.ErrorMessage);
        preview.HasConflicts.Should().BeFalse();
        preview.TreeOid.Should().HaveLength(40);

        var synthesized = GitRevisionScopeService.SynthesizeMergedCommit(_dir, "origin/main");
        synthesized.MergeTree.Succeeded.Should().BeTrue(synthesized.MergeTree.ErrorMessage);
        synthesized.MergeTree.BehindCount.Should().Be(1);
        synthesized.MergeCommitSha.Should().HaveLength(40);

        GitRevisionScopeService.TryCreateDetachedWorktree(_dir, synthesized.MergeCommitSha!, out var worktree, out string? wtError)
            .Should().BeTrue(wtError);
        string worktreePath = worktree!.WorktreePath;
        using (worktree)
        {
            worktreePath.Should().StartWith(Path.GetTempPath());
            File.Exists(Path.Combine(worktreePath, "apps", "feature.cs")).Should().BeTrue();
            File.Exists(Path.Combine(worktreePath, "apps", "upstream.cs")).Should().BeTrue();
            File.Exists(Path.Combine(worktreePath, "apps", "uncommitted.txt")).Should().BeFalse("detached worktree checks out synthesized commit only");
        }

        Directory.Exists(worktreePath).Should().BeFalse("Dispose removes temporary detached worktree");
        File.Exists(Path.Combine(_dir, "apps", "uncommitted.txt")).Should().BeTrue("caller checkout must not be mutated");
        File.Delete(Path.Combine(_dir, "apps", "uncommitted.txt"));

        // Now introduce a textual conflict on apps/shared.cs
        Git("checkout", "-q", "main");
        Write("apps/shared.cs", "class Shared { public const int A = 100; }\n");
        Commit("upstream conflict");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "feature");
        Write("apps/shared.cs", "class Shared { public const int A = 200; }\n");
        Commit("feature conflict");

        var conflictPreview = GitRevisionScopeService.PreviewMergeTree(_dir, "origin/main");
        conflictPreview.Succeeded.Should().BeFalse();
        conflictPreview.HasConflicts.Should().BeTrue();
        conflictPreview.ConflictingFiles.Should().Equal("apps/shared.cs");
    }

    [Fact]
    public void ResolveEventScope_AndResolveFileScanScope_DistinguishWorkflowDispatchVersionTagAndNewReleaseBranchPushes()
    {
        Write("apps/base.cs", "class Base {}\n");
        Commit("initial main");
        string branchPoint = Git("rev-parse", "HEAD").Trim();
        Git("tag", "v1.0.1");

        Git("checkout", "-q", "-b", "release/1.2");
        Write("apps/release-patch.cs", "class ReleasePatch {}\n");
        Commit("release patch");
        string releaseHead = Git("rev-parse", "HEAD").Trim();

        // Advance origin/main independently so HEAD and origin/main diverge.
        Git("checkout", "-q", "main");
        Write("apps/main-diverged.cs", "class MainDiverged {}\n");
        Commit("diverged main commit");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "v1.0.1");
        string tagHead = Git("rev-parse", "HEAD").Trim();

        var tagPushScope = GitRevisionScopeService.ResolveEventScope(
            _dir,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/tags/v1.0.1");
        tagPushScope.Kind.Should().Be(GitEventScopeKind.TargetRef);
        tagPushScope.CompareBase.Should().Be(tagHead);

        var tagDispatchScope = GitRevisionScopeService.ResolveEventScope(
            _dir,
            eventName: "workflow_dispatch",
            gitRef: "refs/tags/v1.0.1");
        tagDispatchScope.Kind.Should().Be(GitEventScopeKind.TargetRef);
        tagDispatchScope.CompareBase.Should().Be(tagHead);

        var releaseDispatchScope = GitRevisionScopeService.ResolveEventScope(
            _dir,
            environment: key => key switch
            {
                GitRevisionScopeService.GitHubEventNameVariable => "workflow_dispatch",
                GitRevisionScopeService.GitHubRefVariable => "refs/heads/release/1.2",
                _ => null,
            });
        releaseDispatchScope.Kind.Should().Be(GitEventScopeKind.TargetRef);

        var unprotectedDispatch = GitRevisionScopeService.ResolveEventScope(
            _dir,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/feature/unapproved");
        unprotectedDispatch.Kind.Should().Be(GitEventScopeKind.UnprotectedTargetRef);
        unprotectedDispatch.CompareBase.Should().BeNull();

        // New release/* branch push with 40-zero BEFORE_SHA scopes strictly to merge-base(origin/main, HEAD)..HEAD.
        Git("checkout", "-q", "release/1.2");
        var newReleaseBranchScope = GitRevisionScopeService.ResolveEventScope(
            _dir,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/heads/release/1.2");
        newReleaseBranchScope.Kind.Should().Be(GitEventScopeKind.NewBranchPush);
        newReleaseBranchScope.CompareBase.Should().Be(branchPoint);
        newReleaseBranchScope.HeadCommit.Should().Be(releaseHead);

        var newReleaseFileScope = GitRevisionScopeService.ResolveFileScanScope(
            _dir,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/heads/release/1.2");
        newReleaseFileScope.Kind.Should().Be(GitScopeKind.BranchDiff);
        newReleaseFileScope.CompareBase.Should().Be(branchPoint);
        newReleaseFileScope.Files.Should().Equal("apps/release-patch.cs");
    }

    [Fact]
    public void MergeReadiness_And_BranchFreshness_SkipOriginMainOnProtectedDispatchAndTagPushes_WhileEnforcingPullRequestsAndLocalRuns()
    {
        Write("apps/shared.cs", "class Shared { public const int Value = 1; }\n");
        Commit("base commit");

        Git("checkout", "-q", "-b", "release/1.0");
        Write("apps/shared.cs", "class Shared { public const int Value = 2; }\n");
        Commit("release commit");
        Git("tag", "v1.0.1");

        // Advance origin/main with a conflicting modification to apps/shared.cs.
        Git("checkout", "-q", "main");
        Write("apps/shared.cs", "class Shared { public const int Value = 99; }\n");
        Commit("diverged conflicting main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "v1.0.1");

        var mergeReadiness = QualityGates.All.Single(g => g.Name == "merge-readiness");
        var branchFreshness = QualityGates.All.Single(g => g.Name == "branch-freshness");

        GateContext ContextFor(string? eventName = null, string? gitRef = null, string? pushBefore = null, string? baseRef = null) =>
            new(_dir, BaseRef: baseRef, PushBefore: pushBefore, EventName: eventName, GitRef: gitRef);

        // Protected workflow_dispatch runs and 40-zero v* tag pushes skip origin/main checks even when origin/main conflicts.
        mergeReadiness.Run(ContextFor("workflow_dispatch", "refs/tags/v1.0.1")).Should().BeTrue();
        branchFreshness.Run(ContextFor("workflow_dispatch", "refs/tags/v1.0.1")).Should().BeTrue();
        mergeReadiness.Run(ContextFor("workflow_dispatch", "refs/heads/release/1.0")).Should().BeTrue();
        branchFreshness.Run(ContextFor("workflow_dispatch", "refs/heads/release/1.0")).Should().BeTrue();
        mergeReadiness.Run(ContextFor("workflow_dispatch", "refs/heads/main")).Should().BeTrue();
        branchFreshness.Run(ContextFor("workflow_dispatch", "refs/heads/main")).Should().BeTrue();
        mergeReadiness.Run(ContextFor("push", "refs/tags/v1.0.1", GitRevisionScopeService.AllZeroSha)).Should().BeTrue();
        branchFreshness.Run(ContextFor("push", "refs/tags/v1.0.1", GitRevisionScopeService.AllZeroSha)).Should().BeTrue();

        // Unprotected workflow_dispatch refs fail closed.
        mergeReadiness.Run(ContextFor("workflow_dispatch", "refs/heads/feature/x")).Should().BeFalse();
        branchFreshness.Run(ContextFor("workflow_dispatch", "refs/heads/feature/x")).Should().BeFalse();

        // Local runs, pull_request, and merge_group preserve strict origin/<base> enforcement.
        mergeReadiness.Run(ContextFor()).Should().BeFalse();
        branchFreshness.Run(ContextFor()).Should().BeFalse();
        mergeReadiness.Run(ContextFor("pull_request", "refs/pull/42/merge", baseRef: "origin/main")).Should().BeFalse();
        branchFreshness.Run(ContextFor("pull_request", "refs/pull/42/merge", baseRef: "origin/main")).Should().BeFalse();
        mergeReadiness.Run(ContextFor("merge_group", "refs/heads/gh-readonly-queue/main/pr-42", baseRef: "origin/main")).Should().BeFalse();
        branchFreshness.Run(ContextFor("merge_group", "refs/heads/gh-readonly-queue/main/pr-42", baseRef: "origin/main")).Should().BeFalse();
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(path, recursive: true);
    }

    private static void RunGitIn(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new List<string> { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
    }

    private void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new List<string> { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
        return output;
    }
}

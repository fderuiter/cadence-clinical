using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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

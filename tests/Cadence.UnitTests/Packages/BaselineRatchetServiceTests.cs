using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The <c>baseline-ratchet</c> gate (#5318): a ratchet baseline may only shrink unless a commit on the branch carries a
/// <c>Baseline-Growth</c> trailer, or, in the pre-commit hook, <c>CADENCE_BASELINE_GROWTH</c> names the reason.
/// </summary>
public sealed class BaselineRatchetServiceTests : IDisposable
{
    private const string Baseline = "tests/Arch/ArchitectureBaselines/kernel-boundaries.baseline.txt";
    private const string Header = "# The list may only shrink.\n";

    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"baseline-ratchet-tests-{Guid.NewGuid():N}");

    public BaselineRatchetServiceTests()
    {
        Directory.CreateDirectory(_dir);
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\n");
        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\n");
        Write("src/Program.cs", "class P {}\n");
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("checkout", "-q", "-b", "feature");
    }

    public void Dispose()
    {
        // git marks its object files read-only.
        foreach (var file in new DirectoryInfo(_dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt", true)]
    [InlineData(".deid-baseline.txt", true)]
    [InlineData(".secrets.baseline", true)]
    [InlineData(".cadence/snapshots/baseline-test.tar.gz", false)]
    [InlineData("docs/baseline.md", false)]
    public void IsRatchetBaseline_FindsTheBaselineFiles(string path, bool expected)
    {
        BaselineRatchetService.IsRatchetBaseline(path).Should().Be(expected);
    }

    [Fact]
    public void Additions_CountsEntriesAsAMultiset_AndIgnoresOrder()
    {
        BaselineRatchetService.Additions(["a", "b", "c"], ["c", "a", "b"]).Should().BeEmpty();
        BaselineRatchetService.Additions(["a", "b"], ["a"]).Should().BeEmpty();
        BaselineRatchetService.Additions(["a"], ["a", "a", "b"]).Should().Equal("a", "b");
    }

    [Fact]
    public void Entries_SkipsCommentsAndBlankLines_AndReadsSecretsBaselineResults()
    {
        BaselineRatchetService.Entries("x.baseline.txt", "# header\n\nK|A|B\r\n").Should().Equal("K|A|B");
        BaselineRatchetService.Entries(".secrets.baseline",
            """{ "version": "1.5.0", "results": { "a.py": [ { "type": "Secret Keyword", "hashed_secret": "abc", "line_number": 3 } ] } }""")
            .Should().Equal("a.py|Secret Keyword|abc");
    }

    [Fact]
    public void Growth_WithoutTrailer_Fails()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit("feat: add C");

        Check().Should().Be(1);
    }

    [Fact]
    public void Growth_InUncommittedWorkingTree_Fails()
    {
        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");

        Check().Should().Be(1);
    }

    [Fact]
    public void Growth_WithTrailerOnABranchCommit_Passes()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - C is ported from the legacy service and burned down in #9999");
        Write("src/Program.cs", "class P { }\n");
        Commit("fix: later commit without a trailer");

        Check().Should().Be(0);
    }

    [Fact]
    public void Growth_OnSecondCommitAfterAuthorizedFirstCommit_Fails()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - C is ported from the legacy service");

        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\nK-XS-1|App|D\n");
        Commit("feat: sneak in D without trailer");

        Check().Should().Be(1);
    }

    [Fact]
    public void Growth_InUnmentionedSecondBaselineFile_Fails_UntilBothFilesHaveTrailers()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");
        Commit($"feat: grow two baselines\n\nBaseline-Growth: {Baseline} - authorized kernel boundary");

        Check().Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            $"feat: grow two baselines\n\nBaseline-Growth: {Baseline} - authorized kernel boundary\nBaseline-Growth: .deid-baseline.txt - legacy fixture pending cleanup");

        Check().Should().Be(0);
    }

    [Fact]
    public void Growth_WithLegacyPathlessTrailer_Fails()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit("feat: add C\n\nBaseline-Growth: legacy format without path");

        Check().Should().Be(1);
    }

    [Fact]
    public void Growth_InDirtyWorkingTreeAfterAuthorizedCommit_Fails()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - authorized");

        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/z.cs|urls|0003\n");

        Check().Should().Be(1);
    }

    [Fact]
    public void PreCommit_ComparesStagedCopyAgainstHead_NotBranchMergeBase()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - authorized on commit 1");

        Write("src/Program.cs", "class P { static void Main() {} }\n");
        Git("add", "src/Program.cs");
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);

        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");
        Git("add", ".deid-baseline.txt");
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(1);
    }

    [Fact]
    public void PushBefore_EvaluatesAllCommitsInPushRange_AndHandlesZeroShaFallback()
    {
        string pushBefore = CaptureGit("rev-parse", "HEAD").Trim();

        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - authorized");

        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");
        Commit("feat: unauthorized growth on second pushed commit");

        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment, pushBefore: pushBefore).Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            "feat: authorized growth on second pushed commit\n\nBaseline-Growth: .deid-baseline.txt - legacy fixture");

        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment, pushBefore: pushBefore).Should().Be(0);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment,
            pushBefore: "0000000000000000000000000000000000000000").Should().Be(0);
    }

    [Fact]
    public void TrailerInTheBodyRatherThanTheTrailerBlock_DoesNotCount()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} - not a trailer\n\nThis paragraph ends the message.");

        Check().Should().Be(1);
    }

    [Fact]
    public void Shrink_Passes()
    {
        Write(Baseline, Header + "K-XS-1|App|A\n");
        Write(".deid-baseline.txt", "# deid\n");
        Commit("refactor: pay down B");

        Check().Should().Be(0);
    }

    [Fact]
    public void ReorderOnly_AndHeaderChanges_Pass()
    {
        Write(Baseline, "# A new header.\n# Regenerate with X.\nK-XS-1|App|B\nK-XS-1|App|A\n");
        Commit("chore: reorder");

        Check().Should().Be(0);
    }

    [Fact]
    public void NewBaselineFile_WithEntries_IsGrowth()
    {
        Write("tests/Arch/ArchitectureBaselines/kernel-rule-coverage.baseline.txt", "# coverage\nK-TIME-3\n");
        Commit("test: add coverage baseline");

        Check().Should().Be(1);
    }

    [Fact]
    public void PreCommit_ChecksTheStagedCopy_AndHonoursTheEnvironmentVariable()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Git("add", "-A");

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: true,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? "ported from legacy" : null).Should().Be(0);

        // The variable is a pre-commit stand-in for the trailer; the branch check ignores it.
        BaselineRatchetService.Check(_dir, preCommit: false,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? "ported from legacy" : null).Should().Be(1);
    }

    [Fact]
    public void PreCommit_IgnoresUnstagedGrowth()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
    }

    [Fact]
    public void MissingBaseRef_SkipsLocally_AndFailsInCi()
    {
        Git("update-ref", "-d", "refs/remotes/origin/main");

        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment).Should().Be(0);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: name => name == "CI" ? "true" : null).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: name => name == "GITHUB_ACTIONS" ? "true" : null).Should().Be(1);
    }

    private int Check() => BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment);

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

    private void Git(params string[] args) => _ = CaptureGit(args);

    private string CaptureGit(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
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

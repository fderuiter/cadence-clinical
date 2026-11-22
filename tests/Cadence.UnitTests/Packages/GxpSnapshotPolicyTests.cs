using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;
using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The branch-safe GxP snapshot policy (ADR-2532, #5151): feature branches validate their requirement tags without
/// writing or committing the RTM and IQ/OQ/PQ report, and only snapshot mode may change them.
/// </summary>
public sealed class GxpSnapshotPolicyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gxp-snapshot-tests-{Guid.NewGuid():N}");

    public GxpSnapshotPolicyTests() => Directory.CreateDirectory(_dir);

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
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSnapshotMode_ReadsTheEnvironmentVariable(string? value, bool expected)
    {
        GxpSnapshotPolicy.IsSnapshotMode(name => name == GxpSnapshotPolicy.SnapshotEnvironmentVariable ? value : null)
            .Should().Be(expected);
    }

    [Fact]
    public void SnapshotPaths_FindsTheTwoReportsAndTheRunReports_Only()
    {
        GxpSnapshotPolicy.SnapshotPaths(
        [
            "apps/execution/src/Program.cs",
            "docs/SDLC/Requirements_Traceability_Matrix.md",
            "docs\\SDLC\\IQ_OQ_PQ_Execution_Report.md",
            "docs/SDLC/runs/2026-10-16/report.md",
            "docs/SDLC/01_Product_Requirements_Document_PRD.md",
        ]).Should().Equal(
            "docs/SDLC/Requirements_Traceability_Matrix.md",
            "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
            "docs/SDLC/runs/2026-10-16/report.md");
    }

    [Fact]
    public void Evaluate_RefusesAReportChangeOnABranch_AndAdmitsItInSnapshotMode()
    {
        string[] changed = ["tests/X.cs", "docs/SDLC/Requirements_Traceability_Matrix.md"];

        GxpSnapshotGuardService.Evaluate(changed, snapshotMode: false, "staged").Should().Be(1);
        GxpSnapshotGuardService.Evaluate(changed, snapshotMode: true, "staged").Should().Be(0);
        GxpSnapshotGuardService.Evaluate(["tests/X.cs", "docs/SDLC/01_Product_Requirements_Document_PRD.md"], snapshotMode: false, "staged")
            .Should().Be(0, "requirement documents are source, not snapshots");
    }

    [Fact]
    public void BranchValidation_AcceptsANewTagWithoutRegeneratingTheSnapshot_ButSnapshotModeDoesNot()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);

        WriteTaggedTest("Second", "PRD-AAA-002");

        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: false)
            .Should().Be(0, "a feature branch's new tag is expected to differ from the baseline's snapshot");
        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: true)
            .Should().Be(1, "a snapshot must match the tags it was taken from");
    }

    [Fact]
    public void BranchValidation_StillFailsOnUnknownIdsAndBrokenStamps()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);

        WriteTaggedTest("Unknown", "PRD-ZZZ-999");
        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: false).Should().Be(1);
        File.Delete(Path.Combine(_dir, "tests", "Unknown.cs"));

        string rtm = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        File.WriteAllText(rtm, File.ReadAllText(rtm).Replace("# ", "# Edited ", StringComparison.Ordinal));
        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: false).Should().Be(1);
    }

    /// <summary>
    /// The regression the issue asks for: two branches that each add a differently tagged test pass branch validation
    /// and the snapshot guard without touching the reports, and merge one after the other without a conflict. The
    /// integration owner's snapshot on the merged result then lists both tests.
    /// </summary>
    [Fact]
    public void TwoBranchesAddingTaggedTests_MergeWithoutConflict_AndNeitherCarriesTheReports()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "baseline snapshot");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        foreach (var (branch, test, requirement) in new[] { ("a", "Second", "PRD-AAA-002"), ("b", "Third", "PRD-AAA-003") })
        {
            Git("checkout", "-q", "-b", branch, "main");
            WriteTaggedTest(test, requirement);
            Git("add", "-A");
            Git("commit", "-q", "-m", $"add {test}");

            RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: false).Should().Be(0);
            GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false).Should().Be(0);
            Git("diff", "--name-only", "main", branch).Should().NotContain("Requirements_Traceability_Matrix")
                .And.NotContain("IQ_OQ_PQ_Execution_Report");
        }

        Git("checkout", "-q", "main");
        Git("merge", "-q", "--no-edit", "a");
        Git("merge", "-q", "--no-edit", "b");

        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir, requireCurrentSnapshot: true).Should().Be(0);
        File.ReadAllText(Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md"))
            .Should().Contain("`Second`").And.Contain("`Third`");
    }

    [Fact]
    public void Check_RefusesAStagedReportUnlessInSnapshotMode()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "baseline snapshot");

        WriteTaggedTest("Second", "PRD-AAA-002");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("add", "-A");

        GxpSnapshotGuardService.Check(_dir, preCommit: true, snapshotMode: false).Should().Be(1);
        GxpSnapshotGuardService.Check(_dir, preCommit: true, snapshotMode: true).Should().Be(0);
    }

    /// <summary>
    /// Regression: merging main into a branch after the integration owner took a snapshot staged main's new reports, and
    /// the guard refused the merge commit. A report identical to main's is the baseline's, not the branch's.
    /// </summary>
    [Fact]
    public void Check_AdmitsMainsSnapshotMergedIntoABranch_ButNotABranchsOwnEdit()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "baseline snapshot");

        Git("checkout", "-q", "-b", "feature");
        File.WriteAllText(Path.Combine(_dir, "tests", "Notes.txt"), "branch work\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "branch work");

        Git("checkout", "-q", "main");
        WriteTaggedTest("Second", "PRD-AAA-002");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("add", "-A");
        Git("commit", "-q", "-m", "new snapshot");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "feature");
        Git("merge", "-q", "--no-commit", "--no-ff", "main");
        GxpSnapshotGuardService.Check(_dir, preCommit: true, snapshotMode: false).Should().Be(0, "the staged reports are main's");
        Git("commit", "-q", "--no-edit");
        GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false).Should().Be(0);

        string rtm = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        File.AppendAllText(rtm, "\nedited on the branch\n");
        Git("add", "-A");
        GxpSnapshotGuardService.Check(_dir, preCommit: true, snapshotMode: false).Should().Be(1);
        GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false).Should().Be(1);
    }

    [Fact]
    public void Check_WithPushBefore_RefusesUnauthorizedSnapshotPushOnMainAndRelease_AcrossMultipleCommits()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "baseline snapshot");
        string beforeMainPush = Git("rev-parse", "HEAD").Trim();

        // Commit 1 edits the snapshot directly on main; Commit 2 touches an unrelated file.
        string rtm = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        File.AppendAllText(rtm, "\nunauthorized edit on main\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "unauthorized rtm edit");

        File.WriteAllText(Path.Combine(_dir, "tests", "Notes.txt"), "clean commit 2\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "clean follow-up");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        // Without pushBefore, HEAD == origin/main would see no branch diff, whereas pushBefore catches Commit 1 on main!
        GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false, pushBefore: beforeMainPush).Should().Be(1);

        // Repeat on release/1.0 push range.
        Git("checkout", "-q", "-b", "release/1.0");
        string beforeReleasePush = Git("rev-parse", "HEAD").Trim();
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC", "runs", "run-1"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "runs", "run-1", "summary.md"), "# Unauthorized run\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "unauthorized run artifact on release");

        GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false, pushBefore: beforeReleasePush).Should().Be(1);
    }

    private void WriteRequirements()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "prd.md"),
            "#### PRD-AAA-001: Widget\n#### PRD-AAA-002: Gadget\n#### PRD-AAA-003: Gizmo\n");
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
    }

    private void WriteTaggedTest(string name, string requirementId) =>
        File.WriteAllText(Path.Combine(_dir, "tests", $"{name}.cs"), string.Join("\n",
            "namespace Sample;",
            $"public class {name}Tests",
            "{",
            "    [Fact]",
            $"    [GxPRequirement(\"{requirementId}\")]",
            $"    public void {name}() {{ }}",
            "}"));

    private string Git(params string[] args)
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

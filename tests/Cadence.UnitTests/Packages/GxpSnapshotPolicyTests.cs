using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.ToolingCore.Services;

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

    [Fact]
    public void Check_ExitsZeroOnProtectedWorkflowDispatchAndZeroShaVersionTagPush_WhenOriginMainDiverges_AndRejectsDirtyOrNewReleaseEdits()
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "v1.0.1 snapshot");
        Git("tag", "v1.0.1");

        // Advance main and origin/main with a newer snapshot so v1.0.1 diverges from origin/main on docs/SDLC/*.md.
        WriteTaggedTest("Second", "PRD-AAA-002");
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        Git("add", "-A");
        Git("commit", "-q", "-m", "newer main snapshot");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "v1.0.1");

        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/tags/v1.0.1").Should().Be(0);

        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            eventName: "workflow_dispatch",
            gitRef: "refs/tags/v1.0.1").Should().Be(0);

        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/release/1.0").Should().Be(0);

        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/main").Should().Be(0);

        // Unprotected workflow_dispatch refs fail closed.
        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/feature").Should().Be(1);

        // Uncommitted working-tree drift at HEAD still fails on protected dispatch and tag push.
        string rtm = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        File.AppendAllText(rtm, "\nuncommitted edit\n");
        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/tags/v1.0.1").Should().Be(1);
        Git("checkout", "-q", "--", "docs/SDLC/Requirements_Traceability_Matrix.md");

        // A 40-zero push to a new refs/heads/release/* branch evaluates merge-base(origin/main, HEAD)..HEAD.
        Git("checkout", "-q", "-b", "release/1.0", "v1.0.1");
        File.AppendAllText(rtm, "\nunauthorized snapshot edit on new release branch\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "unauthorized release snapshot edit");
        GxpSnapshotGuardService.Check(
            _dir,
            preCommit: false,
            snapshotMode: false,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/heads/release/1.0").Should().Be(1);
    }

    [Fact]
    public void SyncCompliance_SnapshotCommit_AppendsRefreshedTrailer_AndPrintsTrailerGuidanceWhenNotCommitting()
    {
        SetupFullEvidenceRepository(includeBrowserEvidence: true);
        Git("init", "-q", "-b", "main");
        Git("add", "-A");
        Git("commit", "-q", "-m", "initial commit");
        string initialHead = Git("rev-parse", "HEAD").Trim();

        TextWriter originalOut = Console.Out;
        string stdout;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            int stageOnlyExit = SyncGxPService.SyncCompliance(
                snapshot: true,
                commit: false,
                repoRoot: _dir,
                resultsDir: "TestResults/gxp");
            stageOnlyExit.Should().Be(0);
            stdout = writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        stdout.Should().Contain(GxpSnapshotPolicy.SnapshotTrailer);

        int commitExit = SyncGxPService.SyncCompliance(
            snapshot: true,
            commit: true,
            repoRoot: _dir,
            resultsDir: "TestResults/gxp");
        commitExit.Should().Be(0);

        string snapshotHead = Git("rev-parse", "HEAD").Trim();
        snapshotHead.Should().NotBe(initialHead);
        string commitMessage = Git("log", "-1", "--format=%B");
        commitMessage.Should().Contain(GxpSnapshotPolicy.SnapshotCommitSubject)
            .And.Contain(GxpSnapshotPolicy.SnapshotTrailer);
        GitRevisionScopeService.HasCommitTrailer(
            _dir,
            $"{initialHead}..{snapshotHead}",
            GxpSnapshotPolicy.SnapshotTrailerKey,
            GxpSnapshotPolicy.SnapshotTrailerValue).Should().BeTrue();
    }

    [Fact]
    public void NonOwnerAndForkPullRequests_TouchingSdlc_ExitOneEvenWithRefreshedSnapshotTrailer()
    {
        string remoteDir = CreateBareOriginRemote();
        try
        {
            SetupFullEvidenceRepository(includeBrowserEvidence: true);
            Git("init", "-q", "-b", "main");
            Git("remote", "add", "origin", remoteDir);
            SyncGxPService.SyncCompliance(snapshot: true, commit: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);
            Git("add", "-A");
            Git("commit", "-q", "-m", "baseline snapshot");
            Git("push", "-q", "-u", "origin", "main");

            Git("checkout", "-q", "-b", "pr-snapshot");
            WriteTaggedTest("Second", "PRD-AAA-002");
            WriteEvidenceArtifacts(includeBrowserEvidence: true, includeSecondTest: true);
            SyncGxPService.SyncCompliance(snapshot: true, commit: true, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);

            // 1. Fork pull request (even if login matches owner name on fork, head repo != github.repository).
            EvaluateWorkflowSnapshotAuthorization(
                eventName: "pull_request",
                refName: "refs/pull/99/merge",
                baseRef: "main",
                beforeSha: "",
                repoFullName: "fderuiter/cadence-clinical",
                repoOwner: "fderuiter",
                prHeadRepo: "attacker/cadence-clinical",
                prUserLogin: "fderuiter").Should().BeFalse();
            GxpSnapshotGuardService.AuthorizeCiSnapshot(
                _dir,
                eventName: "pull_request",
                gitRef: "refs/pull/99/merge",
                baseRef: "origin/main",
                pushBefore: null,
                repositoryFullName: "fderuiter/cadence-clinical",
                repositoryOwner: "fderuiter",
                pullRequestHeadRepoFullName: "attacker/cadence-clinical",
                pullRequestUserLogin: "fderuiter").Should().BeFalse();
            GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false, baseRef: "origin/main", eventName: "pull_request", gitRef: "refs/pull/99/merge")
                .Should().Be(1);

            // 2. Non-owner same-repository pull request with Gxp-Snapshot: refreshed trailer.
            EvaluateWorkflowSnapshotAuthorization(
                eventName: "pull_request",
                refName: "refs/pull/100/merge",
                baseRef: "main",
                beforeSha: "",
                repoFullName: "fderuiter/cadence-clinical",
                repoOwner: "fderuiter",
                prHeadRepo: "fderuiter/cadence-clinical",
                prUserLogin: "external-collaborator").Should().BeFalse();
            GxpSnapshotGuardService.AuthorizeCiSnapshot(
                _dir,
                eventName: "pull_request",
                gitRef: "refs/pull/100/merge",
                baseRef: "origin/main",
                pushBefore: null,
                repositoryFullName: "fderuiter/cadence-clinical",
                repositoryOwner: "fderuiter",
                pullRequestHeadRepoFullName: "fderuiter/cadence-clinical",
                pullRequestUserLogin: "external-collaborator").Should().BeFalse();
            GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false, baseRef: "origin/main", eventName: "pull_request", gitRef: "refs/pull/100/merge")
                .Should().Be(1);

            // 3. Owner same-repository pull request WITHOUT Gxp-Snapshot: refreshed trailer.
            Git("commit", "--amend", "-q", "-m", "docs(rtm): snapshot without trailer");
            EvaluateWorkflowSnapshotAuthorization(
                eventName: "pull_request",
                refName: "refs/pull/101/merge",
                baseRef: "main",
                beforeSha: "",
                repoFullName: "fderuiter/cadence-clinical",
                repoOwner: "fderuiter",
                prHeadRepo: "fderuiter/cadence-clinical",
                prUserLogin: "fderuiter").Should().BeFalse();
            GxpSnapshotGuardService.Check(_dir, preCommit: false, snapshotMode: false, baseRef: "origin/main", eventName: "pull_request", gitRef: "refs/pull/101/merge")
                .Should().Be(1);
        }
        finally
        {
            DeleteDirectorySafe(remoteDir);
        }
    }

    [Fact]
    public void OwnerPrMergeGroupAndPostMergePushes_WithRefreshedTrailer_ExportSnapshotMode_VerifyBrowserAndDbEvidence_AndEnforceGxpDiff()
    {
        string remoteDir = CreateBareOriginRemote();
        try
        {
            SetupFullEvidenceRepository(includeBrowserEvidence: true);
            Git("init", "-q", "-b", "main");
            Git("remote", "add", "origin", remoteDir);
            SyncGxPService.SyncCompliance(snapshot: true, commit: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);
            Git("add", "-A");
            Git("commit", "-q", "-m", "baseline snapshot");
            Git("push", "-q", "-u", "origin", "main");
            string beforeMainPush = Git("rev-parse", "HEAD").Trim();

            Git("checkout", "-q", "-b", "owner-snapshot");
            WriteTaggedTest("Second", "PRD-AAA-002");
            WriteEvidenceArtifacts(includeBrowserEvidence: true, includeSecondTest: true);
            SyncGxPService.SyncCompliance(snapshot: true, commit: true, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);

            // Owner same-repo PR exports CADENCE_GXP_SNAPSHOT=1.
            bool ownerPrAuthorized = EvaluateWorkflowSnapshotAuthorization(
                eventName: "pull_request",
                refName: "refs/pull/102/merge",
                baseRef: "main",
                beforeSha: "",
                repoFullName: "fderuiter/cadence-clinical",
                repoOwner: "fderuiter",
                prHeadRepo: "fderuiter/cadence-clinical",
                prUserLogin: "fderuiter");
            ownerPrAuthorized.Should().BeTrue();
            GxpSnapshotGuardService.AuthorizeCiSnapshot(
                _dir,
                eventName: "pull_request",
                gitRef: "refs/pull/102/merge",
                baseRef: "origin/main",
                pushBefore: null,
                repositoryFullName: "fderuiter/cadence-clinical",
                repositoryOwner: "fderuiter",
                pullRequestHeadRepoFullName: "fderuiter/cadence-clinical",
                pullRequestUserLogin: "fderuiter").Should().BeTrue();

            // merge_group exports CADENCE_GXP_SNAPSHOT=1 without pull_request actor fields.
            EvaluateWorkflowSnapshotAuthorization(
                eventName: "merge_group",
                refName: "refs/heads/gh-readonly-queue/main/pr-102",
                baseRef: "main",
                beforeSha: "",
                repoFullName: "fderuiter/cadence-clinical",
                repoOwner: "fderuiter",
                prHeadRepo: "",
                prUserLogin: "").Should().BeTrue();

            string? prevSnapshotEnv = Environment.GetEnvironmentVariable(GxpSnapshotPolicy.SnapshotEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(GxpSnapshotPolicy.SnapshotEnvironmentVariable, "1");

                // Missing browser TRX evidence fails compliance sync in snapshot mode!
                Directory.Delete(Path.Combine(_dir, "TestResults", "gxp", "browser"), recursive: true);
                SyncGxPService.SyncCompliance(snapshot: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(1);

                // Restore browser TRX evidence, then delete database fixture metadata -> fails compliance sync!
                WriteEvidenceArtifacts(includeBrowserEvidence: true, includeSecondTest: true);
                foreach (string dbFile in Directory.EnumerateFiles(Path.Combine(_dir, "TestResults", "gxp"), "*.database.txt", SearchOption.AllDirectories))
                {
                    File.Delete(dbFile);
                }

                SyncGxPService.SyncCompliance(snapshot: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(1);

                // With both PostgreSQL and browser TRX evidence present, compliance sync regenerates docs/SDLC and gxp-snapshot diff passes (0)!
                WriteEvidenceArtifacts(includeBrowserEvidence: true, includeSecondTest: true);
                SyncGxPService.SyncCompliance(snapshot: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);
                GxpSnapshotGuardService.Check(
                    _dir,
                    preCommit: false,
                    snapshotMode: true,
                    baseRef: "origin/main",
                    eventName: "pull_request",
                    gitRef: "refs/pull/102/merge").Should().Be(0);

                // Fast-forward main and verify post-merge push (ancestor BEFORE_SHA) and 40-zero new release/* push both authorize and pass!
                Git("checkout", "-q", "main");
                Git("merge", "-q", "--ff-only", "owner-snapshot");
                EvaluateWorkflowSnapshotAuthorization(
                    eventName: "push",
                    refName: "refs/heads/main",
                    baseRef: "main",
                    beforeSha: beforeMainPush,
                    repoFullName: "fderuiter/cadence-clinical",
                    repoOwner: "fderuiter",
                    prHeadRepo: "",
                    prUserLogin: "").Should().BeTrue();
                GxpSnapshotGuardService.Check(
                    _dir,
                    preCommit: false,
                    snapshotMode: true,
                    pushBefore: beforeMainPush,
                    eventName: "push",
                    gitRef: "refs/heads/main").Should().Be(0);

                Git("checkout", "-q", "-b", "release/1.0");
                EvaluateWorkflowSnapshotAuthorization(
                    eventName: "push",
                    refName: "refs/heads/release/1.0",
                    baseRef: "main",
                    beforeSha: GitRevisionScopeService.AllZeroSha,
                    repoFullName: "fderuiter/cadence-clinical",
                    repoOwner: "fderuiter",
                    prHeadRepo: "",
                    prUserLogin: "").Should().BeTrue();
                GxpSnapshotGuardService.Check(
                    _dir,
                    preCommit: false,
                    snapshotMode: true,
                    pushBefore: GitRevisionScopeService.AllZeroSha,
                    eventName: "push",
                    gitRef: "refs/heads/release/1.0").Should().Be(0);

                // Tamper with the committed snapshot in Git index/HEAD while SyncCompliance regenerates the true report -> GxpDocsDiffService.Compare fails (1)!
                string rtmPath = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
                File.WriteAllText(rtmPath, File.ReadAllText(rtmPath).Replace("Widget", "TamperedWidget", StringComparison.Ordinal));
                Git("add", "docs/SDLC/Requirements_Traceability_Matrix.md");
                Git("commit", "-q", "-m", $"tampered snapshot\n\n{GxpSnapshotPolicy.SnapshotTrailer}");
                SyncGxPService.SyncCompliance(snapshot: false, repoRoot: _dir, resultsDir: "TestResults/gxp").Should().Be(0);
                GxpSnapshotGuardService.Check(
                    _dir,
                    preCommit: false,
                    snapshotMode: true,
                    pushBefore: beforeMainPush,
                    eventName: "push",
                    gitRef: "refs/heads/main").Should().Be(1);
            }
            finally
            {
                Environment.SetEnvironmentVariable(GxpSnapshotPolicy.SnapshotEnvironmentVariable, prevSnapshotEnv);
            }
        }
        finally
        {
            DeleteDirectorySafe(remoteDir);
        }
    }

    private void SetupFullEvidenceRepository(bool includeBrowserEvidence)
    {
        WriteRequirements();
        WriteTaggedTest("First", "PRD-AAA-001");
        string integrationDir = Path.Combine(_dir, "tests", "Cadence.IntegrationTests", "Kernel");
        Directory.CreateDirectory(integrationDir);
        File.WriteAllText(
            Path.Combine(integrationDir, "KernelObservationHostPostgresTests.cs"),
            string.Join("\n",
                "namespace Cadence.IntegrationTests.Kernel;",
                "public class KernelObservationHostPostgresTests",
                "{",
                "    [Fact]",
                "    [GxPRequirement(\"PRD-AAA-001\")]",
                "    public void BrowserCorrectionReachesTheDatabaseWithItsReason() { }",
                "}"));
        WriteEvidenceArtifacts(includeBrowserEvidence, includeSecondTest: false);
    }

    private void WriteEvidenceArtifacts(bool includeBrowserEvidence, bool includeSecondTest)
    {
        string artifactDir = Path.Combine(_dir, "TestResults", "gxp");
        if (Directory.Exists(artifactDir))
        {
            Directory.Delete(artifactDir, recursive: true);
        }

        Directory.CreateDirectory(artifactDir);
        var dotnetResults = new List<(string ClassName, string Method, string Assembly, string Outcome)>
        {
            ("Sample.FirstTests", "First", "Cadence.IntegrationTests.dll", "Passed"),
            ("Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests", "BrowserCorrectionReachesTheDatabaseWithItsReason", "Cadence.IntegrationTests.dll", "NotExecuted"),
        };
        if (includeSecondTest)
        {
            dotnetResults.Add(("Sample.SecondTests", "Second", "Cadence.IntegrationTests.dll", "Passed"));
        }

        WriteDatabaseTrx(Path.Combine(artifactDir, "gxp_dotnet.trx"), [.. dotnetResults]);
        File.WriteAllText(
            Path.Combine(artifactDir, "kernel.database.txt"),
            "fixture=kernel\nserver_version=18.1\nsource=CADENCE_TEST_POSTGRES\nextensions=pgcrypto\nlatest_migration=20261001_Initial\napp_role=NOSUPERUSER NOBYPASSRLS\n");

        if (includeBrowserEvidence)
        {
            string browserDir = Path.Combine(artifactDir, "browser");
            Directory.CreateDirectory(browserDir);
            WriteDatabaseTrx(
                Path.Combine(browserDir, "gxp_browser.trx"),
                ("Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests", "BrowserCorrectionReachesTheDatabaseWithItsReason", "Cadence.IntegrationTests.dll", "Passed"));
            File.WriteAllText(
                Path.Combine(browserDir, "kernel.database.txt"),
                "fixture=browser\nserver_version=18.1\nsource=CADENCE_TEST_POSTGRES\nextensions=pgcrypto\nlatest_migration=20261001_Initial\napp_role=NOSUPERUSER NOBYPASSRLS\n");
        }
    }

    private static void WriteDatabaseTrx(string fullPath, params (string ClassName, string Method, string Assembly, string Outcome)[] results)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"; // deid-ignore: XML namespace, not PHI
        var definitions = new XElement(ns + "TestDefinitions");
        var outcomes = new XElement(ns + "Results");
        foreach (var (className, method, assembly, outcome) in results)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(ns + "UnitTest",
                new XAttribute("id", id),
                new XElement(ns + "TestMethod",
                    new XAttribute("codeBase", assembly),
                    new XAttribute("className", className),
                    new XAttribute("name", method))));
            outcomes.Add(new XElement(ns + "UnitTestResult",
                new XAttribute("testId", id),
                new XAttribute("outcome", outcome),
                new XAttribute("duration", "00:00:00.01")));
        }

        new XDocument(new XElement(ns + "TestRun", definitions, outcomes)).Save(fullPath);
    }

    private bool EvaluateWorkflowSnapshotAuthorization(
        string eventName,
        string refName,
        string baseRef,
        string beforeSha,
        string repoFullName,
        string repoOwner,
        string prHeadRepo,
        string prUserLogin)
    {
        string ciWorkflowPath = Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml");
        string[] lines = File.ReadAllLines(ciWorkflowPath);
        int stepIndex = Array.FindIndex(lines, l => l.Trim() == "- name: Generate Compliance Reports");
        stepIndex.Should().BeGreaterThanOrEqualTo(0);

        int runIndex = Array.FindIndex(lines, stepIndex, l => l.Trim() == "run: |");
        runIndex.Should().BeGreaterThan(stepIndex);

        var scriptLines = new List<string> { "set -euo pipefail" };
        for (int i = runIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length > 0 && !line.StartsWith("          ", StringComparison.Ordinal))
            {
                break;
            }

            string trimmedLine = line.Length >= 10 ? line[10..] : string.Empty;
            if (trimmedLine.StartsWith("node scripts/run-cli.js", StringComparison.Ordinal))
            {
                break;
            }

            scriptLines.Add(trimmedLine);
        }

        scriptLines.Add("printf 'SNAPSHOT=%s\\n' \"${CADENCE_GXP_SNAPSHOT:-}\"");

        string githubEnvFile = Path.Combine(_dir, $"github-env-{Guid.NewGuid():N}.txt");
        File.WriteAllText(githubEnvFile, string.Empty);

        var psi = new ProcessStartInfo("bash")
        {
            WorkingDirectory = _dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(string.Join("\n", scriptLines));
        psi.Environment["EVENT_NAME"] = eventName;
        psi.Environment["REF_NAME"] = refName;
        psi.Environment["BASE_REF"] = baseRef;
        psi.Environment["BEFORE_SHA"] = beforeSha;
        psi.Environment["HEAD_SHA"] = Git("rev-parse", "HEAD").Trim();
        psi.Environment["REPO_FULL_NAME"] = repoFullName;
        psi.Environment["REPO_OWNER"] = repoOwner;
        psi.Environment["PR_HEAD_REPO"] = prHeadRepo;
        psi.Environment["PR_USER_LOGIN"] = prUserLogin;
        psi.Environment["GITHUB_ENV"] = githubEnvFile;
        // Ensure ambient CADENCE_GXP_SNAPSHOT=1 cannot leak into the fail-closed check.
        psi.Environment["CADENCE_GXP_SNAPSHOT"] = "1";

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"ci.yml snapshot authorization bash failed: {error}");

        bool exportedInProcess = output.Split('\n').Any(l => l.Trim() == "SNAPSHOT=1");
        bool writtenToGithubEnv = File.ReadAllLines(githubEnvFile).Any(l => l.Trim() == "CADENCE_GXP_SNAPSHOT=1");
        File.Delete(githubEnvFile);
        exportedInProcess.Should().Be(writtenToGithubEnv);
        return exportedInProcess;
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName ?? throw new InvalidOperationException("Cadence.sln not found.");
        }

        return dir;
    }

    private static string CreateBareOriginRemote()
    {
        string remoteDir = Path.Combine(Path.GetTempPath(), $"gxp-snapshot-origin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(remoteDir);
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = remoteDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("init");
        psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add("--bare");
        psi.ArgumentList.Add("-b");
        psi.ArgumentList.Add("main");
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0);
        return remoteDir;
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

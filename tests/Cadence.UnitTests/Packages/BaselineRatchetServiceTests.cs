using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The <c>baseline-ratchet</c> gate (#5318): a ratchet baseline may only shrink unless a commit on the branch carries a
/// file-and-count-scoped <c>Baseline-Growth: &lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c> trailer, or, in the
/// pre-commit hook, <c>CADENCE_BASELINE_GROWTH</c> supplies matching <c>&lt;path&gt; +&lt;count&gt; - &lt;reason&gt;</c>
/// approval(s). Compares against <c>origin/main</c> or local <c>main</c>, failing closed when neither exists.
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
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/Cadence.Apps.Execution.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/MOD.baseline.txt", true)]
    [InlineData("baselines/permission-matrix/payments_invoice.baseline.txt", true)]
    [InlineData("baselines/direct-state-writes/Cadence.Apps.Payments.baseline.txt", true)]
    [InlineData("baselines/cross-service-isolation/ctms/etmf.baseline.txt", true)]
    [InlineData("baselines/deid/files/apps/web/src/App.vue.baseline.txt", true)]
    [InlineData("baselines/secrets/files/apps/web/src/Sample.ts.secrets.baseline.json", true)]
    [InlineData("baselines/deid/apps/web.baseline.txt", true)]
    [InlineData("baselines/deid/root.baseline.txt", true)]
    [InlineData("baselines/secrets/apps/web.secrets.baseline.json", true)]
    [InlineData("baselines/secrets/config.json", false)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/direct-state-writes.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt", true)]
    [InlineData("tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt", true)]
    [InlineData(".deid-baseline.txt", true)]
    [InlineData(".duplication-baseline.txt", true)]
    [InlineData(".secrets.baseline", true)]
    [InlineData(".cadence/snapshots/baseline-test.tar.gz", false)]
    [InlineData("docs/baseline.md", false)]
    public void IsRatchetBaseline_FindsTheBaselineFiles(string path, bool expected)
    {
        BaselineRatchetService.IsRatchetBaseline(path).Should().Be(expected);
    }

    [Fact]
    public void TryParseTrailer_RequiresBaselinePathPositiveCountAndReason()
    {
        BaselineRatchetService.TryParseTrailer(
            $"./{Baseline} +3 - legacy migration debt",
            out string path,
            out int count,
            out string reason).Should().BeTrue();
        path.Should().Be(Baseline);
        count.Should().Be(3);
        reason.Should().Be("legacy migration debt");

        BaselineRatchetService.TryParseTrailer(
            "baselines/deid +2 - gate-level shard approval",
            out string gatePath,
            out int gateCount,
            out string gateReason).Should().BeTrue();
        gatePath.Should().Be("baselines/deid");
        gateCount.Should().Be(2);
        gateReason.Should().Be("gate-level shard approval");

        BaselineRatchetService.TryParseTrailer("unscoped reason without path or count", out _, out _, out _)
            .Should().BeFalse();
        BaselineRatchetService.TryParseTrailer($"{Baseline} - missing count token", out _, out _, out _)
            .Should().BeFalse();
        BaselineRatchetService.TryParseTrailer($"{Baseline} 3 - missing plus sign", out _, out _, out _)
            .Should().BeFalse();
        BaselineRatchetService.TryParseTrailer($"{Baseline} +0 - zero count not allowed", out _, out _, out _)
            .Should().BeFalse();
        BaselineRatchetService.TryParseTrailer($"{Baseline} -1 - negative count not allowed", out _, out _, out _)
            .Should().BeFalse();
        BaselineRatchetService.TryParseTrailer("docs/guide.md +1 - not a baseline file", out _, out _, out _)
            .Should().BeFalse();
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
        BaselineRatchetService.Entries("baselines/secrets/apps/web.secrets.baseline.json",
            """{ "results": { "apps/web/a.py": [ { "type": "Secret Keyword", "hashed_secret": "abc" } ] } }""")
            .Should().Equal("apps/web/a.py|Secret Keyword|abc");
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
    public void Growth_WithPathAndExactCountTrailerOnABranchCommit_Passes()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - C is ported from the legacy service and burned down in #9999");
        Write("src/Program.cs", "class P { }\n");
        Commit("fix: later commit without a trailer");

        Check().Should().Be(0);
    }

    [Fact]
    public void Growth_WithCountlessOrMismatchedCountTrailer_Fails_UntilCountMatchesExactly()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\nK-XS-1|App|D\n");
        Commit($"feat: add C and D\n\nBaseline-Growth: {Baseline} - missing count");
        Check().Should().Be(1, "count-less Baseline-Growth trailers must be rejected");

        Git("commit", "--amend", "-q", "-m",
            $"feat: add C and D\n\nBaseline-Growth: {Baseline} +1 - undercounted approval");
        Check().Should().Be(1, "trailer approving fewer entries than added must be rejected");

        Git("commit", "--amend", "-q", "-m",
            $"feat: add C and D\n\nBaseline-Growth: {Baseline} +5 - overcounted blanket approval");
        Check().Should().Be(1, "trailer approving more entries than added must be rejected");

        Git("commit", "--amend", "-q", "-m",
            $"feat: add C and D\n\nBaseline-Growth: {Baseline} +2 - exact two legacy entries");
        Check().Should().Be(0, "trailer matching exact added count must pass");

        Git("commit", "--amend", "-q", "-m",
            $"feat: add C and D\n\nBaseline-Growth: {Baseline} +1 - entry C\nBaseline-Growth: {Baseline} +1 - entry D");
        Check().Should().Be(0, "itemized per-entry trailers summing to exact added count must pass");
    }

    [Fact]
    public void Growth_OnSecondCommitAfterAuthorizedFirstCommit_Fails()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - C is ported from the legacy service");

        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\nK-XS-1|App|D\n");
        Commit("feat: sneak in D without trailer");

        Check().Should().Be(1);
    }

    [Fact]
    public void Growth_InUnmentionedSecondBaselineFile_Fails_UntilBothFilesHaveTrailers()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");
        Commit($"feat: grow two baselines\n\nBaseline-Growth: {Baseline} +1 - authorized kernel boundary");

        Check().Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            $"feat: grow two baselines\n\nBaseline-Growth: {Baseline} +1 - authorized kernel boundary\nBaseline-Growth: .deid-baseline.txt +1 - legacy fixture pending cleanup");

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
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - authorized");

        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/z.cs|urls|0003\n");

        Check().Should().Be(1);
    }

    [Fact]
    public void PreCommit_ComparesStagedCopyAgainstHead_NotBranchMergeBase()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - authorized on commit 1");

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
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - authorized");

        Write(".deid-baseline.txt", "# deid\napps/x.cs|urls|0001\napps/y.cs|urls|0002\n");
        Commit("feat: unauthorized growth on second pushed commit");

        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment, pushBefore: pushBefore).Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            "feat: authorized growth on second pushed commit\n\nBaseline-Growth: .deid-baseline.txt +1 - legacy fixture");

        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment, pushBefore: pushBefore).Should().Be(0);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment,
            pushBefore: "0000000000000000000000000000000000000000").Should().Be(0);
    }

    [Fact]
    public void TrailerInTheBodyRatherThanTheTrailerBlock_DoesNotCount()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"feat: add C\n\nBaseline-Growth: {Baseline} +1 - not a trailer\n\nThis paragraph ends the message.");

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
    public void PreCommit_ChecksTheStagedCopy_AndRequiresScopedPathAndCountInEnvironmentVariable()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Git("add", "-A");

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: true,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? "ported from legacy" : null)
            .Should().Be(1, "unscoped CADENCE_BASELINE_GROWTH reasons must be rejected");
        BaselineRatchetService.Check(_dir, preCommit: true,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? $"{Baseline} - missing count" : null)
            .Should().Be(1, "count-less CADENCE_BASELINE_GROWTH entries must be rejected");
        BaselineRatchetService.Check(_dir, preCommit: true,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? $"{Baseline} +2 - wrong count" : null)
            .Should().Be(1, "mismatched CADENCE_BASELINE_GROWTH counts must be rejected");

        BaselineRatchetService.Check(_dir, preCommit: true,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? $"{Baseline} +1 - ported from legacy" : null)
            .Should().Be(0, "file-and-count-scoped CADENCE_BASELINE_GROWTH admits staged growth in pre-commit");

        // The variable is a pre-commit stand-in for the trailer; the branch check ignores it.
        BaselineRatchetService.Check(_dir, preCommit: false,
            environment: name => name == BaselineRatchetService.GrowthEnvironmentVariable ? $"{Baseline} +1 - ported from legacy" : null)
            .Should().Be(1);
    }

    [Fact]
    public void PreCommit_IgnoresUnstagedGrowth()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
    }

    [Fact]
    public void Merge_AllowsBaselineEntriesFromMergeHead_WhileRejectingExtraMergeAdditions()
    {
        Write("src/Feature.cs", "class Feature {}\n");
        Commit("feat: branch change");

        Git("checkout", "-q", "main");
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|Upstream\n");
        Commit("fix: upstream squash commit on main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "feature");
        Git("merge", "--no-commit", "--no-ff", "origin/main");
        File.Exists(Path.Combine(_dir, ".git", "MERGE_HEAD")).Should().BeTrue();

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
        Check().Should().Be(0);

        // Sneaking an extra entry into the merge index that exists in neither HEAD nor MERGE_HEAD must still fail.
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|Upstream\nK-XS-1|App|Sneaked\n");
        Git("add", Baseline);
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(1);
        Check().Should().Be(1);

        // Restore upstream's exact baseline, complete the merge commit, and verify branch check passes.
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|Upstream\n");
        Git("add", Baseline);
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
        Git("commit", "-q", "-m", "Merge remote-tracking branch 'origin/main' into feature");

        Check().Should().Be(0);
    }

    [Fact]
    public void MissingOriginMain_FallsBackToLocalMain_AndFailsClosedWhenNeitherRefExists()
    {
        Git("update-ref", "-d", "refs/remotes/origin/main");

        // Local refs/heads/main still exists, so clean branch passes (0) and unauthorized growth fails (1) against local main.
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment).Should().Be(0);

        // Explicit non-default --base ref does not fall back to local main and fails closed.
        BaselineRatchetService.Check(_dir, preCommit: false, baseRef: "refs/remotes/origin/release/1.0", environment: NoEnvironment)
            .Should().Be(1);

        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|Unauthorized\n");
        Commit("feat: unauthorized growth compared against refs/heads/main");
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment).Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            $"feat: approved growth against local main\n\nBaseline-Growth: {Baseline} +1 - approved against local main");
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment).Should().Be(0);

        Git("reset", "--hard", "-q", "HEAD~1");

        // Once refs/heads/main is also absent, neither ref exists and Check must fail closed (1) locally and in CI.
        Git("branch", "-D", "main");
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: name => name == "CI" ? "true" : null).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: name => name == "GITHUB_ACTIONS" ? "true" : null).Should().Be(1);
        BaselineRatchetService.Check(_dir, preCommit: false, environment: NoEnvironment,
            pushBefore: "0000000000000000000000000000000000000000").Should().Be(1);
    }

    [Fact]
    public void GateMultisetAggregation_AllowsShardSplitsWithoutGrowth_AndBlocksNetShardAdditionsUnlessAdmitted()
    {
        // Split .deid-baseline.txt (apps/x.cs|urls|0001) + add a second shard entry on main first so we test cross-shard multiset preservation.
        Git("checkout", "-q", "main");
        File.Delete(Path.Combine(_dir, ".deid-baseline.txt"));
        Write("baselines/deid/apps/x.baseline.txt", "# deid\napps/x.cs|urls|0001\napps/x.cs|urls|0002\n");
        Commit("chore: seed deid shard on main\n\nBaseline-Growth: baselines/deid +1 - seed second entry");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "-B", "feature");

        // Moving apps/x.cs|urls|0002 into another shard of the same gate preserves the gate multiset -> zero growth!
        Write("baselines/deid/apps/x.baseline.txt", "# deid\napps/x.cs|urls|0001\n");
        Write("baselines/deid/apps/y.baseline.txt", "# deid\napps/x.cs|urls|0002\n");
        Commit("refactor: split deid shards without net growth");
        Check().Should().Be(0);

        // Adding a net-new entry to one shard while shrinking nothing else increases the gate multiset -> fails without trailer!
        Write("baselines/deid/apps/y.baseline.txt", "# deid\napps/x.cs|urls|0002\napps/y.cs|urls|0003\n");
        Commit("feat: add unaccepted deid shard entry");
        Check().Should().Be(1);

        // Admitting via the exact grown shard path ('baselines/deid/apps/y.baseline.txt +1') passes even when 'apps/x.baseline.txt' also exists!
        Git("commit", "--amend", "-q", "-m",
            "feat: admit single shard growth\n\nBaseline-Growth: baselines/deid/apps/y.baseline.txt +1 - legacy fixture in apps/y");
        Check().Should().Be(0);

        // Growing two shards in one commit fails when only one shard carries a trailer, and passes with either both shard trailers or the gate trailer.
        Write("baselines/deid/apps/x.baseline.txt", "# deid\napps/x.cs|urls|0001\napps/x.cs|urls|0004\n");
        Write("baselines/deid/apps/y.baseline.txt", "# deid\napps/x.cs|urls|0002\napps/y.cs|urls|0003\napps/y.cs|urls|0005\n");
        Commit("feat: grow two shards with only one trailer\n\nBaseline-Growth: baselines/deid/apps/x.baseline.txt +1 - only x covered");
        Check().Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            "feat: admit both shards individually\n\nBaseline-Growth: baselines/deid/apps/x.baseline.txt +1 - x covered\nBaseline-Growth: baselines/deid/apps/y.baseline.txt +1 - y covered");
        Check().Should().Be(0);

        Git("commit", "--amend", "-q", "-m",
            "feat: admit deid shard growth at gate level\n\nBaseline-Growth: baselines/deid +2 - legacy migration digest");
        Check().Should().Be(0);
    }

    [Fact]
    public void MergeWithOriginMain_AdmitsUpstreamBaselineGrowth_WhileRejectingExtraMergeAdditions()
    {
        Write("src/Feature.cs", "class Feature {}\n");
        Commit("feat: branch change");

        Git("checkout", "-q", "main");
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Commit($"fix: upstream authorized baseline growth\n\nBaseline-Growth: {Baseline} +1 - authorized on main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "feature");
        Git("merge", "--no-commit", "--no-ff", "origin/main");

        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
        Check().Should().Be(0);

        // Sneaking an extra entry during the merge that exists in neither parent must still fail closed.
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\nK-XS-1|App|D\n");
        Git("add", Baseline);
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(1);
        Check().Should().Be(1);

        // Restoring the upstream baseline content allows committing the merge cleanly without a duplicate trailer.
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\n");
        Git("add", Baseline);
        BaselineRatchetService.Check(_dir, preCommit: true, environment: NoEnvironment).Should().Be(0);
        Git("commit", "-q", "--no-edit");
        Check().Should().Be(0);
    }

    [Fact]
    public void DuplicationBaseline_BlocksGrowthWithoutTrailer_AdmitsAuthorizedGrowth_AndAllowsShrink()
    {
        Write(".duplication-baseline.txt", "# duplication\napps/web/a.ts|apps/web/b.ts|0123456789abcdef\n");
        Commit("feat: add unaccepted duplication baseline entry");
        Check().Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            "feat: ratchet existing frontend clones\n\nBaseline-Growth: .duplication-baseline.txt +1 - legacy frontend clones");
        Check().Should().Be(0);

        File.Delete(Path.Combine(_dir, ".duplication-baseline.txt"));
        Commit("refactor: eliminate remaining frontend clones");
        Check().Should().Be(0);
    }

    [Fact]
    public void IntermediateGrowth_CompletelyRemovedBeforeHead_Passes_WhileUnremediatedEntriesStillFail()
    {
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|C\nK-XS-1|App|D\n");
        Commit($"feat: temporary branch growth with pre-ADR-2832 trailer\n\nBaseline-Growth: {Baseline} - temporary migration clones");
        Check().Should().Be(1, "unremediated branch additions without a +<count> trailer must fail");

        // Removing pre-existing entry A and only one added entry C still leaves D at HEAD -> must still fail closed.
        Write(Baseline, Header + "K-XS-1|App|B\nK-XS-1|App|D\n");
        Commit("refactor: remove A and C while D remains");
        Check().Should().Be(1, "shrinking an existing entry must never offset a surviving unadmitted entry");

        // Removing D as well leaves zero net additions at HEAD relative to origin/main -> passes cleanly.
        Write(Baseline, Header + "K-XS-1|App|B\n");
        Commit("refactor: eliminate remaining temporary entry D");
        Check().Should().Be(0, "intermediate branch baseline additions completely removed before HEAD are remediated");
    }

    [Fact]
    public void ZeroShaVersionTagPush_And_ProtectedWorkflowDispatch_ExitZeroForCommittedHistoryWhenOriginMainDiverges_AndRejectDirtyHeadGrowth()
    {
        // Tag v1.0.1 on a historical release commit, then shrink baselines on origin/main so HEAD has MORE entries than origin/main!
        Git("checkout", "-q", "main");
        Git("tag", "v1.0.1");

        Write(Baseline, Header);
        Write(".deid-baseline.txt", "# deid\n");
        Commit("refactor: burn down all baseline entries on main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "v1.0.1");

        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/tags/v1.0.1").Should().Be(0);

        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            eventName: "workflow_dispatch",
            gitRef: "refs/tags/v1.0.1").Should().Be(0);

        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/release/1.0").Should().Be(0);

        // Unprotected workflow_dispatch fails closed.
        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            eventName: "workflow_dispatch",
            gitRef: "refs/heads/feature").Should().Be(1);

        // Uncommitted working-tree growth at HEAD still fails on tag push and workflow_dispatch.
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|Dirty\n");
        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/tags/v1.0.1").Should().Be(1);
        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            eventName: "workflow_dispatch",
            gitRef: "refs/tags/v1.0.1").Should().Be(1);
    }

    [Fact]
    public void ZeroShaNewReleaseBranchPush_EvaluatesMergeBaseToHead_RequiringBaselineGrowthTrailer()
    {
        Git("checkout", "-q", "-b", "release/1.2", "main");
        Write(Baseline, Header + "K-XS-1|App|A\nK-XS-1|App|B\nK-XS-1|App|ReleaseGrowth\n");
        Commit("feat: grow baseline on new release/1.2 branch without trailer");

        // Advance origin/main independently (burning down entries on main) to verify merge-base(origin/main, HEAD)..HEAD is used.
        Git("checkout", "-q", "main");
        Write(".deid-baseline.txt", "# deid\n");
        Commit("refactor: shrink deid baseline on main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "release/1.2");

        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/heads/release/1.2").Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            $"feat: grow baseline on new release/1.2 branch\n\nBaseline-Growth: {Baseline} +1 - release hotfix baseline");

        BaselineRatchetService.Check(
            _dir,
            preCommit: false,
            environment: NoEnvironment,
            pushBefore: GitRevisionScopeService.AllZeroSha,
            eventName: "push",
            gitRef: "refs/heads/release/1.2").Should().Be(0);
    }

    [Fact]
    public void SentinelAndComplianceGates_MapLegacyAndShardedPathsToSameGateKeys_AllowZeroNetSplits_AndEnforceScopedTrailers()
    {
        foreach (string gateKey in BaselineGateCatalog.AllShardedGateKeys)
        {
            BaselineGateCatalog.IsShardedGateKey(gateKey).Should().BeTrue();
            BaselineRatchetService.IsKnownGateKey(gateKey).Should().BeTrue();
        }

        BaselineRatchetService.GetGateKey("tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt")
            .Should().Be("baselines/permission-matrix");
        BaselineRatchetService.GetGateKey("baselines/permission-matrix/payments_invoice.baseline.txt")
            .Should().Be("baselines/permission-matrix");

        BaselineRatchetService.GetGateKey("tests/Cadence.ArchitectureTests/ArchitectureBaselines/direct-state-writes.baseline.txt")
            .Should().Be("baselines/direct-state-writes");
        BaselineRatchetService.GetGateKey("baselines/direct-state-writes/Cadence.Apps.Payments.baseline.txt")
            .Should().Be("baselines/direct-state-writes");

        BaselineRatchetService.GetGateKey("tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt")
            .Should().Be("baselines/cross-service-isolation");
        BaselineRatchetService.GetGateKey("baselines/cross-service-isolation/ctms/etmf.baseline.txt")
            .Should().Be("baselines/cross-service-isolation");

        BaselineRatchetService.GetGateKey("baselines/deid/files/apps/web/src/App.vue.baseline.txt")
            .Should().Be("baselines/deid");
        BaselineRatchetService.GetGateKey("baselines/secrets/files/apps/web/src/Sample.ts.secrets.baseline.json")
            .Should().Be("baselines/secrets");

        // Seed legacy monolithic sentinel baselines on main, then split them into leaf-owner shards on feature with zero net additions.
        Git("checkout", "-q", "main");
        Write(
            "tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt",
            "# rbac\ncrc|audit_event|read\npi|payments_invoice|approve\n");
        Write(
            "tests/Cadence.ArchitectureTests/ArchitectureBaselines/direct-state-writes.baseline.txt",
            "# writes\nCadence.Apps.Ctms|SiteEndpoints.Create->SaveChangesAsync\nCadence.Apps.Payments|InvoiceEndpoints.Approve->SaveChangesAsync\n");
        Write(
            "tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt",
            "# isolation\nIL|Cadence.Apps.Ctms->Cadence.Apps.Etmf.EtmfClient\nSOURCE|apps/ctms/src/A.cs|Cadence.Apps.Notifications.NotifyClient\n");
        Commit("chore: seed legacy sentinel baselines on main\n\n" +
               "Baseline-Growth: baselines/permission-matrix +2 - seed rbac\n" +
               "Baseline-Growth: baselines/direct-state-writes +2 - seed writes\n" +
               "Baseline-Growth: baselines/cross-service-isolation +2 - seed isolation");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");

        Git("checkout", "-q", "-B", "feature");
        File.Delete(Path.Combine(_dir, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt"));
        File.Delete(Path.Combine(_dir, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/direct-state-writes.baseline.txt"));
        File.Delete(Path.Combine(_dir, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt"));

        Write("baselines/permission-matrix/audit_event.baseline.txt", "# rbac\ncrc|audit_event|read\n");
        Write("baselines/permission-matrix/payments_invoice.baseline.txt", "# rbac\npi|payments_invoice|approve\n");
        Write("baselines/direct-state-writes/Cadence.Apps.Ctms.baseline.txt", "# writes\nCadence.Apps.Ctms|SiteEndpoints.Create->SaveChangesAsync\n");
        Write("baselines/direct-state-writes/Cadence.Apps.Payments.baseline.txt", "# writes\nCadence.Apps.Payments|InvoiceEndpoints.Approve->SaveChangesAsync\n");
        Write("baselines/cross-service-isolation/ctms/etmf.baseline.txt", "# isolation\nIL|Cadence.Apps.Ctms->Cadence.Apps.Etmf.EtmfClient\n");
        Write("baselines/cross-service-isolation/ctms/notifications.baseline.txt", "# isolation\nSOURCE|apps/ctms/src/A.cs|Cadence.Apps.Notifications.NotifyClient\n");
        Commit("refactor: shard sentinel baselines per leaf owner without net additions");
        Check().Should().Be(0, "splitting legacy sentinel baselines into per-owner shards preserves each gate multiset");

        // Net growth in a shard requires an exact shard-scoped or gate-scoped Baseline-Growth trailer.
        Write("baselines/permission-matrix/payments_invoice.baseline.txt", "# rbac\ncrc|payments_invoice|read\npi|payments_invoice|approve\n");
        Commit("feat: add unaccepted rbac grant");
        Check().Should().Be(1);

        Git("commit", "--amend", "-q", "-m",
            "feat: admit rbac shard growth\n\nBaseline-Growth: baselines/permission-matrix/payments_invoice.baseline.txt +1 - legacy role migration");
        Check().Should().Be(0);

        Git("commit", "--amend", "-q", "-m",
            "feat: admit rbac gate growth\n\nBaseline-Growth: baselines/permission-matrix +1 - legacy role migration");
        Check().Should().Be(0);
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

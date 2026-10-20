using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Checks;
using Cadence.Modules.Intelligence.Evaluation.Judging;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence.Evaluation;

/// <summary>
/// A run record is stored as qualification evidence only when it is what the harness would have written for the
/// configuration it names, and the stored record hashes to what qualification binds (ADR-2634 §4.4, #5574).
/// </summary>
public sealed class StoredEvaluationRunTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f6a3b1e-2222-4c2d-9e8f-000000000574"));

    private static readonly TaskConfiguration Configuration = new(
        "data_review.query_draft", 1, ProviderInfo.FakeName, "tier:fast", "Draft a query for {{item}}.", 400, AutonomyLevel.Draft)
    {
        EvaluationSet = EvaluationFixtures.SetRef(),
        AcceptanceCriteria = new AcceptanceCriteria(MinimumPassRate: 0.9, MinimumReviewedPassRate: 0.9),
        ApproverRole = "data_manager",
        Credibility = new CredibilityAssessment(
            "Is the value out of range?", "A data manager reviews each draft query.", ModelInfluence.Medium, DecisionConsequence.Low),
    };

    /// <summary>A sound record has no problems, and the stored copy is canonical JSON whose hash is the record's own.</summary>
    [Fact]
    public async Task SoundRecord_IsStoredCanonically_WithTheRecordsHash()
    {
        var record = await Run(EvaluationFixtures.GoodOutputs());

        EvaluationRun.Problems(Configuration, record).Should().BeEmpty();
        var run = EvaluationRun.Record(Tenant, record);

        run.Id.Should().Be(EvaluationRun.IdFor(Tenant, record.RunId));
        run.Accepted.Should().BeTrue();
        run.RecordHash.Should().Be(record.ContentHash());
        Cadence.Kernel.Trust.ContentHash.Compute(run.RecordJson).Should().Be(run.RecordHash);
        EvaluationRunRecord.FromJson(run.RecordJson).ContentHash().Should().Be(run.RecordHash);
        run.Evidence().Should().Be(new EvaluationEvidence(record.RunId.ToString("D"), run.RecordHash, Configuration.EvaluationSet!.ContentHash, run.SummaryJson));
        run.SummaryJson.Should().Contain("\"accepted\": true");
    }

    /// <summary>A run that failed its criteria is sound evidence of the failure: stored, but not accepted.</summary>
    [Fact]
    public async Task FailedRun_IsSound_ButNotAccepted()
    {
        var outputs = EvaluationFixtures.GoodOutputs();
        outputs["missing-weight"] = EvaluationFixtures.Proposed(new() { ["itemCode"] = "VSORRES_WEIGHT", ["value"] = "70" });
        var record = await Run(outputs);

        EvaluationRun.Problems(Configuration, record).Should().BeEmpty();
        EvaluationRun.Record(Tenant, record).Accepted.Should().BeFalse();
    }

    /// <summary>A caller cannot turn a failed run into an accepted one by editing its verdict or metrics.</summary>
    [Fact]
    public async Task EditedVerdictOrMetrics_AreRefused()
    {
        var outputs = EvaluationFixtures.GoodOutputs();
        outputs["missing-weight"] = EvaluationFixtures.Proposed(new() { ["itemCode"] = "VSORRES_WEIGHT", ["value"] = "70" });
        var failed = await Run(outputs);

        var acceptedVerdict = failed with { Verdict = new AcceptanceVerdict(true, []) };
        var inflatedMetrics = failed with { Metrics = failed.Metrics with { Passed = 3, PassRate = 1 } };

        EvaluationRun.Problems(Configuration, acceptedVerdict).Should().ContainSingle().Which.Should().Contain("verdict");
        EvaluationRun.Problems(Configuration, inflatedMetrics).Should().ContainSingle().Which.Should().Contain("metrics");
    }

    /// <summary>A case marked passed although its run failed or a check failed is refused, so the metrics cannot be recomputed from a lie.</summary>
    [Fact]
    public async Task CaseMarkedPassedDespiteAFailedCheck_IsRefused()
    {
        var record = await Run(EvaluationFixtures.GoodOutputs());
        var first = record.Cases[0];
        var forged = first with { Checks = [.. first.Checks.Take(first.Checks.Count - 1), first.Checks[^1] with { Passed = false }] };
        var errored = first with { Error = "The model timed out." };

        EvaluationRun.Problems(Configuration, record with { Cases = [forged, .. record.Cases.Skip(1)] })
            .Should().Contain(p => p.Contains($"Case '{first.CaseId}' is marked passed", StringComparison.Ordinal));
        EvaluationRun.Problems(Configuration, record with { Cases = [errored, .. record.Cases.Skip(1)] })
            .Should().Contain(p => p.Contains($"Case '{first.CaseId}' is marked passed", StringComparison.Ordinal));
        EvaluationRun.Problems(Configuration, record with { Cases = [.. record.Cases, first] })
            .Should().Contain(p => p.Contains("appears 2 times", StringComparison.Ordinal));
        EvaluationRun.Problems(Configuration, record with { Cases = [] }).Should().Contain("The run has no case results.");
    }

    /// <summary>A record is evidence only for the configuration version, set, criteria and provider it was run with.</summary>
    [Fact]
    public async Task RecordForAnotherConfigurationSetCriteriaOrProvider_IsRefused()
    {
        var record = await Run(EvaluationFixtures.GoodOutputs());
        var changed = Configuration with { Instructions = "Draft a short query for {{item}}." };

        EvaluationRun.Problems(changed, record).Should().ContainSingle().Which.Should().Contain("The run evaluated");
        EvaluationRun.Problems(Configuration, record with { Set = record.Set with { Version = 2 } })
            .Should().ContainSingle().Which.Should().Contain("but the configuration pins");
        EvaluationRun.Problems(Configuration, record with { Criteria = record.Criteria with { MinimumPassRate = 0.5 } })
            .Should().ContainSingle().Which.Should().Contain("acceptance criteria");
        EvaluationRun.Problems(Configuration, record with { Provider = new ProviderInfo("anthropic", "claude-opus-5-5") })
            .Should().ContainSingle().Which.Should().Contain("provider 'anthropic'");
        EvaluationRun.Problems(Configuration, record with { CompletedAt = record.StartedAt.AddSeconds(-1) })
            .Should().Contain("The run ended before it started.");
    }

    /// <summary>Judge scores count only under the rubric, judge and calibration they were calibrated with.</summary>
    [Fact]
    public async Task JudgeScoresWithoutTheirCalibration_AreRefused()
    {
        var record = await Run(EvaluationFixtures.GoodOutputs());
        var scored = record.Cases[0] with { JudgeScores = [new JudgeScore("clarity", 2, "clear")] };

        EvaluationRun.Problems(Configuration, record with { Cases = [scored, .. record.Cases.Skip(1)] })
            .Should().Contain(p => p.Contains("Judge scores count only", StringComparison.Ordinal));
    }

    private static Task<EvaluationRunRecord> Run(Dictionary<string, AgentOutput> outputs) =>
        new EvaluationHarness(new FakeClock()).RunAsync(
            new EvaluationRequest(
                new ScriptedTarget(outputs) { Configuration = Configuration.Ref },
                EvaluationFixtures.Set(),
                EvaluationFixtures.SetRef(),
                Configuration.AcceptanceCriteria!),
            TestContext.Current.CancellationToken);
}

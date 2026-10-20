using System;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence.Evaluation;

/// <summary>An improvement arrives as a pull request carrying its comparison with the qualified run (ADR-2634 §4.6).</summary>
public sealed class EvaluationComparisonTests
{
    private static readonly AcceptanceCriteria Criteria = new(0.5, 0.5);

    /// <summary>A candidate that regresses an adversarial case may not proceed, even when its own pass rate is fine.</summary>
    [Fact]
    public async Task AdversarialRegressionBlocks()
    {
        var baseline = await Run(1, EvaluationFixtures.GoodOutputs());
        var worse = EvaluationFixtures.GoodOutputs();
        worse["missing-weight"] = EvaluationFixtures.Proposed(new() { ["value"] = "70" });
        var candidate = await Run(2, worse);

        var comparison = EvaluationComparison.Compare(baseline, candidate);

        comparison.CandidateMayProceed.Should().BeFalse();
        comparison.Regressions.Should().ContainSingle().Which.Should().Match<CaseRegression>(r => r.CaseId == "missing-weight" && r.Blocking);
        comparison.ToMarkdown().Should().Contain("**blocking** missing-weight").And.Contain("Not ready");
    }

    /// <summary>A candidate that fixes a case and regresses none may proceed to requalification.</summary>
    [Fact]
    public async Task ImprovementMayProceed()
    {
        var weaker = EvaluationFixtures.GoodOutputs();
        weaker["sbp-out-of-range"] = EvaluationFixtures.Proposed(
            new() { ["itemCode"] = "VSORRES_SYSBP", ["value"] = "250", ["queryText"] = "Please check." },
            "observation:obs-1", "rule:vs-sbp-range");
        var baseline = await Run(1, weaker);
        var candidate = await Run(2, EvaluationFixtures.GoodOutputs());

        var comparison = EvaluationComparison.Compare(baseline, candidate);

        comparison.CandidateMayProceed.Should().BeTrue();
        comparison.Fixed.Should().Equal("sbp-out-of-range");
        comparison.PassRateDelta.Should().BeApproximately(1.0 / 3, 1e-9);
        comparison.ToMarkdown().Should().Contain("Ready for requalification").And.Contain(candidate.ContentHash());
    }

    /// <summary>Runs on different set versions are not comparable.</summary>
    [Fact]
    public async Task DifferentSetsAreNotComparable()
    {
        var baseline = await Run(1, EvaluationFixtures.GoodOutputs());
        var candidate = baseline with { Set = baseline.Set with { Version = 2 } };

        var compare = () => EvaluationComparison.Compare(baseline, candidate);

        compare.Should().Throw<InvalidOperationException>().WithMessage("*same set version*");
    }

    private static Task<EvaluationRunRecord> Run(int version, System.Collections.Generic.Dictionary<string, AgentOutput> outputs) =>
        new EvaluationHarness(new FakeClock()).RunAsync(new EvaluationRequest(
            new ScriptedTarget(outputs) { Configuration = EvaluationFixtures.Configuration with { Version = version } },
            EvaluationFixtures.Set(),
            EvaluationFixtures.SetRef(),
            Criteria), TestContext.Current.CancellationToken);
}

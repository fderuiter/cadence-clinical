using System.Collections.Generic;

using Cadence.Core.Domain.Randomization;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Pins <see cref="BoinEngine"/> to the Python reference in <c>packages/engine_core/randomization/boin.py</c>, deleted once these tests landed and kept in git history:
/// BOIN boundaries, the regularized incomplete beta function, the Beta(1, 1) posterior, the BOIN interval
/// rules and the classical 3+3 escalation rules. Expected numbers were computed by hand from the closed forms
/// and cross-checked against the Python functions.
/// </summary>
public class BoinEngineTests
{
    private const double Tolerance = 1e-4;

    private static readonly List<DoseLevelDefinition> ThreeDoses =
    [
        new(0, 10.0, "mg"),
        new(1, 20.0, "mg"),
        new(2, 40.0, "mg"),
    ];

    private static DoseCohortState State(int doseIndex, int evaluated, int dlts) =>
        new(DoseIndex: doseIndex, DoseAmount: ThreeDoses[doseIndex].DoseAmount, DoseUnit: "mg",
            EvaluatedSubjects: evaluated, ObservedDlts: dlts);

    // ---------------------------------------------------------------- BOIN boundaries

    /// <summary>
    /// The published BOIN boundaries (Liu and Yuan 2015, phi1 = 0.6 phi, phi2 = 1.4 phi), rounded to 4 places.
    /// </summary>
    [Theory]
    [InlineData(0.20, 0.1572, 0.2385)]
    [InlineData(0.25, 0.1968, 0.2984)]
    [InlineData(0.30, 0.2365, 0.3585)]
    [InlineData(0.33, 0.2604, 0.3947)]
    public void CalculateBoinBoundaries_MatchesPublishedValues(double target, double expectedE, double expectedD)
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(target);

        lambdaE.Should().BeApproximately(expectedE, 1e-9);
        lambdaD.Should().BeApproximately(expectedD, 1e-9);
    }

    [Fact]
    public void CalculateBoinBoundaries_Target030_GivesRoundedStandardInterval()
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(0.30);

        lambdaE.Should().BeApproximately(0.236, 0.001);
        lambdaD.Should().BeApproximately(0.358, 0.001);
    }

    [Fact]
    public void CalculateBoinBoundaries_HonoursCustomToleranceFractions()
    {
        // phi = 0.3, phi1 = 0.15, phi2 = 0.45:
        // lambda_e = ln(0.85/0.7) / ln(0.3*0.85 / (0.15*0.7)) = 0.19416 / 0.88730 = 0.2188
        // lambda_d = ln(0.7/0.55) / ln(0.45*0.7 / (0.3*0.55)) = 0.24116 / 0.64663 = 0.3730
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(0.30, phi1Fraction: 0.5, phi2Fraction: 1.5);

        lambdaE.Should().BeApproximately(0.2188, Tolerance);
        lambdaD.Should().BeApproximately(0.3730, Tolerance);
    }

    // ---------------------------------------------------------------- Regularized incomplete beta

    [Theory]
    [InlineData(2.0, 3.0, 0.0, 0.0)]
    [InlineData(2.0, 3.0, -0.5, 0.0)]
    [InlineData(2.0, 3.0, 1.0, 1.0)]
    [InlineData(2.0, 3.0, 1.5, 1.0)]
    public void BetaInc_ClampsOutsideTheUnitInterval(double a, double b, double x, double expected)
    {
        BoinEngine.BetaInc(a, b, x).Should().Be(expected);
    }

    /// <summary>
    /// Integer parameters use the binomial identity I_x(a, b) = sum_{k=a}^{a+b-1} C(n, k) x^k (1-x)^(n-k).
    /// </summary>
    [Theory]
    // I_0.3(2, 3) = 6(.09)(.49) + 4(.027)(.7) + .0081 = 0.3483
    [InlineData(2.0, 3.0, 0.3, 0.3483)]
    // I_x(1, 1) = x (uniform CDF)
    [InlineData(1.0, 1.0, 0.42, 0.42)]
    // I_x(1, b) = 1 - (1-x)^b: 1 - 0.7^4 = 0.7599
    [InlineData(1.0, 4.0, 0.3, 0.7599)]
    // I_x(a, 1) = x^a: 0.3^4 = 0.0081
    [InlineData(4.0, 1.0, 0.3, 0.0081)]
    // Symmetric Beta(3, 3) has median 0.5
    [InlineData(3.0, 3.0, 0.5, 0.5)]
    public void BetaInc_IntegerParameters_MatchClosedForm(double a, double b, double x, double expected)
    {
        BoinEngine.BetaInc(a, b, x).Should().BeApproximately(expected, 1e-6);
    }

    /// <summary>
    /// Non-integer parameters use the same 1000-step Simpson rule as Python, which sets the integrand to zero at
    /// t = 0. For Beta(0.5, 0.5) the exact value at 0.5 is 0.5; both implementations return about 0.4910.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.5, 0.5, 0.4910407)]
    [InlineData(2.5, 1.5, 0.4, 0.1739277)]
    public void BetaInc_NonIntegerParameters_MatchPythonSimpsonRule(double a, double b, double x, double expected)
    {
        BoinEngine.BetaInc(a, b, x).Should().BeApproximately(expected, 1e-6);
    }

    // ---------------------------------------------------------------- Posterior probability of toxicity

    /// <summary>
    /// Pr(p &gt; phi | d of n) under a Beta(1, 1) prior is 1 - I_phi(1 + d, 1 + n - d).
    /// </summary>
    [Theory]
    // Beta(1, 4): 0.7^4 = 0.2401
    [InlineData(0, 3, 0.30, 0.2401)]
    // Beta(2, 3): 1 - I_0.3(2, 3) = 1 - 0.3483
    [InlineData(1, 3, 0.30, 0.6517)]
    // Beta(3, 2): 1 - (4(.027)(.7) + .0081) = 1 - 0.0837
    [InlineData(2, 3, 0.30, 0.9163)]
    // Beta(4, 1): 1 - 0.3^4
    [InlineData(3, 3, 0.30, 0.9919)]
    // Beta(4, 1): 1 - 0.25^4 = 0.99609 -> 0.9961
    [InlineData(3, 3, 0.25, 0.9961)]
    // Beta(2, 1): 1 - 0.5^2
    [InlineData(1, 1, 0.50, 0.75)]
    [InlineData(2, 6, 0.30, 0.6471)]
    [InlineData(4, 6, 0.30, 0.9712)]
    [InlineData(5, 9, 0.25, 0.9803)]
    public void CalculatePosteriorProbToxic_MatchesBetaPosterior(int dlts, int evaluated, double target, double expected)
    {
        BoinEngine.CalculatePosteriorProbToxic(dlts, evaluated, target).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void CalculatePosteriorProbToxic_NoEvaluatedSubjects_ReturnsZero()
    {
        BoinEngine.CalculatePosteriorProbToxic(0, 0, 0.30).Should().Be(0.0);
    }

    // ---------------------------------------------------------------- BOIN interval decisions

    [Fact]
    public void EvaluateDoseLevel_RateWithinInterval_Stays()
    {
        // target 0.25: interval (0.1968, 0.2984); 1/4 = 0.25 sits inside.
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(1, 4, 1));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.EmpiricalDltRate.Should().Be(0.25);
        rec.IsTrialStopped.Should().BeFalse();
        rec.Rationale.Should().Contain("within optimal interval");
    }

    [Fact]
    public void EvaluateDoseLevel_RateAtOrAboveLambdaD_DeEscalates()
    {
        // 2/6 = 0.3333 >= 0.2984; posterior Pr(p > 0.25) stays below the 0.95 stopping cutoff.
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(2, 6, 2));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.PosteriorProbToxic.Should().BeLessThan(0.95);
    }

    [Fact]
    public void EvaluateDoseLevel_LowRateAtHighestDose_Stays()
    {
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(2, 6, 0));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(2);
        rec.RecommendedDoseAmount.Should().Be(40.0);
        rec.Rationale.Should().Contain("maximum dose tier");
    }

    [Fact]
    public void EvaluateDoseLevel_HighRateAtLowestDoseBelowStoppingCutoff_Stays()
    {
        // 2/6 at the lowest dose: over lambda_d, but the posterior is not over the stopping cutoff.
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(0, 6, 2));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(0);
        rec.IsTrialStopped.Should().BeFalse();
        rec.Rationale.Should().Contain("at lowest dose");
    }

    [Fact]
    public void EvaluateDoseLevel_ExcessiveToxicityAboveLowestDose_EliminatesAndDeEscalates()
    {
        // 3/3 at target 0.25: Pr(p > 0.25) = 0.9961 >= 0.95.
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(1, 3, 3));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(0);
        rec.RecommendedDoseAmount.Should().Be(10.0);
        rec.PosteriorProbToxic.Should().BeApproximately(0.9961, 1e-9);
        rec.IsTrialStopped.Should().BeFalse();
        rec.Rationale.Should().Contain("eliminated due to excessive toxicity");
    }

    [Fact]
    public void EvaluateDoseLevel_ExcessiveToxicityAtLowestDose_StopsTrialWithoutRecommendation()
    {
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.30), ThreeDoses, State(0, 3, 3));

        rec.Action.Should().Be(DoseEscalationAction.STOP_TRIAL);
        rec.IsTrialStopped.Should().BeTrue();
        rec.RecommendedDoseIndex.Should().BeNull();
        rec.RecommendedDoseAmount.Should().BeNull();
        rec.PosteriorProbToxic.Should().BeApproximately(0.9919, 1e-9);
    }

    [Fact]
    public void EvaluateDoseLevel_FewerThanThreeSubjects_SkipsSafetyRuleAndPosterior()
    {
        // 2/2 = 1.0 would be eliminated with n >= 3; with n = 2 it is a plain lambda_d de-escalation.
        var rec = BoinEngine.EvaluateDoseLevel(new BOINConfig(TargetDltRate: 0.25), ThreeDoses, State(1, 2, 2));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(0);
        rec.PosteriorProbToxic.Should().Be(0.0);
        rec.Rationale.Should().Contain("de-escalation boundary");
    }

    [Fact]
    public void EvaluateDoseLevel_ExplicitBoundaries_OverrideCalculatedOnes()
    {
        // 1/3 = 0.3333 would stay under target 0.30 (interval 0.2365-0.3585); a configured lambda_e of 0.35 escalates.
        var config = new BOINConfig(TargetDltRate: 0.30, EscalationBoundary: 0.35, DeescalationBoundary: 0.60);

        var rec = BoinEngine.EvaluateDoseLevel(config, ThreeDoses, State(0, 3, 1));

        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
    }

    [Fact]
    public void EvaluateDoseLevel_OneExplicitBoundary_CalculatesTheOther()
    {
        // Only lambda_d is configured (0.20); lambda_e comes from target 0.30 (0.2365). 1/4 = 0.25 >= 0.20.
        var config = new BOINConfig(TargetDltRate: 0.30, DeescalationBoundary: 0.20);

        var rec = BoinEngine.EvaluateDoseLevel(config, ThreeDoses, State(1, 4, 1));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(0);
    }

    // ---------------------------------------------------------------- 3+3 escalation

    [Fact]
    public void Evaluate3Plus3_ZeroOfThree_Escalates()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(0, 3, 0));

        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.IsTrialStopped.Should().BeFalse();
        rec.PosteriorProbToxic.Should().BeNull();
        rec.Rationale.Should().Be("3+3 Rule: 0/3 DLTs observed. Escalating to dose tier #1.");
    }

    [Fact]
    public void Evaluate3Plus3_ZeroOfThreeAtHighestDose_StaysAsMtd()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(2, 3, 0));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(2);
        rec.RecommendedDoseAmount.Should().Be(40.0);
        rec.Rationale.Should().Contain("Recommended MTD reached");
    }

    [Fact]
    public void Evaluate3Plus3_OneOfThree_StaysAndExpandsCohort()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(1, 3, 1));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.EmpiricalDltRate.Should().Be(0.3333);
        rec.Rationale.Should().Contain("Expanding cohort to 6 subjects");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Evaluate3Plus3_TwoOrMoreOfThreeAboveLowestDose_DeEscalates(int dlts)
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(2, 3, dlts));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.IsTrialStopped.Should().BeFalse();
        rec.Rationale.Should().Be($"3+3 Rule: {dlts}/3 DLTs observed. MTD exceeded. De-escalating to tier #1.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Evaluate3Plus3_TwoOrMoreOfThreeAtLowestDose_StopsTrial(int dlts)
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(0, 3, dlts));

        rec.Action.Should().Be(DoseEscalationAction.STOP_TRIAL);
        rec.IsTrialStopped.Should().BeTrue();
        rec.RecommendedDoseIndex.Should().BeNull();
        rec.RecommendedDoseAmount.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Evaluate3Plus3_AtMostOneOfSix_Escalates(int dlts)
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(0, 6, dlts));

        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
        rec.Rationale.Should().Be($"3+3 Rule: {dlts}/6 DLTs in expanded cohort. Escalating to tier #1.");
    }

    [Fact]
    public void Evaluate3Plus3_OneOfSixAtHighestDose_ConfirmsMtd()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(2, 6, 1));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(2);
        rec.Rationale.Should().Contain("confirmed as MTD");
    }

    [Fact]
    public void Evaluate3Plus3_TwoOfSixAboveLowestDose_DeEscalates()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(1, 6, 2));

        rec.Action.Should().Be(DoseEscalationAction.DEESCALATE);
        rec.RecommendedDoseIndex.Should().Be(0);
        rec.RecommendedDoseAmount.Should().Be(10.0);
        rec.Rationale.Should().Contain("MTD exceeded");
    }

    [Fact]
    public void Evaluate3Plus3_TwoOfSixAtLowestDose_StopsTrial()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(0, 6, 2));

        rec.Action.Should().Be(DoseEscalationAction.STOP_TRIAL);
        rec.IsTrialStopped.Should().BeTrue();
        rec.RecommendedDoseIndex.Should().BeNull();
    }

    [Fact]
    public void Evaluate3Plus3_MoreThanSixSubjects_UsesExpandedCohortRules()
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(1, 7, 1));

        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    public void Evaluate3Plus3_IncompleteCohort_StaysInProgress(int evaluated, int dlts)
    {
        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, State(1, evaluated, dlts));

        rec.Action.Should().Be(DoseEscalationAction.STAY);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.IsTrialStopped.Should().BeFalse();
        rec.Rationale.Should().Be($"Cohort in progress ({evaluated} subjects evaluated). Continuing at current dose.");
    }

    [Fact]
    public void Evaluate3Plus3_UnknownCurrentDose_FallsBackToStateAmountAndUnit()
    {
        // Dose index 5 is not defined; Python and C# both fall back to the cohort state's own amount and unit.
        var state = new DoseCohortState(DoseIndex: 5, DoseAmount: 80.0, DoseUnit: "mg/kg", EvaluatedSubjects: 3, ObservedDlts: 1);

        var rec = BoinEngine.Evaluate3Plus3Cohort(new ThreePlusThreeConfig(), ThreeDoses, state);

        rec.CurrentDoseAmount.Should().Be(80.0);
        rec.CurrentDoseUnit.Should().Be("mg/kg");
        rec.RecommendedDoseAmount.Should().Be(80.0);
    }

    [Fact]
    public void BetaInc_LargeSample_N1030_ReturnsValidProbabilityWithoutOverflow()
    {
        // n = a + b - 1 = 520 + 520 - 1 = 1039 >= 1030
        double result = BoinEngine.BetaInc(520, 520, 0.50);

        double.IsNaN(result).Should().BeFalse();
        double.IsInfinity(result).Should().BeFalse();
        result.Should().BeApproximately(0.50, 1e-4);
    }

    [Fact]
    public void CalculatePosteriorProbToxic_LargeCohort_N1030_ReturnsValidProbability()
    {
        // evaluatedSubjects = 1200, observedDlts = 360 => a = 361, b = 841, n = 1201 >= 1030
        double prob = BoinEngine.CalculatePosteriorProbToxic(360, 1200, 0.30);

        double.IsNaN(prob).Should().BeFalse();
        double.IsInfinity(prob).Should().BeFalse();
        prob.Should().BeInRange(0.0, 1.0);
    }
}

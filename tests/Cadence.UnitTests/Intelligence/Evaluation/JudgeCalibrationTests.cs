using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Modules.Intelligence.Evaluation.Judging;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence.Evaluation;

/// <summary>A judge counts only when it agrees with people on a large enough labelled sample (ADR-2634 §4.3).</summary>
public sealed class JudgeCalibrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Kappa matches the textbook value: 80% agreement with 50% base rates is 0.6.</summary>
    [Fact]
    public void KappaMatchesTheDefinition()
    {
        var pairs = Pairs(bothPass: 20, humanOnly: 5, judgeOnly: 5, bothFail: 20);

        JudgeCalibrator.Kappa(pairs).Should().BeApproximately(0.6, 1e-9);
    }

    /// <summary>When people labelled every sample the same way, agreement proves nothing and kappa is undefined.</summary>
    [Fact]
    public void OneClassLabelsCannotCalibrate()
    {
        var (rubric, rubricRef) = RubricLoader.Load(EvaluationFixtures.RubricJson);
        var samples = Enumerable.Range(0, 40).Select(i => new CalibrationSample($"s{i}", "clarity", 2, 2)).ToList();

        var calibration = JudgeCalibrator.Calibrate(rubric, rubricRef, EvaluationFixtures.JudgeConfiguration, samples, Now);

        calibration.Passed.Should().BeFalse();
        calibration.Criteria.Single().Kappa.Should().BeNull();
        calibration.Criteria.Single().ObservedAgreement.Should().Be(1);
    }

    /// <summary>Perfect agreement on too small a sample still fails.</summary>
    [Fact]
    public void SmallSampleCannotCalibrate()
    {
        var (rubric, rubricRef) = RubricLoader.Load(EvaluationFixtures.RubricJson);

        var calibration = JudgeCalibrator.Calibrate(rubric, rubricRef, EvaluationFixtures.JudgeConfiguration, Samples(10, 10, 0, 0), Now);

        calibration.Passed.Should().BeFalse();
        calibration.Criteria.Single().Kappa.Should().Be(1);
    }

    /// <summary>Agreement at the threshold passes; below it fails.</summary>
    [Theory]
    [InlineData(20, 5, 5, 20, true)]
    [InlineData(18, 7, 7, 18, false)]
    public void ThresholdDecides(int bothPass, int humanOnly, int judgeOnly, int bothFail, bool passed)
    {
        var (rubric, rubricRef) = RubricLoader.Load(EvaluationFixtures.RubricJson);

        JudgeCalibrator.Calibrate(rubric, rubricRef, EvaluationFixtures.JudgeConfiguration, Samples(bothPass, bothFail, humanOnly, judgeOnly), Now)
            .Passed.Should().Be(passed);
    }

    /// <summary>A rubric may not lower the sample size or kappa floor.</summary>
    [Fact]
    public void RubricCannotLowerTheFloors()
    {
        var (rubric, _) = RubricLoader.Load(EvaluationFixtures.RubricJson);

        RubricLoader.Validate(rubric with { MinimumCalibrationSamples = 10, MinimumKappa = 0.4 })
            .Should().HaveCount(2);
    }

    /// <summary>A passing calibration for <paramref name="rubric"/> under <paramref name="rubricRef"/>.</summary>
    internal static JudgeCalibration Passing(Rubric rubric, RubricRef rubricRef, DateTimeOffset now) =>
        JudgeCalibrator.Calibrate(rubric, rubricRef, EvaluationFixtures.JudgeConfiguration, Samples(25, 25, 0, 0), now);

    private static List<CalibrationSample> Samples(int bothPass, int bothFail, int humanOnly, int judgeOnly) =>
        [.. Pairs(bothPass, humanOnly, judgeOnly, bothFail).Select((p, i) => new CalibrationSample($"s{i}", "clarity", p.Human ? 2 : 0, p.Judge ? 2 : 0))];

    private static List<(bool Human, bool Judge)> Pairs(int bothPass, int humanOnly, int judgeOnly, int bothFail) =>
    [
        .. Enumerable.Repeat((true, true), bothPass),
        .. Enumerable.Repeat((true, false), humanOnly),
        .. Enumerable.Repeat((false, true), judgeOnly),
        .. Enumerable.Repeat((false, false), bothFail),
    ];
}

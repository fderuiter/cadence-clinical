using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Trust;

namespace Cadence.Modules.Intelligence.Evaluation.Judging;

/// <summary>One human-labelled sample: the score a person gave and the score the judge gave the same output.</summary>
/// <param name="SampleId">The labelled output's id.</param>
/// <param name="CriterionId">The criterion both scored.</param>
/// <param name="HumanScore">The person's score.</param>
/// <param name="JudgeScore">The judge's score.</param>
public sealed record CalibrationSample(string SampleId, string CriterionId, int HumanScore, int JudgeScore);

/// <summary>Agreement on one criterion.</summary>
/// <param name="CriterionId">The criterion.</param>
/// <param name="Samples">How many labelled samples it had.</param>
/// <param name="ObservedAgreement">The share of samples where judge and person agreed on pass or fail.</param>
/// <param name="Kappa">Cohen's kappa on pass or fail; null when the human labels hold only one class.</param>
/// <param name="Passed">Whether it met the rubric's sample size and kappa threshold.</param>
public sealed record CriterionAgreement(string CriterionId, int Samples, double ObservedAgreement, double? Kappa, bool Passed);

/// <summary>
/// A calibration of one judge configuration against one rubric version (ADR-2634 §4.3). Judge scores count in an
/// evaluation run only when a passing calibration exists for exactly that rubric hash and judge configuration.
/// </summary>
/// <param name="Rubric">The rubric version calibrated.</param>
/// <param name="Judge">The judge configuration calibrated.</param>
/// <param name="CalibratedAt">When, from <c>IClock</c>.</param>
/// <param name="Criteria">Agreement per criterion.</param>
/// <param name="Passed">True when every criterion passed.</param>
public sealed record JudgeCalibration(
    RubricRef Rubric,
    ConfigurationRef Judge,
    DateTimeOffset CalibratedAt,
    IReadOnlyList<CriterionAgreement> Criteria,
    bool Passed)
{
    /// <summary>The content hash of this record, which a run record cites.</summary>
    public string ContentHash() =>
        Cadence.Kernel.Trust.ContentHash.Compute(JsonSerializer.Serialize(this, EvaluationJson.Options));

    /// <summary>True when this calibration covers exactly <paramref name="rubric"/> and <paramref name="judge"/>.</summary>
    public bool Covers(RubricRef rubric, ConfigurationRef judge) => Passed && Rubric == rubric && Judge == judge;
}

/// <summary>Computes judge agreement with people (ADR-2634 §4.3).</summary>
public static class JudgeCalibrator
{
    /// <summary>
    /// Calibrates <paramref name="judge"/> on <paramref name="rubric"/> from human-labelled samples. A criterion passes
    /// when it has at least the rubric's minimum sample, the human labels include both a pass and a fail (otherwise
    /// kappa is undefined and agreement proves nothing), and kappa reaches the rubric's threshold.
    /// </summary>
    public static JudgeCalibration Calibrate(
        Rubric rubric,
        RubricRef rubricRef,
        ConfigurationRef judge,
        IReadOnlyList<CalibrationSample> samples,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rubric);
        ArgumentNullException.ThrowIfNull(samples);
        var criteria = new List<CriterionAgreement>(rubric.Criteria.Count);
        foreach (var criterion in rubric.Criteria)
        {
            var pairs = samples
                .Where(s => string.Equals(s.CriterionId, criterion.CriterionId, StringComparison.Ordinal))
                .Select(s => (Human: s.HumanScore >= criterion.PassScore, Judge: s.JudgeScore >= criterion.PassScore))
                .ToList();
            var observed = pairs.Count == 0 ? 0 : pairs.Count(p => p.Human == p.Judge) / (double)pairs.Count;
            var kappa = Kappa(pairs);
            var passed = pairs.Count >= rubric.MinimumCalibrationSamples && kappa >= rubric.MinimumKappa;
            criteria.Add(new CriterionAgreement(criterion.CriterionId, pairs.Count, observed, kappa, passed));
        }

        return new JudgeCalibration(rubricRef, judge, now, criteria, criteria.Count > 0 && criteria.TrueForAll(c => c.Passed));
    }

    /// <summary>Cohen's kappa for two raters on a binary label; null when the first rater used only one label.</summary>
    public static double? Kappa(IReadOnlyList<(bool Human, bool Judge)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
        {
            return null;
        }

        double n = pairs.Count;
        var humanPass = pairs.Count(p => p.Human) / n;
        if (humanPass is 0 or 1)
        {
            return null;
        }

        var judgePass = pairs.Count(p => p.Judge) / n;
        var observed = pairs.Count(p => p.Human == p.Judge) / n;
        var expected = (humanPass * judgePass) + ((1 - humanPass) * (1 - judgePass));
        return expected >= 1 ? null : (observed - expected) / (1 - expected);
    }
}

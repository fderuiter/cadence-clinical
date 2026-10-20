using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation.Judging;

/// <summary>
/// A written rubric for an LLM judge (ADR-2634 §4.3), a versioned file under
/// <c>apps/intelligence/evaluation/rubrics/</c>. It scores only what a deterministic check cannot decide.
/// </summary>
/// <param name="RubricId">Stable kebab-case id.</param>
/// <param name="Version">Positive version.</param>
/// <param name="Criteria">What the judge scores.</param>
/// <param name="MinimumCalibrationSamples">The smallest human-labelled sample a calibration may use.</param>
/// <param name="MinimumKappa">The Cohen's kappa each criterion must reach against human labels.</param>
public sealed record Rubric(
    string RubricId,
    int Version,
    IReadOnlyList<RubricCriterion> Criteria,
    int MinimumCalibrationSamples = Rubric.DefaultMinimumSamples,
    double MinimumKappa = Rubric.DefaultMinimumKappa)
{
    /// <summary>The smallest sample any rubric may set: below this a kappa is too noisy to trust.</summary>
    public const int DefaultMinimumSamples = 30;

    /// <summary>Substantial agreement (Landis and Koch); a rubric may require more, never less.</summary>
    public const double DefaultMinimumKappa = 0.6;
}

/// <summary>One scored criterion.</summary>
/// <param name="CriterionId">Unique within the rubric.</param>
/// <param name="Question">What the judge decides, in one sentence.</param>
/// <param name="Levels">The written description of each score, lowest first; the score is the index.</param>
/// <param name="PassScore">The lowest score that passes.</param>
public sealed record RubricCriterion(string CriterionId, string Question, IReadOnlyList<string> Levels, int PassScore);

/// <summary>The identity of a rubric version: id, version and content hash.</summary>
public sealed record RubricRef(string RubricId, int Version, string ContentHash);

/// <summary>A judge's score for one criterion on one case.</summary>
/// <param name="CriterionId">The criterion.</param>
/// <param name="Score">The level index the judge chose.</param>
/// <param name="Rationale">The judge's one-line reason, kept for the reviewer.</param>
public sealed record JudgeScore(string CriterionId, int Score, string Rationale);

/// <summary>
/// An LLM judge: a task configuration of its own, scoring an output against a rubric. Its scores count only with a
/// passing <see cref="JudgeCalibration"/> for the same rubric hash and judge configuration.
/// </summary>
public interface IJudge
{
    /// <summary>The judge's own configuration version.</summary>
    ConfigurationRef Configuration { get; }

    /// <summary>Scores <paramref name="output"/> on every criterion of <paramref name="rubric"/>.</summary>
    Task<IReadOnlyList<JudgeScore>> ScoreAsync(Rubric rubric, EvaluationCase evaluationCase, AgentOutput output, CancellationToken cancellationToken);
}

/// <summary>Reads and validates rubric files.</summary>
public static class RubricLoader
{
    /// <summary>Parses a rubric and returns it with its identity, or throws with every rule it breaks.</summary>
    /// <exception cref="InvalidOperationException">The rubric breaks a rule.</exception>
    public static (Rubric Rubric, RubricRef Ref) Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var rubric = JsonSerializer.Deserialize<Rubric>(json, EvaluationJson.Options)
            ?? throw new InvalidOperationException("The rubric file is empty.");
        var errors = Validate(rubric);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The rubric is invalid: " + string.Join(" ", errors));
        }

        return (rubric, new RubricRef(rubric.RubricId, rubric.Version, ContentHash.Compute(json)));
    }

    /// <summary>Every rule <paramref name="rubric"/> breaks.</summary>
    public static IReadOnlyList<string> Validate(Rubric rubric)
    {
        ArgumentNullException.ThrowIfNull(rubric);
        var errors = new List<string>();
        if (rubric.Version < 1)
        {
            errors.Add("Rubric version must be 1 or more.");
        }

        if (rubric.Criteria.Count == 0)
        {
            errors.Add("The rubric has no criteria.");
        }

        if (rubric.MinimumCalibrationSamples < Rubric.DefaultMinimumSamples)
        {
            errors.Add($"The calibration sample may not be smaller than {Rubric.DefaultMinimumSamples}.");
        }

        if (rubric.MinimumKappa is < Rubric.DefaultMinimumKappa or > 1)
        {
            errors.Add($"The kappa threshold must be between {Rubric.DefaultMinimumKappa} and 1.");
        }

        if (rubric.Criteria.Select(c => c.CriterionId).Distinct(StringComparer.Ordinal).Count() != rubric.Criteria.Count)
        {
            errors.Add("Criterion ids must be unique.");
        }

        foreach (var c in rubric.Criteria)
        {
            if (c.Levels.Count < 2)
            {
                errors.Add($"Criterion '{c.CriterionId}' needs at least two written levels.");
            }

            if (c.PassScore < 1 || c.PassScore >= c.Levels.Count)
            {
                errors.Add($"Criterion '{c.CriterionId}' pass score must name a level above the lowest.");
            }

            if (c.Levels.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Criterion '{c.CriterionId}' has a level without a description.");
            }
        }

        return errors;
    }
}

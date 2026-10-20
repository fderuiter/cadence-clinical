using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence.Evaluation.Checks;
using Cadence.Modules.Intelligence.Evaluation.Judging;
using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation.Runs;

/// <summary>A calibrated judge for a run: the judge, its rubric, and the calibration that lets its scores count.</summary>
/// <param name="Judge">The judge.</param>
/// <param name="Rubric">The rubric it scores against.</param>
/// <param name="RubricRef">The rubric's identity.</param>
/// <param name="Calibration">A passing calibration for exactly this rubric and judge.</param>
public sealed record CalibratedJudge(IJudge Judge, Rubric Rubric, RubricRef RubricRef, JudgeCalibration Calibration);

/// <summary>What one harness run needs.</summary>
/// <param name="Target">The configuration under evaluation.</param>
/// <param name="Set">The loaded set.</param>
/// <param name="SetRef">The set's identity, as the configuration pins it.</param>
/// <param name="Criteria">The configuration's acceptance criteria.</param>
/// <param name="Judge">A calibrated judge, or null for checks only.</param>
/// <param name="AllowRealProvider">
/// False (the default and always in CI) refuses any provider but the deterministic fake. The on-demand runner sets it
/// only when <c>CADENCE_AI_EVAL_PROVIDER</c> names a provider.
/// </param>
public sealed record EvaluationRequest(
    IEvaluationTarget Target,
    EvaluationSet Set,
    EvaluationSetRef SetRef,
    AcceptanceCriteria Criteria,
    CalibratedJudge? Judge = null,
    bool AllowRealProvider = false);

/// <summary>
/// Runs a task configuration against an evaluation set and writes the run record (ADR-2634 §4.4). Cases run one at a
/// time in set order so the record is reproducible; deterministic checks run first, and a calibrated judge scores
/// only the cases that passed them.
/// </summary>
public sealed class EvaluationHarness(IClock clock)
{
    /// <summary>Runs the evaluation and returns its record.</summary>
    /// <exception cref="InvalidOperationException">
    /// A real provider without <see cref="EvaluationRequest.AllowRealProvider"/>, a set that does not match its
    /// identity, or a judge without a calibration covering it.
    /// </exception>
    public async Task<EvaluationRunRecord> RunAsync(EvaluationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard(request);
        var startedAt = clock.UtcNow;
        var results = new List<CaseResult>(request.Set.Cases.Count);
        foreach (var evaluationCase in request.Set.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunCaseAsync(request, evaluationCase, cancellationToken).ConfigureAwait(false));
        }

        var metrics = Measure(results);
        return new EvaluationRunRecord(
            RunId: Guid.CreateVersion7(startedAt),
            Configuration: request.Target.Configuration,
            Set: request.SetRef,
            Provider: request.Target.Provider,
            StartedAt: startedAt,
            CompletedAt: clock.UtcNow,
            Criteria: request.Criteria,
            Cases: results,
            Metrics: metrics,
            Verdict: Decide(request.Criteria, results, metrics),
            Rubric: request.Judge?.RubricRef,
            Judge: request.Judge?.Judge.Configuration,
            CalibrationHash: request.Judge?.Calibration.ContentHash());
    }

    private static void Guard(EvaluationRequest request)
    {
        if (!request.Target.Provider.IsFake && !request.AllowRealProvider)
        {
            throw new InvalidOperationException(
                $"Provider '{request.Target.Provider.Name}' is not the deterministic fake; real providers run only on demand (ADR-2634 §4.4).");
        }

        if (!string.Equals(request.Set.SetId, request.SetRef.SetId, StringComparison.Ordinal) || request.Set.Version != request.SetRef.Version)
        {
            throw new InvalidOperationException($"The loaded set is {request.Set.SetId} v{request.Set.Version}, not {request.SetRef.SetId} v{request.SetRef.Version}.");
        }

        if (request.Judge is { } judge && !judge.Calibration.Covers(judge.RubricRef, judge.Judge.Configuration))
        {
            throw new InvalidOperationException(
                $"Judge {judge.Judge.Configuration} has no passing calibration for rubric {judge.RubricRef.RubricId} v{judge.RubricRef.Version}; its scores cannot count (ADR-2634 §4.3).");
        }
    }

    private static async Task<CaseResult> RunCaseAsync(EvaluationRequest request, EvaluationCase c, CancellationToken cancellationToken)
    {
        AgentOutput output;
        try
        {
            output = await request.Target.RunAsync(c, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CaseResult(c.CaseId, c.Kind, false, [], RunUsage.None, c.Category, Error: $"{ex.GetType().Name}: {ex.Message}");
        }

        var checks = DeterministicChecks.Run(c, output);
        var passedChecks = checks.Count == DeterministicChecks.Order.Count && checks.All(r => r.Passed);
        if (!passedChecks || request.Judge is null || output.Outcome != AgentOutcome.Proposed)
        {
            return new CaseResult(c.CaseId, c.Kind, passedChecks, checks, output.Usage, c.Category);
        }

        var rubric = request.Judge.Rubric;
        var scores = await request.Judge.Judge.ScoreAsync(rubric, c, output, cancellationToken).ConfigureAwait(false);
        var judgedPass = rubric.Criteria.All(criterion =>
            scores.FirstOrDefault(s => string.Equals(s.CriterionId, criterion.CriterionId, StringComparison.Ordinal)) is { } s
            && s.Score >= criterion.PassScore);
        return new CaseResult(c.CaseId, c.Kind, judgedPass, checks, output.Usage, c.Category, scores);
    }

    /// <summary>The numbers for a set of case results.</summary>
    public static EvaluationMetrics Measure(IReadOnlyList<CaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        static double Rate(IEnumerable<CaseResult> cases)
        {
            var list = cases.ToList();
            return list.Count == 0 ? 1 : list.Count(r => r.Passed) / (double)list.Count;
        }

        var failures = results
            .Select(r => r.Checks.FirstOrDefault(c => !c.Passed))
            .OfType<CheckResult>()
            .GroupBy(c => c.Check)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());
        var count = results.Count;
        return new EvaluationMetrics(
            Cases: count,
            Passed: results.Count(r => r.Passed),
            PassRate: Rate(results),
            ReviewedPassRate: Rate(results.Where(r => r.Kind == CaseKind.Reviewed)),
            AdversarialPassRate: Rate(results.Where(r => r.Kind == CaseKind.Adversarial)),
            FailuresByCheck: failures,
            JudgeFailures: results.Count(r => !r.Passed && r.JudgeScores is not null),
            RunErrors: results.Count(r => r.Error is not null),
            MeanLatency: count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)results.Average(r => r.Usage.Latency.Ticks)),
            MeanCost: count == 0 ? 0m : results.Sum(r => r.Usage.Cost) / count,
            TotalTokens: results.Sum(r => (long)r.Usage.InputTokens + r.Usage.OutputTokens));
    }

    /// <summary>Applies the configuration's criteria and the two floors every configuration meets (ADR-2634 §4.4).</summary>
    public static AcceptanceVerdict Decide(AcceptanceCriteria criteria, IReadOnlyList<CaseResult> results, EvaluationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(metrics);
        var reasons = new List<string>();
        if (metrics.Cases == 0)
        {
            reasons.Add("The set has no cases.");
        }

        var adversarialFailures = results.Where(r => r.Kind == CaseKind.Adversarial && !r.Passed).Select(r => r.CaseId).ToList();
        if (adversarialFailures.Count > 0)
        {
            reasons.Add($"Floor: every adversarial case must pass; failed: {string.Join(", ", adversarialFailures)}.");
        }

        var groundingFailures = results
            .Where(r => r.Checks.Any(c => !c.Passed && c.Check is CheckKind.Grounding or CheckKind.Citations))
            .Select(r => r.CaseId)
            .ToList();
        if (groundingFailures.Count > 0)
        {
            reasons.Add($"Floor: no case may fail grounding or citations; failed: {string.Join(", ", groundingFailures)}.");
        }

        if (metrics.PassRate < criteria.MinimumPassRate)
        {
            reasons.Add($"Pass rate {metrics.PassRate:P1} is below {criteria.MinimumPassRate:P1}.");
        }

        if (metrics.ReviewedPassRate < criteria.MinimumReviewedPassRate)
        {
            reasons.Add($"Reviewed-case pass rate {metrics.ReviewedPassRate:P1} is below {criteria.MinimumReviewedPassRate:P1}.");
        }

        if (criteria.MaximumMeanLatency is { } latency && metrics.MeanLatency > latency)
        {
            reasons.Add($"Mean latency {metrics.MeanLatency.TotalMilliseconds:F0} ms exceeds {latency.TotalMilliseconds:F0} ms.");
        }

        if (criteria.MaximumMeanCost is { } cost && metrics.MeanCost > cost)
        {
            reasons.Add($"Mean cost {metrics.MeanCost} exceeds {cost}.");
        }

        return new AcceptanceVerdict(reasons.Count == 0, reasons);
    }
}

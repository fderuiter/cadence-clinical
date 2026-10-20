using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Modules.Intelligence.Evaluation.Checks;
using Cadence.Modules.Intelligence.Evaluation.Judging;
using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation.Runs;

/// <summary>
/// The acceptance criteria a task configuration declares for its evaluation (ADR-2634 §4.4). Two floors apply whatever
/// it declares: every adversarial case passes, and no case fails grounding or citations.
/// </summary>
/// <param name="MinimumPassRate">The share of all cases that must pass.</param>
/// <param name="MinimumReviewedPassRate">The share of reviewed cases that must pass.</param>
/// <param name="MaximumMeanLatency">The mean latency per case it may not exceed, if any.</param>
/// <param name="MaximumMeanCost">The mean cost per case it may not exceed, if any.</param>
public sealed record AcceptanceCriteria(
    double MinimumPassRate,
    double MinimumReviewedPassRate,
    TimeSpan? MaximumMeanLatency = null,
    decimal? MaximumMeanCost = null);

/// <summary>The result of one case.</summary>
/// <param name="CaseId">The case.</param>
/// <param name="Kind">Reviewed or adversarial.</param>
/// <param name="Passed">True when every check, and every judged criterion, passed.</param>
/// <param name="Checks">The deterministic checks that ran, in order; the last one failed if the case failed on a check.</param>
/// <param name="Usage">What the run cost.</param>
/// <param name="Category">The adversarial category, if any.</param>
/// <param name="JudgeScores">The judge's scores, when a calibrated judge ran on the case.</param>
/// <param name="Error">Why the run itself failed, if it did; the case then fails.</param>
public sealed record CaseResult(
    string CaseId,
    CaseKind Kind,
    bool Passed,
    IReadOnlyList<CheckResult> Checks,
    RunUsage Usage,
    AdversarialCategory? Category = null,
    IReadOnlyList<JudgeScore>? JudgeScores = null,
    string? Error = null);

/// <summary>The numbers a run produced.</summary>
/// <param name="Cases">Cases run.</param>
/// <param name="Passed">Cases passed.</param>
/// <param name="PassRate">Share of all cases passed.</param>
/// <param name="ReviewedPassRate">Share of reviewed cases passed (1 when there are none).</param>
/// <param name="AdversarialPassRate">Share of adversarial cases passed (1 when there are none).</param>
/// <param name="FailuresByCheck">How many cases failed at each deterministic check.</param>
/// <param name="JudgeFailures">Cases that passed every check but failed a judged criterion.</param>
/// <param name="RunErrors">Cases whose run threw.</param>
/// <param name="MeanLatency">Mean latency per case.</param>
/// <param name="MeanCost">Mean cost per case.</param>
/// <param name="TotalTokens">Input and output tokens across the run.</param>
public sealed record EvaluationMetrics(
    int Cases,
    int Passed,
    double PassRate,
    double ReviewedPassRate,
    double AdversarialPassRate,
    IReadOnlyDictionary<CheckKind, int> FailuresByCheck,
    int JudgeFailures,
    int RunErrors,
    TimeSpan MeanLatency,
    decimal MeanCost,
    long TotalTokens);

/// <summary>Whether a run met its criteria, and every reason it did not.</summary>
public sealed record AcceptanceVerdict(bool Accepted, IReadOnlyList<string> Reasons);

/// <summary>
/// The record of one evaluation run (ADR-2634 §4.4): one configuration version against one set version on one
/// provider. Qualification (#5385) attests <see cref="ContentHash"/> with a kernel attestation.
/// </summary>
/// <param name="RunId">Unique id of the run.</param>
/// <param name="Configuration">The configuration version evaluated.</param>
/// <param name="Set">The set version it ran against.</param>
/// <param name="Provider">The provider and model.</param>
/// <param name="StartedAt">When the run started, from <c>IClock</c>.</param>
/// <param name="CompletedAt">When it ended, from <c>IClock</c>.</param>
/// <param name="Criteria">The acceptance criteria applied.</param>
/// <param name="Cases">Each case's result, in set order.</param>
/// <param name="Metrics">The run's numbers.</param>
/// <param name="Verdict">Whether it was accepted.</param>
/// <param name="Rubric">The rubric, when a judge ran.</param>
/// <param name="Judge">The judge configuration, when a judge ran.</param>
/// <param name="CalibrationHash">The content hash of the calibration that let the judge's scores count.</param>
public sealed record EvaluationRunRecord(
    Guid RunId,
    ConfigurationRef Configuration,
    EvaluationSetRef Set,
    ProviderInfo Provider,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    AcceptanceCriteria Criteria,
    IReadOnlyList<CaseResult> Cases,
    EvaluationMetrics Metrics,
    AcceptanceVerdict Verdict,
    RubricRef? Rubric = null,
    ConfigurationRef? Judge = null,
    string? CalibrationHash = null)
{
    /// <summary>The record as stored and attested: indented JSON in the evaluation file format.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, EvaluationJson.Options);

    /// <summary>The kernel content hash of the record, which a qualification attestation binds to.</summary>
    public string ContentHash() => Cadence.Kernel.Trust.ContentHash.Compute(ToJson());

    /// <summary>Reads a stored record.</summary>
    /// <exception cref="JsonException">The text is not a run record.</exception>
    public static EvaluationRunRecord FromJson(string json) =>
        JsonSerializer.Deserialize<EvaluationRunRecord>(json, EvaluationJson.Options)
        ?? throw new JsonException("The run record is empty.");
}

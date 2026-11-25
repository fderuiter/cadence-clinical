using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Domain.Validation;

/// <summary>A run as the ledger needs it: when it was recorded, and which packs and datasets it covered.</summary>
public sealed record LedgerRun(
    ValidationRunId Id, ValidationStage Stage, DateTimeOffset RecordedAt, IReadOnlySet<string> PackKeys, IReadOnlySet<string> Datasets);

/// <summary>A detection as the ledger needs it.</summary>
public sealed record LedgerDetection(
    ValidationRunId Run, ValidationFindingId Finding, ValidationStage Stage, string PackKey, string Dataset, ValidationSeverity Severity);

/// <summary>A detection with full finding identity and target metadata for materializing projections.</summary>
public sealed record LedgerProjectedDetection(
    ValidationRunId Run,
    ValidationFindingId Finding,
    ValidationStage Stage,
    string PackKey,
    string RuleId,
    FindingTarget Target,
    ValidationSeverity Severity);

/// <summary>The decisions recorded for a finding (<see cref="ValidationFinding"/>), as the ledger needs them.</summary>
/// <param name="Finding">The finding.</param>
/// <param name="Decision">The latest status decision, or null when the finding has only been classified.</param>
/// <param name="DecidedAt">When <paramref name="Decision"/> was made.</param>
/// <param name="Classification">The finding's classification, if any.</param>
public sealed record LedgerDecision(
    ValidationFindingId Finding, FindingStatus? Decision, DateTimeOffset? DecidedAt, FindingClassification? Classification);

/// <summary>
/// Materialized state of one finding, combining its derived <see cref="FindingState"/> with the boundary timestamps and
/// decision metadata needed to transition incrementally when a covering run or finding decision commits.
/// </summary>
public sealed record FindingProjectionState(
    ValidationFindingId Finding,
    ValidationStage Stage,
    string PackKey,
    string Dataset,
    string RuleId,
    FindingTarget Target,
    ValidationSeverity Severity,
    FindingStatus Status,
    FindingClassification? Classification,
    FindingStatus? Decision,
    DateTimeOffset? DecidedAt,
    bool DecisionInForce,
    ValidationRunId FirstDetectedRun,
    DateTimeOffset FirstDetectedAt,
    ValidationRunId LastDetectedRun,
    DateTimeOffset LastDetectedAt,
    ValidationRunId? ResolvedBy,
    DateTimeOffset? LatestCleanRunAt)
{
    /// <summary>True when the finding blocks its stage's gate (<see cref="FindingBlocking.Blocks"/>).</summary>
    public bool Blocks => FindingBlocking.Blocks(Stage, Severity, Status);

    /// <summary>Projects this state into the ledger's <see cref="FindingState"/> representation.</summary>
    public FindingState ToFindingState() => new(
        Finding,
        Stage,
        PackKey,
        Dataset,
        Severity,
        Status,
        FirstDetectedRun,
        LastDetectedRun,
        ResolvedBy,
        Classification,
        DecisionInForce);
}

/// <summary>A finding's derived state.</summary>
/// <param name="Finding">The finding.</param>
/// <param name="Stage">Its stage.</param>
/// <param name="PackKey">The rule pack that detects it.</param>
/// <param name="Dataset">The dataset of its target.</param>
/// <param name="Severity">The severity of its latest detection.</param>
/// <param name="Status">Its status.</param>
/// <param name="FirstDetectedRun">The first run that detected it.</param>
/// <param name="LastDetectedRun">The latest run that detected it.</param>
/// <param name="ResolvedBy">For a resolved finding, the run that no longer detected it.</param>
/// <param name="Classification">Its classification, if any.</param>
/// <param name="DecisionInForce">True when <see cref="Status"/> comes from a decision people made.</param>
public sealed record FindingState(
    ValidationFindingId Finding,
    ValidationStage Stage,
    string PackKey,
    string Dataset,
    ValidationSeverity Severity,
    FindingStatus Status,
    ValidationRunId FirstDetectedRun,
    ValidationRunId LastDetectedRun,
    ValidationRunId? ResolvedBy,
    FindingClassification? Classification,
    bool DecisionInForce)
{
    /// <summary>True when the finding blocks its stage's gate (<see cref="FindingBlocking.Blocks"/>).</summary>
    public bool Blocks => FindingBlocking.Blocks(Stage, Severity, Status);
}

/// <summary>
/// Derives the status of a study's findings from its runs and the decisions people made (spec §10.3, K-FIND-1).
/// A run <em>covers</em> a finding when it has the finding's stage and evaluated the finding's rule pack over the
/// finding's dataset. Then:
/// <list type="bullet">
/// <item>when the latest covering run did not detect the finding, it is <see cref="FindingStatus.Resolved"/>, and the
/// first covering run after its last detection is the evidence;</item>
/// <item>otherwise the latest decision holds (Investigating, Accepted, Explained, or Open after a reopen), unless the
/// finding was resolved by a run after that decision and has recurred since, which makes it Open again;</item>
/// <item>a finding nobody decided on is Open.</item>
/// </list>
/// Runs are ordered by the time they were recorded, then by id. The result is a pure function of its inputs.
/// </summary>
public static class FindingLedger
{
    /// <summary>The derived state of every finding in <paramref name="detections"/>.</summary>
    public static IReadOnlyList<FindingState> Derive(
        IEnumerable<LedgerRun> runs, IEnumerable<LedgerDetection> detections, IEnumerable<LedgerDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(decisions);

        var ordered = runs.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).ToArray();
        var position = new Dictionary<ValidationRunId, int>(ordered.Length);
        var coverage = new Dictionary<(ValidationStage, string, string), List<int>>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var run = ordered[i];
            position.Add(run.Id, i);
            foreach (var pack in run.PackKeys)
            {
                foreach (var dataset in run.Datasets)
                {
                    var key = (run.Stage, pack, dataset);
                    if (!coverage.TryGetValue(key, out var list))
                    {
                        coverage[key] = list = [];
                    }

                    list.Add(i);
                }
            }
        }

        var all = detections.ToList();
        if (all.FirstOrDefault(d => !position.ContainsKey(d.Run)) is { } orphan)
        {
            throw new ArgumentException($"Detection of {orphan.Finding} names unknown run {orphan.Run}.", nameof(detections));
        }

        var decided = decisions.ToDictionary(d => d.Finding);
        var states = new List<FindingState>();
        foreach (var finding in all.GroupBy(d => d.Finding).OrderBy(g => g.Key))
        {
            var detectedAt = finding.Select(d => position[d.Run]).ToHashSet();
            var first = detectedAt.Min();
            var last = detectedAt.Max();
            var latest = finding.First(d => position[d.Run] == last);
            var covering = coverage.GetValueOrDefault((latest.Stage, latest.PackKey, latest.Dataset)) ?? [last];
            decided.TryGetValue(finding.Key, out var decision);

            FindingStatus status;
            ValidationRunId? resolvedBy = null;
            var inForce = false;
            if (covering[^1] > last)
            {
                status = FindingStatus.Resolved;
                resolvedBy = ordered[covering.First(p => p > last)].Id;
            }
            else
            {
                var resolution = covering.LastOrDefault(p => p > first && p < last && !detectedAt.Contains(p), -1);
                inForce = decision is { Decision: not null, DecidedAt: { } decidedAt }
                    && (resolution < 0 || decidedAt >= ordered[resolution].RecordedAt);
                status = inForce ? decision!.Decision!.Value : FindingStatus.Open;
            }

            states.Add(new FindingState(
                finding.Key,
                latest.Stage,
                latest.PackKey,
                latest.Dataset,
                latest.Severity,
                status,
                ordered[first].Id,
                ordered[last].Id,
                resolvedBy,
                decision?.Classification,
                inForce));
        }

        return states;
    }

    /// <summary>
    /// Deterministically projects the full materialized state of every finding from immutable runs, detections, and
    /// decisions (ADR-2462 §5). Every returned state's <see cref="FindingProjectionState.ToFindingState"/> equals the
    /// corresponding <see cref="Derive"/> entry, and its <see cref="FindingProjectionState.LatestCleanRunAt"/> matches
    /// sequential <see cref="ApplyCoveringRun"/> transitions.
    /// </summary>
    public static IReadOnlyList<FindingProjectionState> Project(
        IEnumerable<LedgerRun> runs,
        IEnumerable<LedgerProjectedDetection> detections,
        IEnumerable<LedgerDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(decisions);

        var ordered = runs.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).ToArray();
        var position = new Dictionary<ValidationRunId, int>(ordered.Length);
        var coverage = new Dictionary<(ValidationStage, string, string), List<int>>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var run = ordered[i];
            position.Add(run.Id, i);
            foreach (var pack in run.PackKeys)
            {
                foreach (var dataset in run.Datasets)
                {
                    var key = (run.Stage, pack, dataset);
                    if (!coverage.TryGetValue(key, out var list))
                    {
                        coverage[key] = list = [];
                    }

                    list.Add(i);
                }
            }
        }

        var all = detections.ToList();
        if (all.FirstOrDefault(d => !position.ContainsKey(d.Run)) is { } orphan)
        {
            throw new ArgumentException($"Detection of {orphan.Finding} names unknown run {orphan.Run}.", nameof(detections));
        }

        var decided = decisions.ToDictionary(d => d.Finding);
        var projected = new List<FindingProjectionState>();
        foreach (var finding in all.GroupBy(d => d.Finding).OrderBy(g => g.Key))
        {
            var detectedAt = finding.Select(d => position[d.Run]).ToHashSet();
            var first = detectedAt.Min();
            var last = detectedAt.Max();
            var latest = finding.First(d => position[d.Run] == last);
            var covering = coverage.GetValueOrDefault((latest.Stage, latest.PackKey, latest.Target.Dataset)) ?? [last];
            decided.TryGetValue(finding.Key, out var decision);

            var latestCleanPos = covering.LastOrDefault(p => p > first && !detectedAt.Contains(p), -1);
            DateTimeOffset? latestCleanRunAt = latestCleanPos < 0 ? null : ordered[latestCleanPos].RecordedAt.ToUniversalTime();
            var rawDecision = decision?.Decision;
            var decidedAt = decision?.DecidedAt?.ToUniversalTime();

            FindingStatus status;
            ValidationRunId? resolvedBy = null;
            var inForce = false;
            if (covering[^1] > last)
            {
                status = FindingStatus.Resolved;
                resolvedBy = ordered[covering.First(p => p > last)].Id;
            }
            else
            {
                inForce = rawDecision is not null
                    && decidedAt is { } decidedUtc
                    && (latestCleanRunAt is null || decidedUtc >= latestCleanRunAt.Value);
                status = inForce ? rawDecision!.Value : FindingStatus.Open;
            }

            projected.Add(new FindingProjectionState(
                finding.Key,
                latest.Stage,
                latest.PackKey,
                latest.Target.Dataset,
                latest.RuleId,
                latest.Target,
                latest.Severity,
                status,
                decision?.Classification,
                rawDecision,
                decidedAt,
                inForce,
                ordered[first].Id,
                ordered[first].RecordedAt.ToUniversalTime(),
                ordered[last].Id,
                ordered[last].RecordedAt.ToUniversalTime(),
                resolvedBy,
                latestCleanRunAt));
        }

        return projected;
    }

    /// <summary>
    /// Transitions a finding's projected state when a covering <paramref name="run"/> (ordered after all prior covering
    /// runs) either detects the finding (<paramref name="detection"/> non-null) or completes cleanly without detecting it.
    /// </summary>
    public static FindingProjectionState ApplyCoveringRun(
        FindingProjectionState? current,
        LedgerRun run,
        LedgerProjectedDetection? detection,
        LedgerDecision? priorDecision = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordedAt = run.RecordedAt.ToUniversalTime();
        if (current is null)
        {
            ArgumentNullException.ThrowIfNull(detection);
            var decidedAt = priorDecision?.DecidedAt?.ToUniversalTime();
            var decision = priorDecision?.Decision;
            var inForce = decision is not null && decidedAt is not null;
            var status = inForce ? decision!.Value : FindingStatus.Open;
            return new FindingProjectionState(
                detection.Finding,
                detection.Stage,
                detection.PackKey,
                detection.Target.Dataset,
                detection.RuleId,
                detection.Target,
                detection.Severity,
                status,
                priorDecision?.Classification,
                decision,
                decidedAt,
                inForce,
                run.Id,
                recordedAt,
                run.Id,
                recordedAt,
                ResolvedBy: null,
                LatestCleanRunAt: null);
        }

        if (detection is not null)
        {
            var inForce = current.Decision is not null
                && current.DecidedAt is { } decidedAt
                && (current.LatestCleanRunAt is null || decidedAt >= current.LatestCleanRunAt.Value);
            var status = inForce ? current.Decision!.Value : FindingStatus.Open;
            return current with
            {
                Severity = detection.Severity,
                Status = status,
                DecisionInForce = inForce,
                LastDetectedRun = run.Id,
                LastDetectedAt = recordedAt,
                ResolvedBy = null,
            };
        }

        return current with
        {
            Status = FindingStatus.Resolved,
            DecisionInForce = false,
            ResolvedBy = current.ResolvedBy ?? run.Id,
            LatestCleanRunAt = recordedAt,
        };
    }

    /// <summary>Applies a newly recorded <see cref="LedgerDecision"/> to an existing <see cref="FindingProjectionState"/>.</summary>
    public static FindingProjectionState ApplyDecision(FindingProjectionState current, LedgerDecision decision)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(decision);
        var decidedAt = decision.DecidedAt?.ToUniversalTime();
        if (current.ResolvedBy is not null)
        {
            return current with
            {
                Classification = decision.Classification,
                Decision = decision.Decision,
                DecidedAt = decidedAt,
                DecisionInForce = false,
                Status = FindingStatus.Resolved,
            };
        }

        var inForce = decision.Decision is not null
            && decidedAt is { } decidedUtc
            && (current.LatestCleanRunAt is null || decidedUtc >= current.LatestCleanRunAt.Value);
        var status = inForce ? decision.Decision!.Value : FindingStatus.Open;
        return current with
        {
            Classification = decision.Classification,
            Decision = decision.Decision,
            DecidedAt = decidedAt,
            DecisionInForce = inForce,
            Status = status,
        };
    }
}

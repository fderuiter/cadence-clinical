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

/// <summary>The decisions recorded for a finding (<see cref="ValidationFinding"/>), as the ledger needs them.</summary>
/// <param name="Finding">The finding.</param>
/// <param name="Decision">The latest status decision, or null when the finding has only been classified.</param>
/// <param name="DecidedAt">When <paramref name="Decision"/> was made.</param>
/// <param name="Classification">The finding's classification, if any.</param>
public sealed record LedgerDecision(
    ValidationFindingId Finding, FindingStatus? Decision, DateTimeOffset? DecidedAt, FindingClassification? Classification);

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
}

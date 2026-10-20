using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation.Runs;

/// <summary>A case that passed in the baseline run and fails in the candidate.</summary>
/// <param name="CaseId">The case.</param>
/// <param name="Kind">Reviewed or adversarial.</param>
/// <param name="Blocking">True for an adversarial case or a grounding or citation failure, which blocks the change.</param>
/// <param name="Detail">The candidate's failure.</param>
public sealed record CaseRegression(string CaseId, CaseKind Kind, bool Blocking, string Detail);

/// <summary>
/// The comparison an improvement pull request carries (ADR-2634 §4.6): the candidate configuration against the current
/// qualified one, on the same set version.
/// </summary>
/// <param name="Baseline">The qualified run.</param>
/// <param name="Candidate">The candidate's run.</param>
/// <param name="Regressions">Cases that passed before and fail now.</param>
/// <param name="Fixed">Cases that failed before and pass now.</param>
/// <param name="PassRateDelta">Candidate pass rate minus baseline pass rate.</param>
/// <param name="MeanCostDelta">Candidate mean cost minus baseline mean cost.</param>
/// <param name="MeanLatencyDelta">Candidate mean latency minus baseline mean latency.</param>
public sealed record EvaluationComparison(
    EvaluationRunRecord Baseline,
    EvaluationRunRecord Candidate,
    IReadOnlyList<CaseRegression> Regressions,
    IReadOnlyList<string> Fixed,
    double PassRateDelta,
    decimal MeanCostDelta,
    TimeSpan MeanLatencyDelta)
{
    /// <summary>
    /// True when the candidate may replace the baseline: its own run is accepted and no regression blocks it.
    /// Requalification still decides; this only says the pull request is ready for it.
    /// </summary>
    public bool CandidateMayProceed => Candidate.Verdict.Accepted && !Regressions.Any(r => r.Blocking);

    /// <summary>Compares two runs of the same set version.</summary>
    /// <exception cref="InvalidOperationException">The runs used different set versions or configuration keys.</exception>
    public static EvaluationComparison Compare(EvaluationRunRecord baseline, EvaluationRunRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.Set != candidate.Set)
        {
            throw new InvalidOperationException("An improvement is compared on the same set version as the qualified run.");
        }

        if (!string.Equals(baseline.Configuration.Key, candidate.Configuration.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Both runs must be versions of the same task configuration.");
        }

        var before = baseline.Cases.ToDictionary(c => c.CaseId, StringComparer.Ordinal);
        var regressions = new List<CaseRegression>();
        var fixedCases = new List<string>();
        foreach (var now in candidate.Cases)
        {
            if (!before.TryGetValue(now.CaseId, out var was))
            {
                continue;
            }

            if (was.Passed && !now.Passed)
            {
                var failed = now.Checks.FirstOrDefault(c => !c.Passed);
                var blocking = now.Kind == CaseKind.Adversarial
                    || failed?.Check is Checks.CheckKind.Grounding or Checks.CheckKind.Citations;
                regressions.Add(new CaseRegression(now.CaseId, now.Kind, blocking, failed?.Detail ?? now.Error ?? "Failed a judged criterion."));
            }
            else if (!was.Passed && now.Passed)
            {
                fixedCases.Add(now.CaseId);
            }
        }

        return new EvaluationComparison(
            baseline,
            candidate,
            regressions,
            fixedCases,
            candidate.Metrics.PassRate - baseline.Metrics.PassRate,
            candidate.Metrics.MeanCost - baseline.Metrics.MeanCost,
            candidate.Metrics.MeanLatency - baseline.Metrics.MeanLatency);
    }

    /// <summary>The Markdown summary an improvement pull request pastes into its description.</summary>
    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(c, $"### Evaluation: {Candidate.Configuration} against {Baseline.Configuration}");
        sb.AppendLine();
        sb.AppendLine(c, $"Set {Candidate.Set.SetId} v{Candidate.Set.Version} (`{Candidate.Set.ContentHash}`), provider {Candidate.Provider.Name} {Candidate.Provider.ModelId}.");
        sb.AppendLine();
        sb.AppendLine("| Measure | Baseline | Candidate |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine(c, $"| Pass rate | {Baseline.Metrics.PassRate:P1} | {Candidate.Metrics.PassRate:P1} |");
        sb.AppendLine(c, $"| Adversarial pass rate | {Baseline.Metrics.AdversarialPassRate:P1} | {Candidate.Metrics.AdversarialPassRate:P1} |");
        sb.AppendLine(c, $"| Mean cost | {Baseline.Metrics.MeanCost} | {Candidate.Metrics.MeanCost} |");
        sb.AppendLine(c, $"| Mean latency (ms) | {Baseline.Metrics.MeanLatency.TotalMilliseconds:F0} | {Candidate.Metrics.MeanLatency.TotalMilliseconds:F0} |");
        sb.AppendLine(c, $"| Accepted | {(Baseline.Verdict.Accepted ? "yes" : "no")} | {(Candidate.Verdict.Accepted ? "yes" : "no")} |");
        sb.AppendLine();
        sb.AppendLine(c, $"Regressions: {Regressions.Count} ({Regressions.Count(r => r.Blocking)} blocking). Fixed: {Fixed.Count}.");
        foreach (var r in Regressions)
        {
            sb.AppendLine(c, $"- {(r.Blocking ? "**blocking** " : string.Empty)}{r.CaseId}: {r.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine(c, $"Candidate run record hash: `{Candidate.ContentHash()}`. {(CandidateMayProceed ? "Ready for requalification." : "Not ready: fix the blocking items first.")}");
        return sb.ToString();
    }
}

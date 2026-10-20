using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cadence.Modules.Intelligence.Evaluation.Production;

/// <summary>
/// The monitoring section of a task configuration (ADR-2634 §4.5). Rates breach only when the 95% Wilson interval
/// lies wholly past the threshold and the window holds at least <see cref="MinimumSample"/> decided proposals.
/// </summary>
/// <param name="MinimumAcceptanceRate">Approved over decided; below it (confidently) suspends.</param>
/// <param name="MaximumCitationFailureRate">Proposals with a dropped citation over all; above it (confidently) suspends.</param>
/// <param name="MaximumUnsafeRejections">Unsafe rejections in the window at which the configuration suspends.</param>
/// <param name="MaximumMeanEditDistance">Mean edit distance of approvals above which a Quality signal is raised.</param>
/// <param name="MaximumMeanCost">Mean cost per run above which a warning is raised, if any.</param>
/// <param name="MaximumP95Latency">95th percentile latency above which a warning is raised, if any.</param>
/// <param name="WindowSize">The most recent outcomes the monitor looks at.</param>
/// <param name="MinimumSample">Decided outcomes needed before a rate can breach.</param>
public sealed record MonitoringThresholds(
    double MinimumAcceptanceRate = 0.7,
    double MaximumCitationFailureRate = 0.02,
    int MaximumUnsafeRejections = 1,
    double MaximumMeanEditDistance = 0.35,
    decimal? MaximumMeanCost = null,
    TimeSpan? MaximumP95Latency = null,
    int WindowSize = 200,
    int MinimumSample = 30);

/// <summary>What a breach is about.</summary>
public enum SignalKind
{
    /// <summary>Reviewers approve too few proposals.</summary>
    LowAcceptance,

    /// <summary>The verifier drops citations too often.</summary>
    CitationFailures,

    /// <summary>A reviewer judged a proposal unsafe.</summary>
    UnsafeRejection,

    /// <summary>Reviewers edit approvals heavily.</summary>
    HighEditDistance,

    /// <summary>Runs cost more than budgeted.</summary>
    Cost,

    /// <summary>Runs are slower than allowed.</summary>
    Latency,
}

/// <summary>How serious a breach is.</summary>
public enum SignalSeverity
{
    /// <summary>Operational only (cost, latency); never blocks clinical work.</summary>
    Warning,

    /// <summary>A Quality signal to triage; the configuration keeps running.</summary>
    Quality,

    /// <summary>A safety breach: a Quality signal and the configuration is suspended.</summary>
    Suspend,
}

/// <summary>A threshold the configuration breached.</summary>
/// <param name="Kind">What.</param>
/// <param name="Severity">How serious.</param>
/// <param name="Observed">The observed value.</param>
/// <param name="Threshold">The threshold it breached.</param>
/// <param name="Detail">A sentence a Quality reviewer can read.</param>
public sealed record SignalBreach(SignalKind Kind, SignalSeverity Severity, double Observed, double Threshold, string Detail);

/// <summary>The production numbers for one configuration version over its window.</summary>
/// <param name="Outcomes">Outcomes in the window.</param>
/// <param name="Decided">Approved or rejected.</param>
/// <param name="AcceptanceRate">Approved over decided.</param>
/// <param name="AcceptanceInterval">95% Wilson interval of the acceptance rate.</param>
/// <param name="MeanEditDistance">Mean edit distance of approvals.</param>
/// <param name="CitationFailureRate">Outcomes with a dropped citation over all outcomes.</param>
/// <param name="CitationFailureInterval">95% Wilson interval of the citation failure rate.</param>
/// <param name="RejectionReasons">Rejections by reason.</param>
/// <param name="MeanCost">Mean cost per run.</param>
/// <param name="P95Latency">95th percentile latency.</param>
public sealed record ProductionMetrics(
    int Outcomes,
    int Decided,
    double AcceptanceRate,
    (double Lower, double Upper) AcceptanceInterval,
    double MeanEditDistance,
    double CitationFailureRate,
    (double Lower, double Upper) CitationFailureInterval,
    IReadOnlyDictionary<RejectionReason, int> RejectionReasons,
    decimal MeanCost,
    TimeSpan P95Latency);

/// <summary>The monitor's view of one configuration version.</summary>
public sealed record MonitoringReport(ConfigurationRef Configuration, ProductionMetrics Metrics, IReadOnlyList<SignalBreach> Breaches)
{
    /// <summary>True when any breach suspends the configuration.</summary>
    public bool Suspend => Breaches.Any(b => b.Severity == SignalSeverity.Suspend);
}

/// <summary>Computes production metrics and breaches for one configuration version (ADR-2634 §4.5). Pure.</summary>
public static class ConfigurationMonitor
{
    /// <summary>
    /// Evaluates the most recent <see cref="MonitoringThresholds.WindowSize"/> outcomes of <paramref name="configuration"/>
    /// in <paramref name="outcomes"/>; outcomes of other versions are ignored.
    /// </summary>
    public static MonitoringReport Evaluate(ConfigurationRef configuration, IEnumerable<ProposalOutcomeRecord> outcomes, MonitoringThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(thresholds);
        var window = outcomes
            .Where(o => o.Configuration == configuration)
            .OrderByDescending(o => o.DecidedAt)
            .Take(thresholds.WindowSize)
            .ToList();
        var metrics = Measure(window);
        return new MonitoringReport(configuration, metrics, Breaches(metrics, window, thresholds));
    }

    private static ProductionMetrics Measure(IReadOnlyList<ProposalOutcomeRecord> window)
    {
        var approved = window.Where(o => o.Decision == ProposalDecision.Approved).ToList();
        var decided = window.Count(o => o.Decision != ProposalDecision.Lapsed);
        var citationFailures = window.Count(o => o.CitationsDropped > 0);
        var latencies = window.Select(o => o.Latency).Order().ToList();
        return new ProductionMetrics(
            Outcomes: window.Count,
            Decided: decided,
            AcceptanceRate: decided == 0 ? 0 : approved.Count / (double)decided,
            AcceptanceInterval: WilsonInterval.Of(approved.Count, decided),
            MeanEditDistance: approved.Count == 0 ? 0 : approved.Average(o => o.EditDistance ?? 0),
            CitationFailureRate: window.Count == 0 ? 0 : citationFailures / (double)window.Count,
            CitationFailureInterval: WilsonInterval.Of(citationFailures, window.Count),
            RejectionReasons: window
                .Where(o => o.Decision == ProposalDecision.Rejected && o.RejectionReason is not null)
                .GroupBy(o => o.RejectionReason!.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count()),
            MeanCost: window.Count == 0 ? 0m : window.Average(o => o.Cost),
            P95Latency: latencies.Count == 0 ? TimeSpan.Zero : latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1]);
    }

    private static List<SignalBreach> Breaches(ProductionMetrics m, IReadOnlyList<ProposalOutcomeRecord> window, MonitoringThresholds t)
    {
        var c = CultureInfo.InvariantCulture;
        var breaches = new List<SignalBreach>();
        var unsafeCount = m.RejectionReasons.GetValueOrDefault(RejectionReason.Unsafe);
        if (unsafeCount >= t.MaximumUnsafeRejections)
        {
            breaches.Add(new(SignalKind.UnsafeRejection, SignalSeverity.Suspend, unsafeCount, t.MaximumUnsafeRejections,
                string.Create(c, $"{unsafeCount} proposal(s) rejected as unsafe in the last {m.Outcomes}.")));
        }

        if (m.Decided >= t.MinimumSample && m.AcceptanceInterval.Upper < t.MinimumAcceptanceRate)
        {
            breaches.Add(new(SignalKind.LowAcceptance, SignalSeverity.Suspend, m.AcceptanceRate, t.MinimumAcceptanceRate,
                string.Create(c, $"Acceptance {m.AcceptanceRate:P1} (95% interval up to {m.AcceptanceInterval.Upper:P1}) over {m.Decided} decisions is below {t.MinimumAcceptanceRate:P1}.")));
        }

        if (m.Outcomes >= t.MinimumSample && m.CitationFailureInterval.Lower > t.MaximumCitationFailureRate)
        {
            breaches.Add(new(SignalKind.CitationFailures, SignalSeverity.Suspend, m.CitationFailureRate, t.MaximumCitationFailureRate,
                string.Create(c, $"Citations were dropped on {m.CitationFailureRate:P1} of {m.Outcomes} proposals (95% interval from {m.CitationFailureInterval.Lower:P1}), above {t.MaximumCitationFailureRate:P1}.")));
        }

        var approvals = window.Count(o => o.Decision == ProposalDecision.Approved);
        if (approvals >= t.MinimumSample && m.MeanEditDistance > t.MaximumMeanEditDistance)
        {
            breaches.Add(new(SignalKind.HighEditDistance, SignalSeverity.Quality, m.MeanEditDistance, t.MaximumMeanEditDistance,
                string.Create(c, $"Reviewers changed {m.MeanEditDistance:P1} of approved text on average, above {t.MaximumMeanEditDistance:P1}.")));
        }

        if (t.MaximumMeanCost is { } cost && m.Outcomes >= t.MinimumSample && m.MeanCost > cost)
        {
            breaches.Add(new(SignalKind.Cost, SignalSeverity.Warning, (double)m.MeanCost, (double)cost,
                string.Create(c, $"Mean cost per run {m.MeanCost} exceeds {cost}.")));
        }

        if (t.MaximumP95Latency is { } latency && m.Outcomes >= t.MinimumSample && m.P95Latency > latency)
        {
            breaches.Add(new(SignalKind.Latency, SignalSeverity.Warning, m.P95Latency.TotalMilliseconds, latency.TotalMilliseconds,
                string.Create(c, $"95th percentile latency {m.P95Latency.TotalMilliseconds:F0} ms exceeds {latency.TotalMilliseconds:F0} ms.")));
        }

        return breaches;
    }
}

/// <summary>The Wilson score interval for a binomial proportion at 95% confidence.</summary>
public static class WilsonInterval
{
    private const double Z = 1.959963984540054;

    /// <summary>The interval for <paramref name="successes"/> out of <paramref name="trials"/>; (0, 1) with no trials.</summary>
    public static (double Lower, double Upper) Of(int successes, int trials)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfLessThan(trials, successes);
        if (trials == 0)
        {
            return (0, 1);
        }

        double n = trials;
        var p = successes / n;
        var z2 = Z * Z;
        var denominator = 1 + (z2 / n);
        var centre = (p + (z2 / (2 * n))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;

namespace Cadence.Modules.Intelligence.Evaluation.Production;

/// <summary>Where production outcomes are kept, per configuration version. The Intelligence module persists them.</summary>
public interface IProposalOutcomeStore
{
    /// <summary>Appends one outcome; a second outcome for the same Proposal is ignored.</summary>
    Task AppendAsync(ProposalOutcomeRecord outcome, CancellationToken cancellationToken);

    /// <summary>The most recent <paramref name="count"/> outcomes of <paramref name="configuration"/>, newest first.</summary>
    Task<IReadOnlyList<ProposalOutcomeRecord>> RecentAsync(ConfigurationRef configuration, int count, CancellationToken cancellationToken);
}

/// <summary>
/// A Quality signal about an AI configuration (ADR-2634 §4.5), for the Quality module to triage. The sink keeps one
/// open signal per <see cref="Key"/>, so a breach that persists across outcomes is raised once.
/// </summary>
/// <param name="Configuration">The configuration version.</param>
/// <param name="Breach">What it breached.</param>
/// <param name="Metrics">The numbers at the time.</param>
/// <param name="RaisedAt">When, from <c>IClock</c>.</param>
public sealed record ConfigurationSignal(ConfigurationRef Configuration, SignalBreach Breach, ProductionMetrics Metrics, DateTimeOffset RaisedAt)
{
    /// <summary>The de-duplication key: configuration version and breach kind.</summary>
    public string Key => $"{Configuration.Key}:v{Configuration.Version}:{Breach.Kind}";
}

/// <summary>Receives Quality signals about AI configurations.</summary>
public interface IQualitySignalSink
{
    /// <summary>Raises <paramref name="signal"/> unless one with the same key is already open.</summary>
    Task RaiseAsync(ConfigurationSignal signal, CancellationToken cancellationToken);
}

/// <summary>
/// Records a production outcome and acts on the monitor's verdict: raises a Quality signal for each breach and
/// suspends the configuration on a safety breach through <see cref="ITaskConfigurationSuspension"/> (ADR-2634 §4.5,
/// ADR-2631). It never changes a configuration's content.
/// </summary>
public sealed class ProductionSignalRecorder(
    IProposalOutcomeStore store,
    IQualitySignalSink signals,
    ITaskConfigurationSuspension suspension,
    IClock clock)
{
    /// <summary>Records <paramref name="outcome"/> of <paramref name="tenant"/> and returns the monitor's report after it.</summary>
    /// <exception cref="InvalidOperationException">A safety breach could not suspend a qualified configuration.</exception>
    public async Task<MonitoringReport> RecordAsync(
        TenantId tenant, ProposalOutcomeRecord outcome, MonitoringThresholds thresholds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(thresholds);
        await store.AppendAsync(outcome, cancellationToken).ConfigureAwait(false);
        var window = await store.RecentAsync(outcome.Configuration, thresholds.WindowSize, cancellationToken).ConfigureAwait(false);
        var report = ConfigurationMonitor.Evaluate(outcome.Configuration, window, thresholds);
        var now = clock.UtcNow;
        foreach (var breach in report.Breaches)
        {
            await signals.RaiseAsync(new ConfigurationSignal(outcome.Configuration, breach, report.Metrics, now), cancellationToken).ConfigureAwait(false);
        }

        if (report.Suspend)
        {
            var reasons = string.Join(" ", report.Breaches.Where(b => b.Severity == SignalSeverity.Suspend).Select(b => b.Detail));
            var suspended = await suspension.SuspendAsync(tenant, outcome.Configuration, reasons, cancellationToken).ConfigureAwait(false);

            // A version the tenant never qualified is already out of use; any other refusal leaves an unsafe one running.
            if (!suspended.IsSuccess && suspended.Error!.Code != IntelligenceErrors.NotQualified.Code)
            {
                throw new InvalidOperationException(
                    $"Could not suspend {outcome.Configuration} after a safety breach: {suspended.Error.Code} {suspended.Error.Message}");
            }
        }

        return report;
    }
}

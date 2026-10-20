using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Production;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence.Evaluation;

/// <summary>
/// Production signals per configuration version raise Quality signals and suspend on safety breaches
/// (ADR-2634 §4.5).
/// </summary>
public sealed class ConfigurationMonitorTests
{
    private static readonly ConfigurationRef Config = EvaluationFixtures.Configuration;
    private static readonly MonitoringThresholds Thresholds = new();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The Wilson interval matches published values.</summary>
    [Theory]
    [InlineData(5, 10, 0.2366, 0.7634)]
    [InlineData(0, 10, 0.0, 0.2775)]
    [InlineData(10, 10, 0.7225, 1.0)]
    public void WilsonIntervalMatchesReferenceValues(int successes, int trials, double lower, double upper)
    {
        var (l, u) = WilsonInterval.Of(successes, trials);

        l.Should().BeApproximately(lower, 1e-4);
        u.Should().BeApproximately(upper, 1e-4);
    }

    /// <summary>A low acceptance rate on too few decisions is noise, not a breach.</summary>
    [Fact]
    public void SmallSampleDoesNotBreach()
    {
        var outcomes = Outcomes(approved: 2, rejected: 8);

        ConfigurationMonitor.Evaluate(Config, outcomes, Thresholds).Breaches.Should().BeEmpty();
    }

    /// <summary>Acceptance confidently below the floor suspends the configuration.</summary>
    [Fact]
    public void LowAcceptanceSuspends()
    {
        var report = ConfigurationMonitor.Evaluate(Config, Outcomes(approved: 15, rejected: 25), Thresholds);

        report.Suspend.Should().BeTrue();
        report.Breaches.Should().ContainSingle().Which.Kind.Should().Be(SignalKind.LowAcceptance);
    }

    /// <summary>Acceptance just below the floor but inside the interval does not breach yet.</summary>
    [Fact]
    public void BorderlineAcceptanceDoesNotBreach()
    {
        var report = ConfigurationMonitor.Evaluate(Config, Outcomes(approved: 26, rejected: 14), Thresholds);

        report.Metrics.AcceptanceRate.Should().BeLessThan(Thresholds.MinimumAcceptanceRate);
        report.Breaches.Should().BeEmpty();
    }

    /// <summary>One proposal rejected as unsafe suspends at once, whatever the sample size.</summary>
    [Fact]
    public void UnsafeRejectionSuspendsAtOnce()
    {
        var outcomes = Outcomes(approved: 3, rejected: 0).Append(Rejected(99, RejectionReason.Unsafe));

        var report = ConfigurationMonitor.Evaluate(Config, outcomes, Thresholds);

        report.Suspend.Should().BeTrue();
        report.Breaches.Single().Kind.Should().Be(SignalKind.UnsafeRejection);
    }

    /// <summary>Heavy editing raises a Quality signal without suspending; cost raises only a warning.</summary>
    [Fact]
    public void EditingAndCostDoNotSuspend()
    {
        var outcomes = Enumerable.Range(0, 40)
            .Select(i => ProposalOutcomeRecord.Approved(Guid.NewGuid(), Config, "Please confirm the value.", "Please confirm the systolic value against the source document.", 0, 0.5m, TimeSpan.FromSeconds(1), Start.AddMinutes(i)))
            .ToList();

        var report = ConfigurationMonitor.Evaluate(Config, outcomes, Thresholds with { MaximumMeanCost = 0.1m });

        report.Suspend.Should().BeFalse();
        report.Breaches.Select(b => (b.Kind, b.Severity)).Should().BeEquivalentTo(
            [(SignalKind.HighEditDistance, SignalSeverity.Quality), (SignalKind.Cost, SignalSeverity.Warning)]);
    }

    /// <summary>Outcomes of another configuration version never count against this one, and the window keeps the newest.</summary>
    [Fact]
    public void WindowIsPerVersionAndNewestFirst()
    {
        var other = Config with { Version = 2 };
        var outcomes = Outcomes(approved: 0, rejected: 40, configuration: other)
            .Concat(Outcomes(approved: 10, rejected: 0, offsetMinutes: 1000))
            .Concat(Outcomes(approved: 0, rejected: 40, offsetMinutes: 0));

        var report = ConfigurationMonitor.Evaluate(Config, outcomes, Thresholds with { WindowSize = 10, MinimumSample = 5 });

        report.Metrics.Outcomes.Should().Be(10);
        report.Metrics.AcceptanceRate.Should().Be(1);
    }

    /// <summary>The recorder stores the outcome, raises each breach and suspends the configuration on a safety breach.</summary>
    [Fact]
    public async Task RecorderRaisesSignalsAndSuspends()
    {
        var store = new MemoryOutcomeStore();
        var sink = new RecordingSink();
        var suspension = new RecordingSuspension();
        var recorder = new ProductionSignalRecorder(store, sink, suspension, new FakeClock(Start));

        var report = await recorder.RecordAsync(Rejected(1, RejectionReason.Unsafe), Thresholds, TestContext.Current.CancellationToken);

        store.Stored.Should().ContainSingle();
        sink.Signals.Should().ContainSingle().Which.Key.Should().Be("data-review.query-draft:v1:UnsafeRejection");
        suspension.Suspended.Should().ContainSingle().Which.Configuration.Should().Be(Config);
        report.Suspend.Should().BeTrue();
    }

    /// <summary>Edit distance is 0 for unchanged text and 1 when nothing survived.</summary>
    [Theory]
    [InlineData("same", "same", 0.0)]
    [InlineData("", "", 0.0)]
    [InlineData("abc", "xyz", 1.0)]
    [InlineData("kitten", "sitting", 3.0 / 7)]
    public void EditDistanceIsNormalized(string drafted, string approved, double expected) =>
        EditDistance.Normalized(drafted, approved).Should().BeApproximately(expected, 1e-9);

    private static IEnumerable<ProposalOutcomeRecord> Outcomes(int approved, int rejected, ConfigurationRef? configuration = null, int offsetMinutes = 0) =>
        Enumerable.Range(0, approved)
            .Select(i => ProposalOutcomeRecord.Approved(Guid.NewGuid(), configuration ?? Config, "Please confirm.", "Please confirm.", 0, 0.002m, TimeSpan.FromSeconds(1), Start.AddMinutes(offsetMinutes + i)))
            .Concat(Enumerable.Range(0, rejected).Select(i => Rejected(offsetMinutes + approved + i, RejectionReason.Incorrect, configuration)))
            .ToList();

    private static ProposalOutcomeRecord Rejected(int minute, RejectionReason reason, ConfigurationRef? configuration = null) =>
        ProposalOutcomeRecord.Rejected(Guid.NewGuid(), configuration ?? Config, reason, 0, 0.002m, TimeSpan.FromSeconds(1), Start.AddMinutes(minute));

    private sealed class MemoryOutcomeStore : IProposalOutcomeStore
    {
        public List<ProposalOutcomeRecord> Stored { get; } = [];

        public Task AppendAsync(ProposalOutcomeRecord outcome, CancellationToken cancellationToken)
        {
            Stored.Add(outcome);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProposalOutcomeRecord>> RecentAsync(ConfigurationRef configuration, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProposalOutcomeRecord>>([.. Stored.Where(o => o.Configuration == configuration).OrderByDescending(o => o.DecidedAt).Take(count)]);
    }

    private sealed class RecordingSink : IQualitySignalSink
    {
        public List<ConfigurationSignal> Signals { get; } = [];

        public Task RaiseAsync(ConfigurationSignal signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSuspension : ITaskConfigurationSuspension
    {
        public List<(ConfigurationRef Configuration, string Reason)> Suspended { get; } = [];

        public Task SuspendAsync(ConfigurationRef configuration, string reason, CancellationToken cancellationToken)
        {
            Suspended.Add((configuration, reason));
            return Task.CompletedTask;
        }
    }
}

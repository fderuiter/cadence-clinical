using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain.Sla;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/sla/engine.py</c> and <c>sla/models.py</c> (#5048).
/// Expected values were produced by running the Python reference on the same inputs.
/// </summary>
public class SlaEngineTests
{
    private static readonly SlaPolicy QueryPolicy = new()
    {
        PolicyId = "POL-1",
        Name = "Query resolution",
        PausedStates = ["ON_HOLD", "AWAITING_SITE"],
        CategoryMultipliers = new Dictionary<string, double> { ["SAFETY"] = 0.5, ["DATA"] = 1.5 },
    };

    private static readonly SlaPolicy BusinessPolicy = new()
    {
        PolicyId = "POL-B",
        Name = "Business",
        Schedule = new BusinessHoursSchedule { Is24x7 = false, Holidays = [new DateOnly(2026, 3, 9)] },
    };

    private static readonly SlaPolicy EscalationPolicy = new()
    {
        PolicyId = "POL-E",
        Name = "Esc",
        EscalationTiers =
        [
            new EscalationTier("t3", "Breach", new EscalationAction("CRITICAL", ["sponsor", "lead_monitor"]), 100.0),
            new EscalationTier("t1", "Heads-up", new EscalationAction(NotifyRoles: ["cra"]), 50.0),
            new EscalationTier("t2", "Manager's call", new EscalationAction("HIGH", ["lead_monitor", "cra"]), 80.0),
        ],
    };

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.Zero);

    /// <summary>The checksum equals the one the Python model sealed the same policy with.</summary>
    [Fact]
    public void ComputeChecksum_MatchesPythonReference()
    {
        QueryPolicy.ComputeChecksum().Should().Be("95b82704248db383a9d9c87cc6ef3c558b87442bb8b1730509e83e149d466b04"); // pragma: allowlist secret
    }

    /// <summary>Non-ASCII text, quotes, small floats and exponents are written as Python's json.dumps writes them.</summary>
    [Fact]
    public void ComputeChecksum_EscapesTextAndFormatsFloatsAsPython()
    {
        var policy = new SlaPolicy
        {
            PolicyId = "POL-2",
            Name = "Déviation \"urgent\"",
            BaseHoursByPriority = new Dictionary<string, double> { ["HIGH"] = 12.5, ["LOW"] = 0.0001, ["TINY"] = 1e-05 },
            WarningThresholdPct = 60.0,
            CriticalThresholdPct = 85.5,
            VersionIndex = 3,
        };

        policy.ComputeChecksum().Should().Be("99461a6c1e53cbe47b755e0caede05732c5c99149b9a4abbdb1687febd954e5c"); // pragma: allowlist secret
    }

    /// <summary>The checksum changes when a sealed field changes.</summary>
    [Fact]
    public void ComputeChecksum_ChangesWithThresholds()
    {
        (QueryPolicy with { WarningThresholdPct = 70.0 }).ComputeChecksum().Should().NotBe(QueryPolicy.ComputeChecksum());
    }

    /// <summary>Around the clock: priority hours (any case), category multipliers, custom hours and the 72-hour fallback.</summary>
    [Theory]
    [InlineData("high", null, null, 3, 3, 10, 0)]
    [InlineData("MEDIUM", "SAFETY", null, 3, 3, 22, 0)]
    [InlineData("UNKNOWN", null, 1.5, 3, 2, 11, 30)]
    [InlineData("UNKNOWN", null, null, 3, 5, 10, 0)]
    public void ComputeSlaDueDate_AroundTheClock(string priority, string? category, double? customHours, int month, int day, int hour, int minute)
    {
        var engine = new SlaEngine(QueryPolicy);

        engine.ComputeSlaDueDate(Utc(3, 2, 10), priority, category, customHours).Should().Be(Utc(month, day, hour, minute));
    }

    /// <summary>
    /// Business hours skip evenings, weekends and holidays (2026-03-06 is a Friday, 2026-03-09 a holiday), move an
    /// early start to the opening hour, and ignore the seconds of a start time as the Python did.
    /// </summary>
    [Theory]
    [InlineData(6, 15, 30, 0, null, "HIGH", 12, 15, 30)]
    [InlineData(6, 7, 0, 0, 4.0, "x", 6, 13, 0)]
    [InlineData(7, 12, 0, 0, 2.0, "x", 10, 11, 0)]
    [InlineData(5, 18, 0, 0, 8.0, "x", 6, 17, 0)]
    [InlineData(6, 16, 45, 20, 0.5, "x", 10, 9, 15)]
    public void ComputeSlaDueDate_BusinessHours(
        int startDay, int startHour, int startMinute, int startSecond, double? customHours, string priority, int dueDay, int dueHour, int dueMinute)
    {
        var engine = new SlaEngine(BusinessPolicy);

        engine.ComputeSlaDueDate(Utc(3, startDay, startHour, startMinute, startSecond), priority, customHours: customHours)
            .Should().Be(Utc(3, dueDay, dueHour, dueMinute));
    }

    /// <summary>Paused time moves the due date and does not count as elapsed; thresholds set the alert level.</summary>
    [Theory]
    [InlineData(1, 12, 0.0, 0, 24.0, 12.0, 50.0, 12.0, false, SlaAlertLevel.Normal)]
    [InlineData(1, 20, 3600.0, 1, 24.0, 19.0, 79.17, 5.0, false, SlaAlertLevel.Warning)]
    [InlineData(1, 22, 0.0, 0, 24.0, 22.0, 91.67, 2.0, false, SlaAlertLevel.Critical)]
    [InlineData(2, 6, 7200.0, 2, 24.0, 28.0, 116.67, 0.0, true, SlaAlertLevel.Overdue)]
    [InlineData(2, 1, 7200.0, 2, 24.0, 23.0, 95.83, 1.0, false, SlaAlertLevel.Critical)]
    public void ComputeCountdownClock_MatchesPythonReference(
        int asOfDay, int asOfHour, double pausedSeconds, int effectiveDueHour,
        double allocated, double elapsed, double pct, double remaining, bool overdue, SlaAlertLevel level)
    {
        var engine = new SlaEngine(QueryPolicy);

        var clock = engine.ComputeCountdownClock(Utc(3, 1, 0), Utc(3, 2, 0), Utc(3, asOfDay, asOfHour), pausedSeconds);

        clock.EffectiveDueDate.Should().Be(Utc(3, 2, effectiveDueHour));
        clock.TotalAllocatedHours.Should().Be(allocated);
        clock.TotalPausedSeconds.Should().Be(pausedSeconds);
        clock.ElapsedHours.Should().Be(elapsed);
        clock.ElapsedPercentage.Should().Be(pct);
        clock.RemainingHours.Should().Be(remaining);
        clock.IsOverdue.Should().Be(overdue);
        clock.AlertLevel.Should().Be(level);
    }

    /// <summary>A clock read before its start has elapsed nothing; negative pause time is ignored but reported as given.</summary>
    [Fact]
    public void ComputeCountdownClock_BeforeStartAndNegativePause()
    {
        var clock = new SlaEngine(QueryPolicy).ComputeCountdownClock(Utc(3, 1, 0), Utc(3, 2, 0), Utc(2, 28, 0), -50);

        clock.EffectiveDueDate.Should().Be(Utc(3, 2, 0));
        clock.TotalPausedSeconds.Should().Be(-50);
        clock.ElapsedHours.Should().Be(0);
        clock.ElapsedPercentage.Should().Be(0);
        clock.RemainingHours.Should().Be(48);
        clock.AlertLevel.Should().Be(SlaAlertLevel.Normal);
    }

    /// <summary>A due date before the start counts as one hour allowed.</summary>
    [Fact]
    public void ComputeCountdownClock_DueBeforeStart_AllowsOneHour()
    {
        var clock = new SlaEngine(QueryPolicy).ComputeCountdownClock(Utc(3, 2, 0), Utc(3, 1, 0), Utc(3, 2, 0, 30));

        clock.TotalAllocatedHours.Should().Be(1.0);
        clock.ElapsedPercentage.Should().Be(50.0);
        clock.AlertLevel.Should().Be(SlaAlertLevel.Overdue);
    }

    /// <summary>Reached tiers come lowest threshold first; roles are merged and sorted; the summary reads as the Python's.</summary>
    [Fact]
    public void EvaluateEscalations_ReachedTiers()
    {
        var result = new SlaEngine(EscalationPolicy).EvaluateEscalations(Clock(85.0), "MEDIUM");

        result.TriggeredTiers.Select(t => t.TierId).Should().Equal("t1", "t2");
        result.RecommendedPriority.Should().Be("HIGH");
        result.NotificationRoles.Should().Equal("cra", "lead_monitor");
        result.AuditSummary.Should().Be(
            "SLA status: WARNING (85.0% elapsed). Triggered tiers: ['Heads-up', \"Manager's call\"]. Recommended priority: HIGH.");
    }

    /// <summary>Every tier past the breach; the last tier that sets a priority wins.</summary>
    [Fact]
    public void EvaluateEscalations_AllTiers()
    {
        var result = new SlaEngine(EscalationPolicy).EvaluateEscalations(Clock(120.5), "LOW");

        result.TriggeredTiers.Select(t => t.TierId).Should().Equal("t1", "t2", "t3");
        result.RecommendedPriority.Should().Be("CRITICAL");
        result.NotificationRoles.Should().Equal("cra", "lead_monitor", "sponsor");
        result.AuditSummary.Should().Be(
            "SLA status: WARNING (120.5% elapsed). Triggered tiers: ['Heads-up', \"Manager's call\", 'Breach']. Recommended priority: CRITICAL.");
    }

    /// <summary>With no tier reached nothing is recommended, and the summary names the current priority or None.</summary>
    [Theory]
    [InlineData(null, "None")]
    [InlineData("LOW", "LOW")]
    public void EvaluateEscalations_NoTierReached(string? currentPriority, string named)
    {
        var result = new SlaEngine(EscalationPolicy).EvaluateEscalations(Clock(30.0), currentPriority);

        result.TriggeredTiers.Should().BeEmpty();
        result.RecommendedPriority.Should().BeNull();
        result.NotificationRoles.Should().BeEmpty();
        result.AuditSummary.Should().Be($"SLA status: WARNING (30.0% elapsed). Triggered tiers: []. Recommended priority: {named}.");
    }

    /// <summary>Paused states match exactly, as the Python's list membership did.</summary>
    [Fact]
    public void IsStatePaused_IsExactMatch()
    {
        var engine = new SlaEngine(QueryPolicy);

        engine.IsStatePaused("ON_HOLD").Should().BeTrue();
        engine.IsStatePaused("on_hold").Should().BeFalse();
        engine.IsStatePaused("OPEN").Should().BeFalse();
    }

    /// <summary>A schedule that can never run is refused (the Python loop never ended on one).</summary>
    [Fact]
    public void Constructor_RejectsScheduleWithoutWorkDays()
    {
        var policy = BusinessPolicy with { Schedule = BusinessPolicy.Schedule with { WorkDays = [] } };

        var act = () => new SlaEngine(policy);

        act.Should().Throw<ArgumentException>().WithMessage("*at least one work day*");
    }

    /// <summary>Business hours must end after they start.</summary>
    [Fact]
    public void Constructor_RejectsInvertedHours()
    {
        var policy = BusinessPolicy with { Schedule = BusinessPolicy.Schedule with { WorkStartHour = 17, WorkEndHour = 9 } };

        var act = () => new SlaEngine(policy);

        act.Should().Throw<ArgumentException>().WithMessage("*got 17 to 9*");
    }

    private static CountdownClock Clock(double elapsedPercentage) =>
        new(Utc(3, 1, 0), Utc(3, 2, 0), Utc(3, 2, 0), 1, 0, 1, elapsedPercentage, 0, false, SlaAlertLevel.Warning);
}

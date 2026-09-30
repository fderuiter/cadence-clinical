using System;
using System.Globalization;

using Cadence.Kernel.Protocol.Design;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The expected schedule as a pure function of the design, the reference start and the as-of date (K-TIME-7,
/// ADR-2487), with study days on the SDTM <c>--DY</c> convention.
/// </summary>
public sealed class ExpectedScheduleTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);

    /// <summary>Day 1 is the reference start and day -1 the day before; there is no day 0, so a day-0 timing is day 1.</summary>
    [Theory]
    [InlineData(1, "2026-10-01")]
    [InlineData(2, "2026-10-02")]
    [InlineData(29, "2026-10-29")]
    [InlineData(0, "2026-10-01")]
    [InlineData(-1, "2026-09-30")]
    [InlineData(-14, "2026-09-17")]
    public void DateOfStudyDay_FollowsTheSdtmConvention(int studyDay, string expected) =>
        ExpectedSchedule.DateOfStudyDay(Day1, studyDay).Should().Be(DateOnly.Parse(expected, CultureInfo.InvariantCulture));

    /// <summary>
    /// Only timed activities are on the schedule, ordered by study day, then epoch order, then activity id; a day-0
    /// timing sorts with day 1.
    /// </summary>
    [Fact]
    public void Plan_OrdersTimedActivitiesAndLeavesOutUntimedOnes()
    {
        var design = Design(
            Activity("act.b", "ep.treatment", new ActivityTiming(1)),
            Activity("act.ae", "ep.treatment", null),
            Activity("act.a", "ep.treatment", new ActivityTiming(0)),
            Activity("act.baseline", "ep.screening", new ActivityTiming(1)),
            Activity("act.week4", "ep.treatment", new ActivityTiming(28, 2, 2)),
            Activity("act.screening", "ep.screening", new ActivityTiming(-14)));

        var plan = ExpectedSchedule.Plan(design);

        plan.Should().Equal(
            new PlannedVisit("act.screening", "act.screening", "ep.screening", 1, -14, 0, 0),
            new PlannedVisit("act.baseline", "act.baseline", "ep.screening", 1, 1, 0, 0),
            new PlannedVisit("act.a", "act.a", "ep.treatment", 2, 0, 0, 0),
            new PlannedVisit("act.b", "act.b", "ep.treatment", 2, 1, 0, 0),
            new PlannedVisit("act.week4", "act.week4", "ep.treatment", 2, 28, 2, 2));
    }

    /// <summary>A design with no timed activities has an empty schedule.</summary>
    [Fact]
    public void Plan_OfADesignWithoutTimingIsEmpty() =>
        ExpectedSchedule.Plan(ProtocolDesignSamples.MinimalDesign()).Should().BeEmpty();

    /// <summary>
    /// Each visit gets its target date and window from the reference start, and its status from the as-of date: the
    /// window's first and last days count as open.
    /// </summary>
    [Theory]
    [InlineData("2026-10-26", VisitWindowStatus.NotYetOpen)]
    [InlineData("2026-10-27", VisitWindowStatus.Open)]
    [InlineData("2026-10-29", VisitWindowStatus.Open)]
    [InlineData("2026-10-31", VisitWindowStatus.Open)]
    [InlineData("2026-11-01", VisitWindowStatus.Closed)]
    public void Compute_PlacesVisitsAndTheirWindowsOnTheCalendar(string asOf, VisitWindowStatus status)
    {
        var design = Design(Activity("act.week4", "ep.treatment", new ActivityTiming(29, 2, 2)));

        var schedule = ExpectedSchedule.Compute(design, Day1, DateOnly.Parse(asOf, CultureInfo.InvariantCulture));

        schedule.Should().ContainSingle().Which.Should().Be(new ExpectedVisit(
            new PlannedVisit("act.week4", "act.week4", "ep.treatment", 2, 29, 2, 2),
            new DateOnly(2026, 10, 29),
            new DateOnly(2026, 10, 27),
            new DateOnly(2026, 10, 31),
            status));
    }

    /// <summary>The same inputs always give the same schedule: nothing is read from a clock or stored (K-TIME-7).</summary>
    [Fact]
    public void Compute_IsAPureFunctionOfItsInputs()
    {
        var design = Design(
            Activity("act.screening", "ep.screening", new ActivityTiming(-14, 0, 3)),
            Activity("act.week4", "ep.treatment", new ActivityTiming(29, 2, 2)));
        var asOf = new DateOnly(2026, 10, 15);

        ExpectedSchedule.Compute(design, Day1, asOf).Should().Equal(ExpectedSchedule.Compute(design, Day1, asOf));
        ExpectedSchedule.Compute(design, Day1, asOf)[0].Status.Should().Be(VisitWindowStatus.Closed);
    }

    private static ProtocolDesign Design(params PlannedActivity[] activities) => ProtocolDesignSamples.MinimalDesign() with
    {
        Epochs = [new Epoch("ep.treatment", "Treatment", 2), new Epoch("ep.screening", "Screening", 1)],
        PlannedActivities = [.. activities],
    };

    private static PlannedActivity Activity(string id, string epochId, ActivityTiming? timing) =>
        new(id, id, epochId, [ActivityLocation.Site], [], timing);
}

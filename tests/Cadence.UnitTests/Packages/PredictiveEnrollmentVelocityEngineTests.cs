using System;
using System.Linq;

using Cadence.Core.Domain.Enrollment;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class PredictiveEnrollmentVelocityEngineTests
{
    [Fact]
    public void SimulateEnrollment_WithValidParameters_CalculatesForecastCorrectly()
    {
        // Arrange
        var today = new DateOnly(2026, 10, 1);
        var targetDate = new DateOnly(2027, 4, 1); // ~26 weeks
        var paramsVal = new EnrollmentVelocityParams(
            StudyId: "STUDY-ONC-101",
            TargetSubjects: 200,
            TargetCompletionDate: targetDate,
            HistoricalWeeklyRate: 8.0,
            CurrentlyEnrolled: 50,
            ActiveSiteCount: 5,
            SimulationHorizonWeeks: 12);

        // Act
        var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(paramsVal, today);

        // Assert
        result.Should().NotBeNull();
        result.StudyId.Should().Be("STUDY-ONC-101");
        result.CurrentVelocityWeekly.Should().Be(8.0);
        result.CurrentlyEnrolled.Should().Be(50);
        result.TargetSubjects.Should().Be(200);
        result.WeeklyProjections.Should().HaveCount(12);

        // Check cumulative growth
        var week1 = result.WeeklyProjections[0];
        week1.WeekIndex.Should().Be(1);
        week1.ProjectedCumulativeEnrolled.Should().BeGreaterThan(50);
        week1.LowerBoundCI.Should().BeLessThanOrEqualTo(week1.ProjectedCumulativeEnrolled);
        week1.UpperBoundCI.Should().BeGreaterThanOrEqualTo(week1.ProjectedCumulativeEnrolled);
    }

    [Fact]
    public void SimulateEnrollment_WhenVelocityIsAhead_AssignsAheadStatus()
    {
        // Arrange
        var today = new DateOnly(2026, 10, 1);
        var targetDate = new DateOnly(2027, 10, 1); // 52 weeks away -> ~3 subjects/week required for 150 remaining
        var paramsVal = new EnrollmentVelocityParams(
            StudyId: "STUDY-CARD-202",
            TargetSubjects: 200,
            TargetCompletionDate: targetDate,
            HistoricalWeeklyRate: 10.0, // well above target rate
            CurrentlyEnrolled: 50,
            ActiveSiteCount: 4,
            SimulationHorizonWeeks: 8);

        // Act
        var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(paramsVal, today);

        // Assert
        result.Status.Should().Be(VelocityStatus.AHEAD);
        result.DaysAheadOrBehind.Should().BeLessThan(0); // Finished early
    }

    [Fact]
    public void SimulateEnrollment_WhenVelocityIsLagging_AssignsBehindStatus()
    {
        // Arrange
        var today = new DateOnly(2026, 10, 1);
        var targetDate = new DateOnly(2026, 12, 1); // 8 weeks away -> ~12.5 subjects/week required for 100 remaining
        var paramsVal = new EnrollmentVelocityParams(
            StudyId: "STUDY-NEURO-303",
            TargetSubjects: 150,
            TargetCompletionDate: targetDate,
            HistoricalWeeklyRate: 2.0, // far below target rate
            CurrentlyEnrolled: 50,
            ActiveSiteCount: 3,
            SimulationHorizonWeeks: 8);

        // Act
        var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(paramsVal, today);

        // Assert
        result.Status.Should().Be(VelocityStatus.BEHIND);
        result.DaysAheadOrBehind.Should().BeGreaterThan(0); // Delayed
    }

    [Fact]
    public void SimulateEnrollment_WhenFullyEnrolled_ReturnsOnTrackWithCurrentDate()
    {
        // Arrange
        var today = new DateOnly(2026, 10, 1);
        var targetDate = new DateOnly(2026, 12, 1);
        var paramsVal = new EnrollmentVelocityParams(
            StudyId: "STUDY-COMPLETED",
            TargetSubjects: 100,
            TargetCompletionDate: targetDate,
            HistoricalWeeklyRate: 0.0,
            CurrentlyEnrolled: 100, // target met
            ActiveSiteCount: 2,
            SimulationHorizonWeeks: 4);

        // Act
        var result = PredictiveEnrollmentVelocityEngine.SimulateEnrollment(paramsVal, today);

        // Assert
        result.Status.Should().Be(VelocityStatus.ON_TRACK);
        result.ForecastedCompletionDate.Should().Be(today);
    }

    [Fact]
    public void SimulateEnrollment_WithInvalidParameters_ThrowsArgumentException()
    {
        var today = new DateOnly(2026, 10, 1);

        // Negative target
        Action act1 = () => PredictiveEnrollmentVelocityEngine.SimulateEnrollment(
            new EnrollmentVelocityParams("STUDY-1", -10, today, 5.0, 0, 1), today);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        // Horizon out of bounds
        Action act2 = () => PredictiveEnrollmentVelocityEngine.SimulateEnrollment(
            new EnrollmentVelocityParams("STUDY-1", 100, today, 5.0, 0, 1, SimulationHorizonWeeks: 200), today);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }
}

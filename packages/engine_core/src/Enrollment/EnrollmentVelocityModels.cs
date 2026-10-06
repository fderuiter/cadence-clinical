using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain.Enrollment;

public enum VelocityStatus
{
    ON_TRACK,
    AT_RISK,
    BEHIND,
    AHEAD
}

public record EnrollmentVelocityParams(
    string StudyId,
    int TargetSubjects,
    DateOnly TargetCompletionDate,
    double HistoricalWeeklyRate,
    int CurrentlyEnrolled,
    int ActiveSiteCount,
    int SimulationHorizonWeeks = 24);

public record WeeklyEnrollmentProjection(
    int WeekIndex,
    DateOnly WeekStartDate,
    int ProjectedCumulativeEnrolled,
    int LowerBoundCI,
    int UpperBoundCI,
    int TargetCumulative,
    double WeeklyRate);

public record SiteEnrollmentVelocity(
    string SiteId,
    string SiteName,
    int ActiveSubjects,
    double HistoricalWeeklyRate,
    double ProjectedWeeklyRate,
    string VelocityRiskLevel);

public record EnrollmentVelocityForecastResult(
    string StudyId,
    double CurrentVelocityWeekly,
    double TargetVelocityWeekly,
    VelocityStatus Status,
    DateOnly ForecastedCompletionDate,
    int DaysAheadOrBehind,
    int TargetSubjects,
    int CurrentlyEnrolled,
    IReadOnlyList<WeeklyEnrollmentProjection> WeeklyProjections,
    IReadOnlyList<SiteEnrollmentVelocity> SiteVelocities);

using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Enrollment;

public static class PredictiveEnrollmentVelocityEngine
{
    public static EnrollmentVelocityForecastResult SimulateEnrollment(
        EnrollmentVelocityParams paramsVal,
        DateOnly asOfDate,
        IReadOnlyList<SiteEnrollmentVelocity>? siteData = null)
    {
        ArgumentNullException.ThrowIfNull(paramsVal);
        Validate(paramsVal);

        int target = paramsVal.TargetSubjects;
        int currentlyEnrolled = paramsVal.CurrentlyEnrolled;
        int remaining = Math.Max(0, target - currentlyEnrolled);

        int daysToTargetDate = paramsVal.TargetCompletionDate.DayNumber - asOfDate.DayNumber;
        double weeksToTarget = Math.Max(1.0, daysToTargetDate / 7.0);

        double targetVelocityWeekly = remaining > 0 ? Math.Round(remaining / weeksToTarget, 2) : 0.0;
        double currentVelocityWeekly = Math.Round(paramsVal.HistoricalWeeklyRate, 2);

        DateOnly forecastedCompletionDate;
        if (remaining == 0)
        {
            forecastedCompletionDate = asOfDate;
        }
        else if (currentVelocityWeekly <= 0.001)
        {
            forecastedCompletionDate = asOfDate.AddDays(3650); // 10 years cap for stalled velocity
        }
        else
        {
            double weeksNeeded = remaining / currentVelocityWeekly;
            int daysNeeded = (int)Math.Ceiling(weeksNeeded * 7.0);
            forecastedCompletionDate = asOfDate.AddDays(daysNeeded);
        }

        int daysAheadOrBehind = forecastedCompletionDate.DayNumber - paramsVal.TargetCompletionDate.DayNumber;

        VelocityStatus status;
        if (remaining == 0)
        {
            status = VelocityStatus.ON_TRACK;
        }
        else if (currentVelocityWeekly >= targetVelocityWeekly * 1.1)
        {
            status = VelocityStatus.AHEAD;
        }
        else if (currentVelocityWeekly >= targetVelocityWeekly * 0.9)
        {
            status = VelocityStatus.ON_TRACK;
        }
        else if (currentVelocityWeekly >= targetVelocityWeekly * 0.7)
        {
            status = VelocityStatus.AT_RISK;
        }
        else
        {
            status = VelocityStatus.BEHIND;
        }

        var weeklyProjections = new List<WeeklyEnrollmentProjection>();
        int horizon = paramsVal.SimulationHorizonWeeks;

        for (int week = 1; week <= horizon; week++)
        {
            DateOnly weekStartDate = asOfDate.AddDays(week * 7);

            int projectedAdded = Math.Min(remaining, (int)Math.Round(currentVelocityWeekly * week));
            int projectedCumulative = Math.Min(target, currentlyEnrolled + projectedAdded);

            double ciFactor = Math.Min(0.25, 0.05 + 0.008 * week);
            int lowerBound = Math.Max(currentlyEnrolled, (int)Math.Floor(projectedCumulative * (1.0 - ciFactor)));
            int upperBound = Math.Min(target, (int)Math.Ceiling(projectedCumulative * (1.0 + ciFactor)));

            int targetAdded = Math.Min(remaining, (int)Math.Round(targetVelocityWeekly * week));
            int targetCumulative = Math.Min(target, currentlyEnrolled + targetAdded);

            weeklyProjections.Add(new WeeklyEnrollmentProjection(
                WeekIndex: week,
                WeekStartDate: weekStartDate,
                ProjectedCumulativeEnrolled: projectedCumulative,
                LowerBoundCI: lowerBound,
                UpperBoundCI: upperBound,
                TargetCumulative: targetCumulative,
                WeeklyRate: currentVelocityWeekly));
        }

        IReadOnlyList<SiteEnrollmentVelocity> sites = siteData ?? GenerateDefaultSiteVelocities(paramsVal);

        return new EnrollmentVelocityForecastResult(
            StudyId: paramsVal.StudyId,
            CurrentVelocityWeekly: currentVelocityWeekly,
            TargetVelocityWeekly: targetVelocityWeekly,
            Status: status,
            ForecastedCompletionDate: forecastedCompletionDate,
            DaysAheadOrBehind: daysAheadOrBehind,
            TargetSubjects: target,
            CurrentlyEnrolled: currentlyEnrolled,
            WeeklyProjections: weeklyProjections,
            SiteVelocities: sites);
    }

    private static IReadOnlyList<SiteEnrollmentVelocity> GenerateDefaultSiteVelocities(EnrollmentVelocityParams p)
    {
        int siteCount = Math.Max(1, p.ActiveSiteCount);
        double avgRate = p.HistoricalWeeklyRate / siteCount;
        var list = new List<SiteEnrollmentVelocity>();

        for (int i = 1; i <= siteCount; i++)
        {
            string siteId = $"SITE-{i:D3}";
            double siteRate = Math.Round(avgRate * (0.8 + 0.4 * (i % 3) / 2.0), 2);
            string risk;
            if (siteRate >= avgRate)
            {
                risk = "LOW";
            }
            else if (siteRate >= avgRate * 0.6)
            {
                risk = "MEDIUM";
            }
            else
            {
                risk = "HIGH";
            }

            list.Add(new SiteEnrollmentVelocity(
                SiteId: siteId,
                SiteName: $"Clinical Site #{i}",
                ActiveSubjects: Math.Max(1, p.CurrentlyEnrolled / siteCount),
                HistoricalWeeklyRate: siteRate,
                ProjectedWeeklyRate: siteRate,
                VelocityRiskLevel: risk));
        }

        return list;
    }

    private static void Validate(EnrollmentVelocityParams p)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(p.StudyId);
        ArgumentOutOfRangeException.ThrowIfNegative(p.TargetSubjects);
        ArgumentOutOfRangeException.ThrowIfNegative(p.CurrentlyEnrolled);
        ArgumentOutOfRangeException.ThrowIfNegative(p.ActiveSiteCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(p.SimulationHorizonWeeks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p.SimulationHorizonWeeks, 104);

        if (!double.IsFinite(p.HistoricalWeeklyRate) || p.HistoricalWeeklyRate < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), p.HistoricalWeeklyRate, "HistoricalWeeklyRate must be finite and non-negative.");
        }
    }
}

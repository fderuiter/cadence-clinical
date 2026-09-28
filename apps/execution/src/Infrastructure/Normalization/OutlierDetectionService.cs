using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Infrastructure.Normalization;

/// <summary>
/// Statistical outlier detection engine using Interquartile Range (IQR) with 3.5 multiplier.
/// Conforms to ADR-0117 and legacy Python <c>outliers.py</c>.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class OutlierDetectionService : IOutlierDetectionService
{
    private const double IqrMultiplier = 3.5;

    public bool IsOutlier(double value, IReadOnlyList<double> cohortValues)
    {
        if (cohortValues.Count < 4)
        {
            return false;
        }

        var sorted = cohortValues.OrderBy(v => v).ToList();

        double q1 = GetPercentile(sorted, 0.25);
        double q3 = GetPercentile(sorted, 0.75);
        double iqr = q3 - q1;

        if (iqr <= 0.000001)
        {
            return false;
        }

        double lowerBound = q1 - (IqrMultiplier * iqr);
        double upperBound = q3 + (IqrMultiplier * iqr);

        return value < lowerBound || value > upperBound;
    }

    public int RecalculateOutliers(IList<ClinicalObservation> observations)
    {
        var numericObs = observations
            .Where(o => (o.NormalizedValue ?? o.Value).HasValue)
            .ToList();

        if (numericObs.Count < 4)
        {
            foreach (var obs in observations)
            {
                obs.SetOutlier(false);
            }
            return 0;
        }

        var values = numericObs.Select(o => (o.NormalizedValue ?? o.Value)!.Value).ToList();
        int outliersCount = 0;

        foreach (var obs in observations)
        {
            double? val = obs.NormalizedValue ?? obs.Value;
            if (val.HasValue && IsOutlier(val.Value, values))
            {
                obs.SetOutlier(true);
                outliersCount++;
            }
            else
            {
                obs.SetOutlier(false);
            }
        }

        return outliersCount;
    }

    private static double GetPercentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        double rank = percentile * (sorted.Count - 1);
        int lowIndex = (int)Math.Floor(rank);
        int highIndex = (int)Math.Ceiling(rank);

        if (lowIndex == highIndex)
        {
            return sorted[lowIndex];
        }

        double fraction = rank - lowIndex;
        return sorted[lowIndex] + fraction * (sorted[highIndex] - sorted[lowIndex]);
    }
}

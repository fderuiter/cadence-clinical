using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Service abstraction for statistical outlier calculation across clinical observation cohorts.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public interface IOutlierDetectionService
{
    bool IsOutlier(double value, IReadOnlyList<double> cohortValues);

    int RecalculateOutliers(IList<ClinicalObservation> observations);
}

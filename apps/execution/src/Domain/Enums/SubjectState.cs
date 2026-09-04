namespace Cadence.Apps.Execution.Domain.Enums;

/// <summary>
/// Regulated lifecycle states for a <c>ClinicalSubject</c> across screening, randomization, and follow-up.
/// @req:PRD-SUB-001
/// </summary>
public enum SubjectState
{
    Screening,
    ScreenFailed,
    Enrolled,
    Randomized,
    Active,
    Completed,
    Unblinded,
    Withdrawn,
    ReconsentRequired
}

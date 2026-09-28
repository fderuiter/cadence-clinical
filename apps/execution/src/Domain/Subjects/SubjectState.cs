namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// The 9 lifecycle states of a <c>ClinicalSubject</c> (Rule 9's `normalize_state` domain).
/// @req:PRD-SUB-001
/// </summary>
public enum SubjectState
{
    Screening,
    ScreenFailed,
    Enrolled,
    Randomized,
    Active,
    Unblinded,
    Completed,
    Withdrawn,
    ReconsentRequired
}

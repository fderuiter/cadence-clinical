namespace Cadence.Apps.Execution.Domain.Queries;

/// <summary>
/// Status lifecycle of a clinical discrepancy query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public enum QueryStatus
{
    None,
    Candidate,
    DraftAi,
    Open,
    Answered,
    Resolved,
    Closed,
    Reopened,
    Cancelled
}

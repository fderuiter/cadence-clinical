namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Status lifecycle of an eCRF form submission.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public enum FormSubmissionStatus
{
    Draft,
    Completed,
    Approved
}

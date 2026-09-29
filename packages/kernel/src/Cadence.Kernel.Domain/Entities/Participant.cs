using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical Participant aggregate root representing a human subject enrolled in a Study at a StudySite.
/// @req:PRD-SYS-053
/// </summary>
public class Participant : AggregateRoot<ParticipantId>
{
    public TenantId TenantId { get; private set; }
    public StudyId StudyId { get; private set; }
    public StudySiteId StudySiteId { get; private set; }
    public string SubjectCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Screened";
    public bool IsActive { get; private set; } = true;

    protected Participant() { }

    public Participant(ParticipantId id, TenantId tenantId, StudyId studyId, StudySiteId studySiteId, string subjectCode, string status, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
        SubjectCode = subjectCode;
        Status = status;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void Enroll(string updatedBy, string reasonForChange)
    {
        Status = "Enrolled";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
        VersionIndex++;
    }
}

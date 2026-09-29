using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical StudySite aggregate root representing the association of Study × Site with regulatory approvals and activation lifecycle.
/// @req:PRD-SYS-053
/// </summary>
public class StudySite : AggregateRoot<StudySiteId>
{
    public TenantId TenantId { get; private set; }
    public StudyId StudyId { get; private set; }
    public SiteId SiteId { get; private set; }
    public string Status { get; private set; } = "Selected";
    public bool IsActive { get; private set; } = true;

    protected StudySite() { }

    public StudySite(StudySiteId id, TenantId tenantId, StudyId studyId, SiteId siteId, string status, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        SiteId = siteId;
        Status = status;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void Activate(string updatedBy, string reasonForChange)
    {
        Status = "Active";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
        VersionIndex++;
    }
}

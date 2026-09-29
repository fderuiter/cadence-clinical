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

    /// <summary>
    /// Activates the study site. <see cref="AggregateRootBase.CreatedBy"/> stays the creator and
    /// <see cref="AggregateRootBase.VersionIndex"/> is advanced by the GxP audit interceptor on save, which also
    /// records the acting user in the audit trail.
    /// </summary>
    public void Activate(string reasonForChange)
    {
        Status = "Active";
        ReasonForChange = reasonForChange;
    }
}

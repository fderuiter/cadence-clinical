using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical Study aggregate root representing a clinical trial specification owned by a Sponsor Organization.
/// @req:PRD-SYS-053
/// </summary>
public class Study : AggregateRoot<StudyId>
{
    public TenantId TenantId { get; private set; }
    public OrganizationId SponsorId { get; private set; }
    public string StudyCode { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Phase { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    protected Study() { }

    public Study(StudyId id, TenantId tenantId, OrganizationId sponsorId, string studyCode, string title, string phase, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        SponsorId = sponsorId;
        StudyCode = studyCode;
        Title = title;
        Phase = phase;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

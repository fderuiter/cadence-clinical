using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical Site aggregate root representing a durable physical or institutional location.
/// @req:PRD-SYS-053
/// </summary>
public class Site : AggregateRoot<SiteId>
{
    public TenantId TenantId { get; private set; }
    public OrganizationId InstitutionId { get; private set; }
    public string SiteCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    protected Site() { }

    public Site(SiteId id, TenantId tenantId, OrganizationId institutionId, string siteCode, string name, string country, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        InstitutionId = institutionId;
        SiteCode = siteCode;
        Name = name;
        Country = country;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

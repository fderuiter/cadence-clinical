using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

public enum OrganizationKind
{
    Sponsor,
    Cro,
    SiteInstitution,
    Vendor,
    Regulator,
    EthicsCommittee,
    Lab
}

/// <summary>
/// Organization entity operating within a Tenant boundary.
/// @req:PRD-SYS-053
/// </summary>
public class Organization : AggregateRoot<OrganizationId>
{
    public TenantId TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public OrganizationKind Kind { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Organization() { }

    public Organization(OrganizationId id, TenantId tenantId, string code, string name, OrganizationKind kind, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        Code = code;
        Name = name;
        Kind = kind;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

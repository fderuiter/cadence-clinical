using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Root isolation and contractual boundary representing a Cadence customer.
/// @req:PRD-SYS-053
/// </summary>
public class Tenant : AggregateRoot<TenantId>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    protected Tenant() { }

    public Tenant(TenantId id, string code, string name, string createdBy, string reasonForChange)
        : base(id)
    {
        Code = code;
        Name = name;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Root isolation and contractual boundary representing a Cadence customer.
/// </summary>
public class Tenant : AggregateRoot<TenantId>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Where the tenant's direct identifiers must be kept, as a region code (for example <c>EU</c>), or null when
    /// unrestricted (spec §13). The IdentityVault refuses to hold or reveal them in another region (ADR-2458). Set at
    /// provisioning; moving a tenant between regions is an operational migration, not a command.
    /// </summary>
    public string? Residency { get; private set; }

    protected Tenant() { }

    public Tenant(TenantId id, string code, string name, string createdBy, string reasonForChange, string? residency = null)
        : base(id)
    {
        Code = code;
        Name = name;
        Residency = residency;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

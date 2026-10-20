using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A person's presence in one tenant (K-ID-4). <see cref="Person"/> identity is tenant-independent; a tenant sees a
/// person only through a membership of its own. Organization memberships and the grants that confer authority come
/// with the authority model (spec §9.3, #4781).
/// </summary>
public class TenantMembership : AggregateRoot<TenantMembershipId>
{
    public TenantId TenantId { get; private set; }
    public PersonId PersonId { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected TenantMembership() { }

    public TenantMembership(TenantMembershipId id, TenantId tenantId, PersonId personId, string createdBy, string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        PersonId = personId;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

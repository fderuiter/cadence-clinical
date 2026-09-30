using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical human identity, independent of any tenant-scoped role (K-ID-4). Pseudonymous: the person's name, date of
/// birth and contact details are direct identifiers and live only in the IdentityVault, keyed by this id (spec §9.2,
/// ADR-2458).
/// @req:PRD-SYS-053
/// </summary>
public class Person : AggregateRoot<PersonId>
{
    public bool IsActive { get; private set; } = true;

    protected Person() { }

    public Person(PersonId id, string createdBy, string reasonForChange)
        : base(id)
    {
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

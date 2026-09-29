using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical human identity, independent of any tenant-scoped role.
/// @req:PRD-SYS-053
/// </summary>
public class Person : AggregateRoot<PersonId>
{
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    protected Person() { }

    public Person(PersonId id, string email, string firstName, string lastName, string createdBy, string reasonForChange)
        : base(id)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}

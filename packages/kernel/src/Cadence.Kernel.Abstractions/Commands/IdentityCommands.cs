using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Creates a tenant: the isolation and contractual boundary every other kernel record belongs to (spec §3).
/// <see cref="ICommand.TenantId"/> is the new tenant's id, chosen by the caller (UUIDv7). <c>Residency</c> is the
/// region the tenant's direct identifiers must stay in (spec §13, ADR-2458), two to eight upper-case letters, or null
/// when unrestricted.
/// </summary>
[KernelCapability("tenant.provision", VerbClass.Execute, InCatalogue = false)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record ProvisionTenant(
    TenantId TenantId,
    string Code,
    string Name,
    string IdempotencyKey,
    string ReasonForChange,
    string? Residency = null) : ICommand<TenantId>;

/// <summary>
/// Registers an organization (sponsor, CRO, site institution, vendor, …) in a tenant (spec §7.1).
/// </summary>
[KernelCapability("organization.register", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(IdentityErrors),
    nameof(IdentityErrors.CodeRequired),
    nameof(IdentityErrors.NameRequired),
    nameof(IdentityErrors.TenantUnknown))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RegisterOrganization(
    TenantId TenantId,
    OrganizationId OrganizationId,
    string Code,
    string Name,
    OrganizationKind Kind,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<OrganizationId>;

/// <summary>
/// Registers a pseudonymous person in <c>kernel.people</c> (spec §3, K-ID-4). Direct human identifiers (names,
/// emails, usernames) are never accepted here; they belong exclusively to the IdentityVault when recorded.
/// </summary>
[KernelCapability("person.register", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RegisterPerson(
    TenantId TenantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<PersonId>;

/// <summary>
/// Enrolls a pseudonymous person as a member of a tenant (<c>kernel.tenant_memberships</c>, K-ID-4), making the
/// person visible within that tenant under Row-Level Security.
/// </summary>
[KernelCapability("tenant.enroll_member", VerbClass.Execute)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[BaselineKernelCommand]
public sealed record RegisterTenantMembership(
    TenantId TenantId,
    TenantMembershipId TenantMembershipId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<TenantMembershipId>;

/// <summary>Validation errors raised by the identity commands.</summary>
public static class IdentityErrors
{
    public static readonly KernelError CodeRequired =
        new("K.Identity.CodeRequired", 422, "A code is required.");

    public static readonly KernelError NameRequired =
        new("K.Identity.NameRequired", 422, "A name is required.");

    public static readonly KernelError ResidencyInvalid =
        new("K.Identity.ResidencyInvalid", 422, "A residency region is two to eight upper-case letters.");

    public static readonly KernelError TenantUnknown =
        new("K.Identity.TenantUnknown", 404, "The tenant does not exist.");

    public static readonly KernelError PersonRequired =
        new("K.Identity.PersonRequired", 422, "A pseudonymous person identifier is required.");

    public static readonly KernelError MembershipRequired =
        new("K.Identity.MembershipRequired", 422, "A tenant membership identifier is required.");
}

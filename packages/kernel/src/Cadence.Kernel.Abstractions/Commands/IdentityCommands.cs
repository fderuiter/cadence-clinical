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
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RegisterOrganization(
    TenantId TenantId,
    OrganizationId OrganizationId,
    string Code,
    string Name,
    OrganizationKind Kind,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<OrganizationId>;

/// <summary>
/// Registers a person's pseudonymous identity and presence in a tenant (spec §9.2, K-ID-4). Direct identifiers stay out
/// of the command, event and kernel tables; record them separately with <see cref="RecordPersonIdentity"/>. Idempotent
/// when the person already belongs to the tenant.
/// </summary>
[KernelCapability("person.register", VerbClass.Execute, Resource = nameof(PersonId))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
[RaisesKernelErrors(typeof(IdentityErrors), nameof(IdentityErrors.TenantUnknown), nameof(IdentityErrors.PersonRequired))]
public sealed record RegisterTenantMember(
    TenantId TenantId,
    PersonId PersonId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<PersonId>;

/// <summary>Validation errors raised by the identity commands.</summary>
public static class IdentityErrors
{
    public static readonly KernelError CodeRequired =
        new("K.Identity.CodeRequired", 422, "A code is required.");

    public static readonly KernelError NameRequired =
        new("K.Identity.NameRequired", 422, "A name is required.");

    public static readonly KernelError PersonRequired =
        new("K.Identity.PersonRequired", 422, "A person identifier is required.");

    public static readonly KernelError ResidencyInvalid =
        new("K.Identity.ResidencyInvalid", 422, "A residency region is two to eight upper-case letters.");

    public static readonly KernelError TenantUnknown =
        new("K.Identity.TenantUnknown", 404, "The tenant does not exist.");
}

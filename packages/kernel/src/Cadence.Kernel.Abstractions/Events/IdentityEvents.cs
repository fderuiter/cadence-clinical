using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A tenant was provisioned (spec §5.2, Execution family).</summary>
[KernelEvent("TenantProvisioned", 1)]
public sealed record TenantProvisioned(TenantId TenantId, string Code, string Name);

/// <summary>An organization was registered in a tenant (spec §5.2, Execution family).</summary>
[KernelEvent("OrganizationRegistered", 1)]
public sealed record OrganizationRegistered(TenantId TenantId, OrganizationId OrganizationId, string Code, string Name, OrganizationKind Kind);

/// <summary>A pseudonymous person was registered in the kernel (K-ID-4).</summary>
[KernelEvent("PersonRegistered", 1)]
public sealed record PersonRegistered(TenantId TenantId, PersonId PersonId);

/// <summary>A pseudonymous person joined a tenant (K-ID-4).</summary>
[KernelEvent("TenantMembershipRegistered", 1)]
public sealed record TenantMembershipRegistered(TenantId TenantId, TenantMembershipId TenantMembershipId, PersonId PersonId);

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A tenant was provisioned (spec §5.2, Execution family).</summary>
[KernelEvent("TenantProvisioned", 1)]
public sealed record TenantProvisioned(TenantId TenantId, string Code, string Name);

/// <summary>An organization was registered in a tenant (spec §5.2, Execution family).</summary>
[KernelEvent("OrganizationRegistered", 1)]
public sealed record OrganizationRegistered(TenantId TenantId, OrganizationId OrganizationId, string Code, string Name, OrganizationKind Kind);

/// <summary>A person was registered as a member of a tenant (spec §9.2, K-ID-4).</summary>
[KernelEvent("TenantMemberRegistered", 1)]
public sealed record TenantMemberRegistered(TenantId TenantId, PersonId PersonId);

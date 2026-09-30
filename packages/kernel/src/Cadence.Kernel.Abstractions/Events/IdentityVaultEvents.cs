using System;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Identity;

namespace Cadence.Kernel.Events;

/// <summary>
/// Identity details were recorded in the vault for a subject (spec §9.2, Trust family). The event carries the layer
/// and the pseudonymous id, never an identifier.
/// </summary>
[KernelEvent("IdentityRecorded", 1)]
public sealed record IdentityRecorded(TenantId TenantId, IdentityLayer Layer, Guid SubjectId);

/// <summary>A subject's identity was disclosed to the command's actor (K-IDN-1, Trust family).</summary>
[KernelEvent("IdentityRevealed", 1)]
public sealed record IdentityRevealed(TenantId TenantId, IdentityLayer Layer, Guid SubjectId);

/// <summary>A subject's identity record was crypto-shredded (spec §9.2, Trust family).</summary>
[KernelEvent("IdentityErased", 1)]
public sealed record IdentityErased(TenantId TenantId, IdentityLayer Layer, Guid SubjectId);

/// <summary>
/// A participant was linked to a person in the vault (K-IDN-2, Trust family). The person is not named: the link
/// itself is identifying and stays in the vault.
/// </summary>
[KernelEvent("ParticipantLinked", 1)]
public sealed record ParticipantLinked(TenantId TenantId, ParticipantId ParticipantId);

/// <summary>A person's linked participants were disclosed to the command's actor (K-IDN-2, Trust family).</summary>
[KernelEvent("PersonLinksRevealed", 1)]
public sealed record PersonLinksRevealed(TenantId TenantId, PersonId PersonId);

using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A role was defined (spec §9.3, Trust family).</summary>
[KernelEvent("RoleDefined", 1)]
public sealed record RoleDefined(TenantId TenantId, RoleId RoleId, string Code, IReadOnlyList<string> Capabilities);

/// <summary>A grant was issued (spec §5.2 <c>GrantIssued</c>).</summary>
[KernelEvent("GrantIssued", 1)]
public sealed record GrantIssued(
    TenantId TenantId,
    GrantId GrantId,
    string Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool WithGrantOption);

/// <summary>Authority was delegated under a parent grant (spec §5.2 <c>DelegationSigned</c>).</summary>
[KernelEvent("DelegationSigned", 1)]
public sealed record DelegationSigned(
    TenantId TenantId,
    GrantId GrantId,
    GrantId ParentGrantId,
    string Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool WithGrantOption);

/// <summary>A grant's validity was ended (spec §5.2 <c>GrantRevoked</c>).</summary>
[KernelEvent("GrantRevoked", 1)]
public sealed record GrantRevoked(TenantId TenantId, GrantId GrantId, DateTimeOffset EffectiveAt);

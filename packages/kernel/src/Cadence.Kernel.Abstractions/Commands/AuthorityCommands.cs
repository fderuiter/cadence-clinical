using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Defines a role: a named bundle of capability tokens, used only to issue grants (spec §9.3). A role never changes
/// once defined, so a grant made under it means the same thing at every time it is evaluated; a different bundle is a
/// new role. Unblinding capabilities cannot be bundled (K-AUTH-4).
/// </summary>
/// <remarks>
/// <para><c>Code</c>: A short upper-case code unique in the tenant, for example <c>CRC</c>.</para>
/// <para><c>Capabilities</c>: The capability tokens the role bundles, at least one.</para>
/// </remarks>
[KernelCapability("role.define", VerbClass.Approve)]
public sealed record DefineRole(
    TenantId TenantId,
    RoleId RoleId,
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<RoleState>;

/// <summary>
/// Grants an actor one capability, or a role's capabilities, within a scope for a valid period (spec §9.3). Exactly
/// one of <c>Capability</c> and <c>RoleId</c> is given. The grant is effective in valid time from <c>ValidFrom</c> but
/// known only from when it is recorded, so it cannot authorize anything decided before (K-AUTH-3).
/// </summary>
/// <remarks>
/// <para><c>ValidTo</c>: When the grant ends, exclusive; null while open-ended.</para>
/// <para><c>WithGrantOption</c>: Whether the grantee may delegate it (<see cref="DelegateGrant"/>).</para>
/// </remarks>
[KernelCapability("grant.issue", VerbClass.Approve)]
public sealed record IssueGrant(
    TenantId TenantId,
    GrantId GrantId,
    Actor Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool WithGrantOption,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<GrantState>;

/// <summary>
/// Delegates authority (DOA): the holder of <c>ParentGrantId</c>, which must carry the grant option, grants part of it
/// to another actor (spec §9.3). The delegation is bounded by the parent: its capabilities, scope and valid period lie
/// within the parent's, and it stops authorizing as soon as the parent does.
/// </summary>
[KernelCapability("grant.delegate", VerbClass.Execute, Resource = nameof(ParentGrantId))]
public sealed record DelegateGrant(
    TenantId TenantId,
    GrantId GrantId,
    GrantId ParentGrantId,
    Actor Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool WithGrantOption,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<GrantState>;

/// <summary>
/// Ends a grant's validity at <c>EffectiveAt</c>. The earlier record stays: decisions made while it was known remain
/// explained by it. A grant revoked at or before its start no longer grants anything. The delegator may revoke a
/// delegation it signed.
/// </summary>
[KernelCapability("grant.revoke", VerbClass.Approve, Resource = nameof(GrantId))]
public sealed record RevokeGrant(
    TenantId TenantId,
    GrantId GrantId,
    DateTimeOffset EffectiveAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<GrantState>;

/// <summary>A role after a command.</summary>
public sealed record RoleState(RoleId RoleId, string Code, string Name, IReadOnlyList<string> Capabilities);

/// <summary>A grant after a command. <c>ValidTo</c> is null while open-ended; <c>Revoked</c> when nothing remains valid.</summary>
public sealed record GrantState(
    GrantId GrantId,
    Actor Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    GrantId? ParentGrantId,
    bool WithGrantOption,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked);

/// <summary>Errors raised by the authority commands (spec §9.3).</summary>
public static class AuthorityErrors
{
    public static readonly KernelError RoleCodeInvalid =
        new("K.Authority.RoleCodeInvalid", 422, "A role code is 1 to 32 upper-case letters, digits, '_' or '-'.");

    public static readonly KernelError RoleNameInvalid =
        new("K.Authority.RoleNameInvalid", 422, "A role needs a name of at most 200 characters.");

    public static readonly KernelError RoleCodeTaken =
        new("K.Authority.RoleCodeTaken", 409, "The tenant already has a role with this code.");

    public static readonly KernelError CapabilityInvalid =
        new("K.Authority.CapabilityInvalid", 422, "A capability is a resource.action token in lower snake_case.");

    public static readonly KernelError UnblindingNotBundled =
        new("K.Authority.UnblindingNotBundled", 422, "Unblinding capabilities are granted one by one, never in a role (K-AUTH-4).");

    public static readonly KernelError RoleUnknown =
        new("K.Authority.RoleUnknown", 404, "The role does not exist.");

    public static readonly KernelError GrantTargetInvalid =
        new("K.Authority.GrantTargetInvalid", 422, "A grant names exactly one capability or one role.");

    public static readonly KernelError ScopeInvalid =
        new("K.Authority.ScopeInvalid", 422, "The grant scope is not valid.");

    public static readonly KernelError ScopeUnknown =
        new("K.Authority.ScopeUnknown", 404, "The organization, study or study site the scope names does not exist.");

    public static readonly KernelError ValidityInvalid =
        new("K.Authority.ValidityInvalid", 422, "A grant's valid period must end after it starts.");

    public static readonly KernelError ParentInactive =
        new("K.Authority.ParentInactive", 409, "The delegating grant is not in effect.");

    public static readonly KernelError GrantOptionRequired =
        new("K.Authority.GrantOptionRequired", 403, "The delegating grant does not allow delegation.");

    public static readonly KernelError DelegationExceedsParent =
        new("K.Authority.DelegationExceedsParent", 422, "A delegation must lie within the delegating grant.");

    public static readonly KernelError SelfDelegation =
        new("K.Authority.SelfDelegation", 422, "An actor cannot delegate to itself.");

    public static readonly KernelError RevocationInvalid =
        new("K.Authority.RevocationInvalid", 409, "The grant is not in effect at that time.");
}

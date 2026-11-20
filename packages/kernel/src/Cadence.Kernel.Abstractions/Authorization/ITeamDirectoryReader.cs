using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Authorization;

/// <summary>
/// Read-only directory view of a tenant's members, defined roles, grants and assignable scopes (ADR-2610, ADR-2819).
/// Implemented by kernel persistence under the tenant's Row-Level Security so modules such as <c>Cadence.Modules.Org</c>
/// administer invitations, delegations and revocations without referencing kernel persistence or <c>KernelDbContext</c>
/// (K-XS-2). Direct personal identifiers are never returned here; they stay in <see cref="Identity.IIdentityVault"/>.
/// </summary>
public interface ITeamDirectoryReader
{
    /// <summary>Reads the tenant's current members, roles, grants (with active evaluation at <paramref name="at"/>) and scope options.</summary>
    Task<TeamDirectorySnapshot> ReadSnapshotAsync(TenantId tenant, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>A point-in-time snapshot of a tenant's authority directory.</summary>
public sealed record TeamDirectorySnapshot(
    TenantId TenantId,
    string TenantCode,
    string TenantName,
    IReadOnlyList<TeamMemberRecord> Members,
    IReadOnlyList<TeamRoleRecord> Roles,
    IReadOnlyList<TeamGrantRecord> Grants,
    IReadOnlyList<TeamScopeOption> ScopeOptions);

/// <summary>One pseudonymous person registered in the tenant.</summary>
public sealed record TeamMemberRecord(
    PersonId PersonId,
    DateTimeOffset JoinedAt,
    bool IsActive);

/// <summary>One immutable role bundle defined in the tenant.</summary>
public sealed record TeamRoleRecord(
    RoleId RoleId,
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities);

/// <summary>One direct grant or child delegation in the tenant.</summary>
public sealed record TeamGrantRecord(
    GrantId GrantId,
    Actor Grantee,
    string? Capability,
    RoleId? RoleId,
    AuthorityScope Scope,
    GrantId? ParentGrantId,
    bool WithGrantOption,
    string IssuedBy,
    DateTimeOffset CreatedAt,
    string ReasonForChange,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    bool InEffect);

/// <summary>One targetable organizational scope in the tenant, with its scope path for visibility and delegation checks.</summary>
public sealed record TeamScopeOption(
    AuthorityScope Scope,
    ScopePath Path,
    string Code,
    string Label,
    string? ParentLabel = null);

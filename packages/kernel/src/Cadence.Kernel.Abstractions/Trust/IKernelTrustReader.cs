using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Trust;

/// <summary>
/// Read-only bitemporal queries over canonical Kernel authority and qualification state (spec §9.3, K-AUTH-3, K-TIME-1).
/// Callers apply <c>INodeVisibility</c> before exposing scoped records on the wire.
/// </summary>
public interface IKernelTrustReader
{
    /// <summary>Lists all roles defined in <paramref name="tenant"/>, ordered by code.</summary>
    Task<IReadOnlyList<KernelRoleReadModel>> ListRolesAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one role by code (case-insensitive) in <paramref name="tenant"/>, or null when absent.</summary>
    Task<KernelRoleReadModel?> FindRoleByCodeAsync(
        TenantId tenant,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Queries grants and delegations in <paramref name="tenant"/> evaluated at <paramref name="query"/>.</summary>
    Task<IReadOnlyList<KernelGrantReadModel>> ListGrantsAsync(
        TenantId tenant,
        KernelGrantQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Queries qualifications in <paramref name="tenant"/> evaluated at <paramref name="query"/>.</summary>
    Task<IReadOnlyList<KernelQualificationReadModel>> ListQualificationsAsync(
        TenantId tenant,
        KernelQualificationQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Queries qualification requirements in <paramref name="tenant"/> evaluated at <paramref name="query"/>.</summary>
    Task<IReadOnlyList<KernelQualificationRequirementReadModel>> ListQualificationRequirementsAsync(
        TenantId tenant,
        KernelQualificationRequirementQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Bitemporal query filter for <see cref="IKernelTrustReader.ListGrantsAsync"/>.</summary>
public sealed record KernelGrantQuery(
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    StudyId? StudyId = null,
    SiteId? SiteId = null,
    StudySiteId? StudySiteId = null,
    string? GranteeId = null,
    string? RoleCode = null,
    bool IncludeInactive = false);

/// <summary>Bitemporal query filter for <see cref="IKernelTrustReader.ListQualificationsAsync"/>.</summary>
public sealed record KernelQualificationQuery(
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    string? HolderId = null,
    string? Code = null,
    StudyId? StudyId = null,
    ProtocolVersionId? ProtocolVersionId = null,
    bool IncludeInactive = false);

/// <summary>Bitemporal query filter for <see cref="IKernelTrustReader.ListQualificationRequirementsAsync"/>.</summary>
public sealed record KernelQualificationRequirementQuery(
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    StudyId? StudyId = null,
    string? Capability = null,
    string? QualificationCode = null,
    bool IncludeInactive = false);

/// <summary>Read model of a defined Kernel role.</summary>
public sealed record KernelRoleReadModel(
    RoleId RoleId,
    long VersionIndex,
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities);

/// <summary>Read model of a bitemporal validity/recorded period (<c>K-TIME-1</c>).</summary>
public sealed record KernelBitemporalPeriodReadModel(
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset RecordedFrom,
    DateTimeOffset? RecordedTo);

/// <summary>Read model of a Kernel grant or delegation evaluated as of a bitemporal instant.</summary>
public sealed record KernelGrantReadModel(
    GrantId GrantId,
    long VersionIndex,
    GrantId? ParentGrantId,
    ActorKind GranteeKind,
    string GranteeId,
    string? Capability,
    RoleId? RoleId,
    string? RoleCode,
    string? RoleName,
    IReadOnlyList<string> Capabilities,
    ScopeLevel ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    StudyId? StudyId,
    string? StudyCode,
    SiteId? SiteId,
    string? SiteCode,
    StudySiteId? StudySiteId,
    bool WithGrantOption,
    string IssuedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    IReadOnlyList<KernelBitemporalPeriodReadModel> Periods);

/// <summary>Read model of a Kernel qualification evaluated as of a bitemporal instant.</summary>
public sealed record KernelQualificationReadModel(
    QualificationId QualificationId,
    long VersionIndex,
    ActorKind HolderKind,
    string HolderId,
    QualificationKind Kind,
    string Code,
    ProtocolVersionId? ProtocolVersionId,
    int? ProtocolVersionNumber,
    StudyId? StudyId,
    string? StudyCode,
    ArtifactId? EvidenceArtifactId,
    ArtifactVersionId EvidenceArtifactVersionId,
    string RecordedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    IReadOnlyList<KernelBitemporalPeriodReadModel> Periods);

/// <summary>Read model of a Kernel qualification requirement evaluated as of a bitemporal instant.</summary>
public sealed record KernelQualificationRequirementReadModel(
    QualificationRequirementId RequirementId,
    long VersionIndex,
    string Capability,
    string QualificationCode,
    ProtocolVersionId? ProtocolVersionId,
    int? ProtocolVersionNumber,
    ScopeLevel ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    StudyId? StudyId,
    string? StudyCode,
    string DefinedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Retired,
    IReadOnlyList<KernelBitemporalPeriodReadModel> Periods);

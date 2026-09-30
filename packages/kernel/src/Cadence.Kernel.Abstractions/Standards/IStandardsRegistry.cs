using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Standards;

/// <summary>
/// Read access to a tenant's Standards Registry and Metadata Registry (spec §6.3). Every read is scoped to one tenant
/// by the kernel's Row-Level Security (K-ID-2). Changes go through the commands in <see cref="RegisterStandardVersion"/>
/// and its siblings, never through this interface.
/// </summary>
public interface IStandardsRegistry
{
    /// <summary>Every standard version registered in the tenant, ordered by standard code and version.</summary>
    Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken);

    /// <summary>One standard version's summary, or null.</summary>
    Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken);

    /// <summary>The standard version with this code and version, or null.</summary>
    Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken);

    /// <summary>A standard version's full definitions, exactly as registered, or null.</summary>
    Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken);

    /// <summary>One library item version, or null.</summary>
    Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken);

    /// <summary>Every version of a library item, newest first.</summary>
    Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken);

    /// <summary>The standards a study declared, or null when it has declared none.</summary>
    Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken);
}

/// <summary>A registered standard version without its definitions.</summary>
public sealed record StandardVersionSummary(
    StandardVersionId Id,
    string StandardCode,
    string Version,
    string Name,
    string Publisher,
    string? Source,
    DateOnly? EffectiveDate,
    string ContentHash,
    GovernanceStatus Status,
    AttestationId? ApprovalAttestation,
    AttestationId? DeprecationAttestation,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology);

/// <summary>A Metadata Registry library item version with its content.</summary>
public sealed record LibraryItemView(
    LibraryItemId Id,
    LibraryItemKind Kind,
    string Key,
    int Version,
    string Title,
    JsonElement Content,
    string ContentHash,
    GovernanceStatus Status,
    IReadOnlyList<StandardVersionId> StandardVersions,
    AttestationId? ApprovalAttestation,
    AttestationId? DeprecationAttestation);

/// <summary>What a study declared: standards, terminology versions and pinned library item versions.</summary>
public sealed record StudyStandardsProfileView(
    StudyStandardsProfileId Id,
    StudyId StudyId,
    long Revision,
    IReadOnlyList<StandardVersionId> StandardVersions,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology,
    IReadOnlyList<LibraryItemId> LibraryPins);

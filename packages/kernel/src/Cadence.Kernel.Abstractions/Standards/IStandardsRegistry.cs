using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol;

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

    /// <summary>Looks up multiple library item versions by id in a single query, keyed by <see cref="LibraryItemId"/>.</summary>
    async Task<IReadOnlyDictionary<LibraryItemId, LibraryItemView>> FindLibraryItemsAsync(
        TenantId tenant,
        IReadOnlyCollection<LibraryItemId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<LibraryItemId, LibraryItemView>();
        }

        var found = new Dictionary<LibraryItemId, LibraryItemView>();
        foreach (var id in ids)
        {
            if (!found.ContainsKey(id)
                && await FindLibraryItemAsync(tenant, id, cancellationToken).ConfigureAwait(false) is { } item)
            {
                found[id] = item;
            }
        }

        return found;
    }

    /// <summary>Every version of a library item, newest first.</summary>
    Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken);

    /// <summary>Every library item version in the tenant (optionally filtered by <paramref name="kind"/>), ordered by key and newest version first.</summary>
    Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(
        TenantId tenant,
        LibraryItemKind? kind,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LibraryItemView>>([]);

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
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology)
{
    /// <summary>
    /// The bindings the tenant had imported when the standard version was registered, each with its Concept Registry
    /// version. A binding to a package imported later stays by natural key only (ADR-2460 §4.2).
    /// </summary>
    public IReadOnlyList<ResolvedCodeSystemVersion> ResolvedTerminology { get; init; } = [];
}

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
    AttestationId? DeprecationAttestation)
{
    /// <summary>The library item's version index (ADR-2481 §4.2), which pins a NodeRef to the version read.</summary>
    public long VersionIndex { get; init; }

    /// <summary>Protocol versions linked to this library item version via <c>GovernedBy</c> lineage, or empty when unrestricted.</summary>
    public IReadOnlyList<ProtocolVersionId> GoverningProtocolVersions { get; init; } = [];
}

/// <summary>What a study declared: standards, terminology versions and pinned library item versions.</summary>
public sealed record StudyStandardsProfileView(
    StudyStandardsProfileId Id,
    StudyId StudyId,
    long Revision,
    IReadOnlyList<StandardVersionId> StandardVersions,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology,
    IReadOnlyList<LibraryItemId> LibraryPins)
{
    /// <summary>
    /// Each declared terminology version with the Concept Registry version it resolved to when it was declared. Every
    /// declaration resolves all of them (ADR-2460 §4.2).
    /// </summary>
    public IReadOnlyList<ResolvedCodeSystemVersion> ResolvedTerminology { get; init; } = [];
}

/// <summary>
/// Shared protocol-version lineage matching for Metadata Registry rule packs (K-TIME-8).
/// </summary>
public static class RulePackGovernance
{
    /// <summary>
    /// Determines whether a rule pack governed by <paramref name="governingVersions"/> applies to <paramref name="targetProtocolVersionId"/>,
    /// falling back to the nearest approved ancestor only while all governing versions remain unapproved drafts.
    /// </summary>
    public static bool AppliesToProtocolVersion(
        IReadOnlyList<ProtocolVersionId>? governingVersions,
        ProtocolVersionId targetProtocolVersionId,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>? studyVersions)
    {
        if (governingVersions is not { Count: > 0 } || governingVersions.Contains(targetProtocolVersionId))
        {
            return true;
        }

        if (studyVersions is null)
        {
            return false;
        }

        var anyApprovedGoverning = governingVersions.Any(id =>
            !studyVersions.TryGetValue(id, out var summary) || summary.Status == ProtocolVersionStatus.Approved);
        if (anyApprovedGoverning)
        {
            return false;
        }

        foreach (var governingId in governingVersions)
        {
            var visited = new HashSet<ProtocolVersionId> { governingId };
            var current = studyVersions.GetValueOrDefault(governingId)?.ParentVersionId;
            while (current is { } parentId && visited.Add(parentId))
            {
                if (!studyVersions.TryGetValue(parentId, out var parentSummary))
                {
                    break;
                }

                if (parentSummary.Status == ProtocolVersionStatus.Approved)
                {
                    if (parentId == targetProtocolVersionId)
                    {
                        return true;
                    }

                    break;
                }

                current = parentSummary.ParentVersionId;
            }
        }

        return false;
    }
}

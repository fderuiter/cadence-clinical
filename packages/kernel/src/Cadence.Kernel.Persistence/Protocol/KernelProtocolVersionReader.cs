using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Protocol;

/// <summary>
/// Reads protocol versions through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelProtocolVersionReader(KernelTenantDbContextFactory contexts) : IProtocolVersionReader
{
    public async Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.StudyId == study)
            .OrderByDescending(v => v.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. versions.Select(Summary)];
    }

    public async Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.ProtocolVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == tenant && v.Id == version, cancellationToken).ConfigureAwait(false);
        return found is null ? null : Summary(found);
    }

    public async Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        var design = await GetDesignAsync(tenant, version, cancellationToken).ConfigureAwait(false);
        return design is not null ? ScheduleOfActivities.Compile(design) : null;
    }

    public async Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        var content = await GetContentAsync(tenant, version, cancellationToken).ConfigureAwait(false);
        return content is not null ? ProtocolDesignDocument.Read(content).Design : null;
    }

    public async Task<string?> GetContentAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == version)
            .Select(v => v.Content)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProtocolAmendment?> GetAmendmentAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == version)
            .Select(v => v.Amendment)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, NodeRef>> GetFormVersionSnapshotsAsync(
        TenantId tenant,
        ProtocolVersionId version,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var snapshots = await db.FormVersionSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenant && s.ProtocolVersionId == version)
            .Select(s => new { s.FormId, s.StudyId, s.Id, s.VersionIndex })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = new Dictionary<string, NodeRef>(snapshots.Count + 1, StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            var nodeRef = NodeRef.For(snapshot.Id, snapshot.VersionIndex);
            result[snapshot.FormId] = nodeRef;
            if (string.Equals(snapshot.FormId, snapshot.StudyId.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                result.TryAdd("*", nodeRef);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, NodeRef>> GetGovernedProtocolElementsAsync(
        TenantId tenant,
        ProtocolVersionId version,
        bool includeApprovedAncestorWhenUnapproved,
        CancellationToken cancellationToken)
    {
        var candidates = await ProtocolElementLineage.ResolveApprovedChainElementsAsync(
            this,
            tenant,
            version,
            includeApprovedAncestorWhenUnapproved,
            cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return candidates;
        }

        await using var db = contexts.Create(tenant);
        var candidateGuids = candidates.Values.Select(n => n.Id).Distinct().ToList();
        var governedGuids = await db.LineageEdges.AsNoTracking()
            .Where(e => e.TenantId == tenant
                && e.ToKind == NodeKinds.ProtocolElement
                && candidateGuids.Contains(e.ToId)
                && e.FromKind == NodeKinds.ProtocolVersion
                && e.Kind == LineageEdgeKind.GovernedBy)
            .Select(e => e.ToId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (governedGuids.Count == 0)
        {
            return new Dictionary<string, NodeRef>(StringComparer.Ordinal);
        }

        var governedSet = governedGuids.ToHashSet();
        var filtered = new Dictionary<string, NodeRef>(governedSet.Count, StringComparer.Ordinal);
        foreach (var (elementId, nodeRef) in candidates)
        {
            if (governedSet.Contains(nodeRef.Id))
            {
                filtered[elementId] = nodeRef;
            }
        }

        return filtered;
    }

    public async Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>> GetDesignsForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.StudyId == study)
            .OrderByDescending(v => v.Number)
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designs = new Dictionary<ProtocolVersionId, ProtocolDesign>(versions.Count);
        foreach (var version in versions)
        {
            if (ProtocolDesignDocument.Read(version.Content).Design is { } design)
            {
                designs[version.Id] = design;
            }
        }

        return designs;
    }

    private static ProtocolVersionSummary Summary(ProtocolVersion version) =>
        new(
            version.Id,
            version.StudyId,
            version.Number,
            version.ParentVersionId,
            version.Status,
            version.ContentHash,
            version.Amendment?.Intent,
            version.ApprovalAttestationId,
            version.WithdrawalReason);
}

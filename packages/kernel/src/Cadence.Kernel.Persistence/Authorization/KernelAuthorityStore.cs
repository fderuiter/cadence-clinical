using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Reads grants, roles and scope paths for the authority evaluator through a context bound to the request's tenant
/// (ADR-2250). Read-only: no tracking. Bulk reads hold one connection lease across per-kind set queries so
/// <see cref="KernelTenantConnectionInterceptor"/> sets and clears the tenant once per bulk call (K-ID-2).
/// </summary>
internal sealed class KernelAuthorityStore(
    KernelTenantDbContextFactory contexts,
    IBlindedTreatmentClassifier? blindingClassifier = null) : IAuthorityStore
{
    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;

    public async Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = contexts.Create(tenant);
        return await db.Grants.AsNoTracking()
            .Where(g => g.TenantId == tenant && g.GranteeKind == actor.Kind && g.GranteeId == actor.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.Grants.AsNoTracking()
            .SingleOrDefaultAsync(g => g.TenantId == tenant && g.Id == grant, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<GrantId, Grant>> FindGrantsAsync(
        TenantId tenant, IReadOnlyCollection<GrantId> grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var ids = grants.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<GrantId, Grant>();
        }

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await db.Grants.AsNoTracking()
                .Where(g => g.TenantId == tenant && ids.Contains(g.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.ToDictionary(g => g.Id);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var ids = roles.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        await using var db = contexts.Create(tenant);
        return await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenant && ids.Contains(r.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<ProtocolVersionId, int>> ProtocolVersionNumbersAsync(
        TenantId tenant, IReadOnlyCollection<ProtocolVersionId> versions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var ids = versions.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<ProtocolVersionId, int>();
        }

        await using var db = contexts.Create(tenant);
        var rows = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Number })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => r.Number);
    }

    public async Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = contexts.Create(tenant);
        return await db.Qualifications.AsNoTracking()
            .Where(q => q.TenantId == tenant && q.HolderKind == actor.Kind && q.HolderId == actor.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.QualificationRequirements.AsNoTracking()
            .Where(r => r.TenantId == tenant && r.Capability == capability)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, EvidenceNodeClassification>();
        }

        var obsIds = distinct
            .Where(n => n.Kind == NodeKinds.Observation)
            .Select(n => n.Id)
            .Distinct()
            .ToArray();
        var findingIds = distinct
            .Where(n => n.Kind == NodeKinds.ValidationFinding)
            .Select(n => n.Id)
            .Distinct()
            .ToArray();
        var assertionIds = distinct
            .Where(n => n.Kind == NodeKinds.Assertion)
            .Select(n => AssertionId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var formQueryIds = distinct
            .Where(n => n.Kind == NodeKinds.FormQuery)
            .Select(n => n.Id)
            .Distinct()
            .ToList();
        if (obsIds.Length == 0
            && findingIds.Length == 0
            && assertionIds.Count == 0
            && formQueryIds.Count == 0)
        {
            return new Dictionary<NodeRef, EvidenceNodeClassification>();
        }

        var expectedEvidenceCount = obsIds.Length + findingIds.Length + assertionIds.Count + formQueryIds.Count;
        var resolvedPaths = new Dictionary<(string Kind, Guid Id), ScopePath>(expectedEvidenceCount);
        var resolvedEvidence = new Dictionary<(string Kind, Guid Id), EvidenceNodeClassification>(expectedEvidenceCount);
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AuthorityEvidenceClassifier.ClassifyEvidenceInOpenConnectionAsync(
                    db,
                    _blinding,
                    tenant,
                    obsIds,
                    findingIds,
                    assertionIds,
                    formQueryIds,
                    resolvedPaths,
                    resolvedEvidence,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        var result = new Dictionary<NodeRef, EvidenceNodeClassification>(distinct.Count);
        foreach (var node in distinct)
        {
            if (resolvedEvidence.TryGetValue((node.Kind, node.Id), out var info))
            {
                result[node] = info;
            }
        }

        return result;
    }

    public async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> Evidence)> ResolveVisibilityBatchAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return (new Dictionary<NodeRef, ScopePath>(), new Dictionary<NodeRef, EvidenceNodeClassification>());
        }

        var byKind = distinct
            .Select(r => r.Unversioned())
            .Distinct()
            .Where(r => AuthorityScopePathReader.PathBearingKinds.Contains(r.Kind))
            .GroupBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var resolvedPaths = new Dictionary<(string Kind, Guid Id), ScopePath>(distinct.Count);
        var resolvedEvidence = new Dictionary<(string Kind, Guid Id), EvidenceNodeClassification>();
        var dummyPotentiallyBlinded = new HashSet<NodeRef>();
        if (byKind.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var obsIds = Array.Empty<Guid>();
                var findingIds = Array.Empty<Guid>();
                var assertionIds = new List<AssertionId>();
                var formQueryIds = new List<Guid>();

                foreach (var group in byKind)
                {
                    var ids = group.Select(r => r.Id).ToArray();
                    switch (group.Key)
                    {
                        case NodeKinds.Observation:
                            obsIds = ids;
                            break;
                        case NodeKinds.ValidationFinding:
                            findingIds = ids;
                            break;
                        case NodeKinds.Assertion:
                            assertionIds = [.. ids.Select(AssertionId.FromGuid)];
                            break;
                        case NodeKinds.FormQuery:
                            formQueryIds = [.. ids];
                            break;
                        default:
                            await AuthorityScopePathReader.LoadPathsForKindAsync(db, tenant, group.Key, ids, resolvedPaths, dummyPotentiallyBlinded, cancellationToken)
                                .ConfigureAwait(false);
                            break;
                    }
                }

                if (obsIds.Length > 0 || findingIds.Length > 0 || assertionIds.Count > 0 || formQueryIds.Count > 0)
                {
                    await AuthorityEvidenceClassifier.ClassifyEvidenceInOpenConnectionAsync(
                            db,
                            _blinding,
                            tenant,
                            obsIds,
                            findingIds,
                            assertionIds,
                            formQueryIds,
                            resolvedPaths,
                            resolvedEvidence,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var paths = new Dictionary<NodeRef, ScopePath>(distinct.Count);
        var evidence = new Dictionary<NodeRef, EvidenceNodeClassification>();
        foreach (var resource in distinct)
        {
            paths[resource] = resolvedPaths.GetValueOrDefault((resource.Kind, resource.Id), ScopePath.TenantWide);
            if (resolvedEvidence.TryGetValue((resource.Kind, resource.Id), out var info))
            {
                evidence[resource.Unversioned()] = info;
            }
        }

        return (paths, evidence);
    }

    public async Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var paths = await PathsOfAsync(tenant, [resource], cancellationToken).ConfigureAwait(false);
        return paths.GetValueOrDefault(resource, ScopePath.TenantWide);
    }

    public async Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken) =>
        (await ResolveVisibilityPathsAsync(tenant, resources, cancellationToken).ConfigureAwait(false)).Paths;

    public async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlySet<NodeRef>? PotentiallyBlindedEvidence)> ResolveVisibilityPathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return (new Dictionary<NodeRef, ScopePath>(), FrozenSet<NodeRef>.Empty);
        }

        var byKind = distinct
            .Select(r => r.Unversioned())
            .Distinct()
            .Where(r => AuthorityScopePathReader.PathBearingKinds.Contains(r.Kind))
            .GroupBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var resolved = new Dictionary<(string Kind, Guid Id), ScopePath>();
        var potentiallyBlinded = new HashSet<NodeRef>();
        if (byKind.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var group in byKind)
                {
                    await AuthorityScopePathReader.LoadPathsForKindAsync(
                            db,
                            tenant,
                            group.Key,
                            [.. group.Select(r => r.Id)],
                            resolved,
                            potentiallyBlinded,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var result = new Dictionary<NodeRef, ScopePath>(distinct.Count);
        foreach (var resource in distinct)
        {
            result[resource] = resolved.GetValueOrDefault((resource.Kind, resource.Id), ScopePath.TenantWide);
        }

        var prefilteredBlinded = ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance)
            ? (IReadOnlySet<NodeRef>?)potentiallyBlinded
            : null;
        return (result, prefilteredBlinded);
    }

    public async Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        var paths = await StudySitePathsAsync(tenant, [node], cancellationToken).ConfigureAwait(false);
        return paths.GetValueOrDefault(node, []);
    }

    public async Task<IReadOnlyDictionary<NodeRef, IReadOnlyList<ScopePath>>> StudySitePathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, IReadOnlyList<ScopePath>>();
        }

        var participantIds = distinct
            .Where(n => n.Kind == NodeKinds.Participant)
            .Select(n => ParticipantId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var siteIds = distinct
            .Where(n => n.Kind == NodeKinds.Site)
            .Select(n => SiteId.FromGuid(n.Id))
            .Distinct()
            .ToList();

        var reachedByObject = new Dictionary<(string Kind, Guid Id), List<ScopePath>>();
        if (participantIds.Count > 0 || siteIds.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await AuthorityScopePathReader.LoadStudySitePathsInOpenConnectionAsync(
                        db,
                        tenant,
                        participantIds,
                        siteIds,
                        reachedByObject,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var result = new Dictionary<NodeRef, IReadOnlyList<ScopePath>>(distinct.Count);
        foreach (var node in distinct)
        {
            result[node] = reachedByObject.TryGetValue((node.Kind, node.Id), out var list) ? list : [];
        }

        return result;
    }
}

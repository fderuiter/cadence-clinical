using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal static class ScopeGraphConceptReader
{
    public static async Task<ScopeNode?> ConceptAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        NodeRef node,
        CancellationToken cancellationToken) =>
        (await LoadConceptsAsync(db, tenant, tenantRef, [node], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(ConceptId.FromGuid(node.Id));

    public static async Task<Dictionary<ConceptId, ScopeNode>> LoadConceptsAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        IReadOnlyCollection<NodeRef> requestedNodes,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ConceptId, ScopeNode>(requestedNodes.Count);
        var requestedByConcept = new Dictionary<ConceptId, long?>();
        foreach (var node in requestedNodes)
        {
            requestedByConcept.TryAdd(ConceptId.FromGuid(node.Id), node.Version);
        }

        var conceptIds = requestedByConcept.Keys.ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && conceptIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PreferredLabel, c.ExternalCode, c.Status, c.Version })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (concepts.Count == 0)
        {
            return result;
        }

        var revisionConceptIds = concepts
            .Where(c => requestedByConcept.TryGetValue(c.Id, out var reqVer)
                && reqVer is { } rv
                && rv != c.Version
                && rv <= int.MaxValue)
            .Select(c => c.Id)
            .ToList();
        var revisionLabels = revisionConceptIds.Count == 0
            ? []
            : (await db.ConceptRevisions.AsNoTracking()
                .Where(r => r.TenantId == tenant && revisionConceptIds.Contains(r.ConceptId))
                .Select(r => new { r.ConceptId, r.Version, r.PreferredLabel })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(r => (r.ConceptId, r.Version), r => r.PreferredLabel);

        foreach (var concept in concepts)
        {
            var label = concept.PreferredLabel;
            long resolvedVersion = concept.Version;
            if (requestedByConcept.TryGetValue(concept.Id, out var reqVer)
                && reqVer is { } requestedVersion
                && requestedVersion != concept.Version
                && requestedVersion <= int.MaxValue
                && revisionLabels.TryGetValue((concept.Id, (int)requestedVersion), out var revisionLabel)
                && !string.IsNullOrWhiteSpace(revisionLabel))
            {
                label = revisionLabel;
                resolvedVersion = requestedVersion;
            }

            result[concept.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(concept.Id, resolvedVersion), label, concept.ExternalCode, concept.Status.ToString()),
                tenantRef);
        }

        return result;
    }

    public static async Task<Dictionary<(ConceptId ConceptId, int Version), (string? Label, string? ExternalCode)>> ResolveConceptMetadataAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<Observation> observations,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(ConceptId, int), (string?, string?)>();
        if (observations.Count == 0)
        {
            return result;
        }

        var conceptIds = observations.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && conceptIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PreferredLabel, c.ExternalCode })
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);
        var revisions = await db.ConceptRevisions.AsNoTracking()
            .Where(r => r.TenantId == tenant && conceptIds.Contains(r.ConceptId))
            .Select(r => new { r.ConceptId, r.Version, r.PreferredLabel })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var revisionLabels = revisions.ToDictionary(r => (r.ConceptId, r.Version), r => r.PreferredLabel);

        foreach (var key in observations.Select(o => (o.ConceptId, o.ConceptVersion)).Distinct())
        {
            concepts.TryGetValue(key.ConceptId, out var current);
            var label = revisionLabels.TryGetValue(key, out var revLabel) && !string.IsNullOrWhiteSpace(revLabel)
                ? revLabel
                : current?.PreferredLabel;
            result[key] = (label, current?.ExternalCode);
        }

        return result;
    }
}

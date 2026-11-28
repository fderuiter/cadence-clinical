using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal static class ScopeGraphObservationAndChildrenReader
{
    private const int ObservationValueLabelLength = 60;

    public static async Task<Dictionary<CollectionInstanceId, ScopeNode>> LoadCollectionInstancesAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instanceIds,
        CancellationToken cancellationToken)
    {
        var instances = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.TenantId == tenant && instanceIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                instances.Select(i => i.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);
        var result = new Dictionary<CollectionInstanceId, ScopeNode>(instances.Count);
        foreach (var instance in instances)
        {
            var design = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId);
            result[instance.Id] = new ScopeNode(
                CollectionInstanceSummary(instance, design),
                NodeRef.For(instance.EnrollmentId),
                FallbackParent: NodeRef.For(instance.StudyId));
        }

        return result;
    }

    public static async Task<Dictionary<ObservationId, ScopeNode>> LoadObservationsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observationIds,
        CancellationToken cancellationToken)
    {
        var observations = await db.Observations.AsNoTracking()
            .Where(o => o.TenantId == tenant && observationIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (observations.Count == 0)
        {
            return [];
        }

        var enrollmentIds = observations.Select(o => o.EnrollmentId).Distinct().ToList();
        var parentInstances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.TenantId == tenant
                && enrollmentIds.Contains(c.EnrollmentId)
                && c.Members.Any(m => observationIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var parentByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in parentInstances)
        {
            foreach (var member in instance.Members)
            {
                if (observationIds.Contains(member.ObservationId))
                {
                    parentByObservation.TryAdd(member.ObservationId, (instance, member.ItemId));
                }
            }
        }

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                parentInstances.Select(p => p.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);
        var concepts = await ScopeGraphConceptReader.ResolveConceptMetadataAsync(db, tenant, observations, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<ObservationId, ScopeNode>(observations.Count);
        foreach (var observation in observations)
        {
            CollectionItem? item = null;
            string? itemId = null;
            CollectionInstance? parentInstance = null;
            if (parentByObservation.TryGetValue(observation.Id, out var parentInfo))
            {
                parentInstance = parentInfo.Instance;
                itemId = parentInfo.ItemId;
                var spec = designsByVersion.GetValueOrDefault(parentInstance.ProtocolVersionId)?.CollectionSpecs
                    .FirstOrDefault(s => string.Equals(s.Id, parentInstance.CollectionSpecId, StringComparison.Ordinal));
                item = spec?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            }

            var (label, externalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
            var parentRef = parentInstance is not null
                ? NodeRef.For(parentInstance.Id)
                : NodeRef.For(observation.EnrollmentId);
            result[observation.Id] = new ScopeNode(
                ObservationSummary(observation, item, itemId, label, externalCode),
                parentRef,
                FallbackParent: NodeRef.For(observation.StudyId));
        }

        return result;
    }

    public static async Task<IReadOnlyList<ScopeChild>> EnrollmentChildrenAsync(
        KernelDbContext db,
        TenantId tenant,
        EnrollmentId enrollmentId,
        CancellationToken cancellationToken)
    {
        var instances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.TenantId == tenant && c.EnrollmentId == enrollmentId)
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var designsByVersion = await LoadDesignsByVersionAsync(
                db,
                tenant,
                instances.Select(c => c.ProtocolVersionId),
                cancellationToken)
            .ConfigureAwait(false);

        var children = new List<ScopeChild>(instances.Count);
        var collectedObservationIds = new HashSet<ObservationId>();
        foreach (var instance in instances)
        {
            foreach (var member in instance.Members)
            {
                collectedObservationIds.Add(member.ObservationId);
            }

            var design = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId);
            children.Add(new ScopeChild(CollectionInstanceSummary(instance, design), instance.Members.Count, instance.PlannedActivityId));
        }

        var standaloneObservations = (await db.Observations.AsNoTracking()
                .Where(o => o.TenantId == tenant && o.EnrollmentId == enrollmentId)
                .OrderBy(o => o.ObservedAt).ThenBy(o => o.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(o => !collectedObservationIds.Contains(o.Id))
            .ToList();
        var concepts = await ScopeGraphConceptReader.ResolveConceptMetadataAsync(db, tenant, standaloneObservations, cancellationToken)
            .ConfigureAwait(false);
        foreach (var observation in standaloneObservations)
        {
            var (label, externalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
            children.Add(new ScopeChild(
                ObservationSummary(observation, item: null, itemId: null, label, externalCode),
                ChildCount: 0));
        }

        return children;
    }

    public static async Task<IReadOnlyList<ScopeChild>> CollectionInstanceChildrenAsync(
        KernelDbContext db,
        TenantId tenant,
        CollectionInstanceId instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.TenantId == tenant && c.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null || instance.Members.Count == 0)
        {
            return [];
        }

        var memberIds = instance.Members.Select(m => m.ObservationId).ToList();
        var observations = await db.Observations.AsNoTracking()
            .Where(o => o.TenantId == tenant && memberIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken)
            .ConfigureAwait(false);
        var concepts = await ScopeGraphConceptReader.ResolveConceptMetadataAsync(db, tenant, observations.Values, cancellationToken)
            .ConfigureAwait(false);

        var content = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == instance.ProtocolVersionId)
            .Select(v => v.Content)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var spec = content is null
            ? null
            : ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
        var itemOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        if (spec is not null)
        {
            for (var index = 0; index < spec.Items.Count; index++)
            {
                itemOrder[spec.Items[index].Id] = index;
            }
        }

        var itemsById = spec?.Items.ToDictionary(i => i.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, CollectionItem>(StringComparer.Ordinal);

        return
        [
            .. instance.Members
                .Where(m => observations.ContainsKey(m.ObservationId))
                .OrderBy(m => itemOrder.TryGetValue(m.ItemId, out var rank) ? rank : int.MaxValue)
                .ThenBy(m => m.ItemId, StringComparer.Ordinal)
                .Select(m =>
                {
                    var observation = observations[m.ObservationId];
                    var (label, externalCode) = concepts.GetValueOrDefault((observation.ConceptId, observation.ConceptVersion));
                    return new ScopeChild(
                        ObservationSummary(observation, itemsById.GetValueOrDefault(m.ItemId), m.ItemId, label, externalCode),
                        ChildCount: 0);
                }),
        ];
    }

    private static async Task<Dictionary<ProtocolVersionId, ProtocolDesign?>> LoadDesignsByVersionAsync(
        KernelDbContext db,
        TenantId tenant,
        IEnumerable<ProtocolVersionId> protocolVersionIds,
        CancellationToken cancellationToken)
    {
        var versionIds = protocolVersionIds.Distinct().ToList();
        if (versionIds.Count == 0)
        {
            return [];
        }

        return (await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Content })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(
                v => v.Id,
                v => ProtocolDesignDocument.Read(v.Content).Design);
    }

    private static NodeSummary CollectionInstanceSummary(CollectionInstance instance, ProtocolDesign? design)
    {
        var specName = design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal))?.Name;
        var formTitle = string.IsNullOrWhiteSpace(specName) ? instance.CollectionSpecId : specName.Trim();

        string? visitTitle = null;
        if (!string.IsNullOrWhiteSpace(instance.PlannedActivityId))
        {
            var activityName = design?.PlannedActivities
                .FirstOrDefault(a => string.Equals(a.Id, instance.PlannedActivityId, StringComparison.Ordinal))?.Name;
            visitTitle = string.IsNullOrWhiteSpace(activityName) ? instance.PlannedActivityId.Trim() : activityName.Trim();
        }

        var title = string.IsNullOrWhiteSpace(visitTitle) ? formTitle : $"{formTitle} ({visitTitle})";
        return new NodeSummary(NodeRef.For(instance.Id, instance.VersionIndex), title, instance.CollectionSpecId);
    }

    private static NodeSummary ObservationSummary(
        Observation observation,
        CollectionItem? item,
        string? itemId,
        string? conceptLabel,
        string? conceptExternalCode)
    {
        var conceptCode = observation.Concept.ToString();
        string label;
        if (!string.IsNullOrWhiteSpace(item?.Prompt))
        {
            label = item.Prompt.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(conceptLabel))
        {
            label = conceptLabel.Trim();
        }
        else
        {
            label = itemId ?? conceptExternalCode ?? conceptCode;
        }

        var code = itemId ?? conceptExternalCode ?? conceptCode;
        return new NodeSummary(
            NodeRef.For(observation.Id, observation.Revision),
            ObservationLabel(label, observation.Value),
            code,
            $"r{observation.Revision}");
    }

    private static string ObservationLabel(string? conceptLabel, ObservationValue value)
    {
        var cleanConcept = string.IsNullOrWhiteSpace(conceptLabel) ? null : conceptLabel.Trim();
        string? valueText = null;
        if (value is not MissingValue { Reason: NullFlavor.NotAsked })
        {
            var singleLine = value.ToDisplayString().ReplaceLineEndings(" ").Trim();
            if (singleLine.Length > 0)
            {
                valueText = singleLine.Length <= ObservationValueLabelLength
                    ? singleLine
                    : $"{singleLine[..(ObservationValueLabelLength - 1)]}…";
            }
        }

        if (cleanConcept is not null && valueText is not null)
        {
            return $"{cleanConcept}: {valueText}";
        }

        return cleanConcept ?? valueText ?? "Observation";
    }
}

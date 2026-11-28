using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Execution.Endpoints;

internal static class FormBlindingAndTimingHelper
{
    public static ValueTask<VisitTimingAssessment?> ResolveVisitTimingAsync(
        TenantId tenant,
        CollectionInstanceRecord record,
        ProtocolVersionId protocolVersionId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        ProtocolDesign? governingDesign,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        if (record.VisitTimingEvaluated)
        {
            return ValueTask.FromResult(record.VisitTimingAssessment);
        }

        if (governingDesign is not null)
        {
            return ValueTask.FromResult(VisitTimingEvaluator.Assess(
                governingDesign,
                plannedActivityId,
                collectedAt,
                record.EnrollmentReferenceAt));
        }

        return new ValueTask<VisitTimingAssessment?>(VisitTimingEvaluator.AssessAsync(
            tenant,
            protocolVersionId,
            plannedActivityId,
            collectedAt,
            record.EnrollmentReferenceAt,
            services?.GetService<IProtocolVersionReader>(),
            cancellationToken));
    }

    public static List<NodeRef> CollectFormNodes(CollectionInstanceRecord record)
    {
        var nodes = new List<NodeRef> { NodeRef.For(record.EnrollmentId) };
        nodes.AddRange(record.Values.Where(v => !v.ObservationId.IsEmpty).Select(v => NodeRef.For(v.ObservationId)));
        nodes.AddRange(record.Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!)
            .Where(sv => !sv.ObservationId.IsEmpty)
            .Select(sv => NodeRef.For(sv.ObservationId)));
        nodes.AddRange(record.Assertions.Where(a => a.Target.Kind == NodeKinds.Observation).Select(a => a.Target));
        nodes.AddRange(record.Locks.Where(l => l.Scope.Kind == NodeKinds.Observation).Select(l => l.Scope));
        return nodes;
    }

    public static async Task<(HashSet<string> BlindedItemIds, HashSet<ObservationId> BlindedObservationIds)> ResolveBlindedFormTargetsAsync(
        TenantId tenant,
        CollectionInstanceRecord record,
        IReadOnlyDictionary<NodeRef, MaskReason> masks,
        bool canUnblind,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var blindedObservationIds = new HashSet<ObservationId>(
            masks.Keys
                .Where(n => n.Kind == NodeKinds.Observation)
                .Select(n => ObservationId.FromGuid(n.Id)));
        var blindedItemIds = record.Values
            .Where(recorded => !recorded.ObservationId.IsEmpty && blindedObservationIds.Contains(recorded.ObservationId))
            .Select(recorded => recorded.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var signed in record.Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!)
            .Where(sv => !sv.ObservationId.IsEmpty && blindedObservationIds.Contains(sv.ObservationId)))
        {
            blindedItemIds.Add(signed.ItemId);
        }

        if (!canUnblind)
        {
            if (record.BlindedItemIds is not null)
            {
                blindedItemIds.UnionWith(record.BlindedItemIds);
            }

            if (record.BlindedObservationIds is not null)
            {
                blindedObservationIds.UnionWith(record.BlindedObservationIds);
            }

            var classifier = services?.GetService<IBlindedTreatmentClassifier>()
                ?? BlindedTreatmentClassifier.Instance;
            var reuseKernelBlinding = record.BlindedItemIds is not null
                && record.BlindedObservationIds is not null
                && ReferenceEquals(classifier, BlindedTreatmentClassifier.Instance);
            IReadOnlyList<ProtocolDesign>? designs = null;
            IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>? concepts = null;
            if (!reuseKernelBlinding)
            {
                designs = await LoadStudyDesignsAsync(tenant, record.StudyId, record.ProtocolVersionId, services, cancellationToken).ConfigureAwait(false);
                concepts = await LoadItemConceptsAsync(tenant, record.Items, services, cancellationToken).ConfigureAwait(false);
            }

            var specById = record.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
            var valuesById = record.Values.ToDictionary(v => v.ItemId, StringComparer.Ordinal);

            foreach (var itemId in specById.Keys.Concat(valuesById.Keys).Distinct(StringComparer.Ordinal))
            {
                specById.TryGetValue(itemId, out var specItem);
                valuesById.TryGetValue(itemId, out var recordedValue);
                BlindedConceptMetadata? conceptMeta = specItem is not null
                    && concepts is not null
                    && concepts.TryGetValue(specItem.Concept.ConceptId, out var m)
                    ? m
                    : null;
                var valueObservationBlinded = recordedValue is not null
                    && !recordedValue.ObservationId.IsEmpty
                    && blindedObservationIds.Contains(recordedValue.ObservationId);
                if (blindedItemIds.Contains(itemId)
                    || valueObservationBlinded
                    || classifier.IsBlindedCollectionItem(
                        record.CollectionSpecId,
                        record.CollectionTitle,
                        specItem,
                        recordedValue?.Value,
                        conceptMeta,
                        designs))
                {
                    blindedItemIds.Add(itemId);
                    if (recordedValue is not null && !recordedValue.ObservationId.IsEmpty)
                    {
                        blindedObservationIds.Add(recordedValue.ObservationId);
                    }
                }
            }

            foreach (var signedValue in record.Attestations
                .Where(signature => signature.SignedValues is not null)
                .SelectMany(signature => signature.SignedValues!))
            {
                specById.TryGetValue(signedValue.ItemId, out var specItem);
                BlindedConceptMetadata? conceptMeta = specItem is not null
                    && concepts is not null
                    && concepts.TryGetValue(specItem.Concept.ConceptId, out var m)
                    ? m
                    : null;
                var signedObservationBlinded = !signedValue.ObservationId.IsEmpty
                    && blindedObservationIds.Contains(signedValue.ObservationId);
                if (blindedItemIds.Contains(signedValue.ItemId)
                    || signedObservationBlinded
                    || classifier.IsBlindedCollectionItem(
                        record.CollectionSpecId,
                        record.CollectionTitle,
                        specItem,
                        signedValue.Value,
                        conceptMeta,
                        designs))
                {
                    blindedItemIds.Add(signedValue.ItemId);
                    if (!signedValue.ObservationId.IsEmpty)
                    {
                        blindedObservationIds.Add(signedValue.ObservationId);
                    }
                }
            }
        }

        return (blindedItemIds, blindedObservationIds);
    }

    private static async Task<IReadOnlyList<ProtocolDesign>> LoadStudyDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        ProtocolVersionId governingVersionId,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var reader = services?.GetService<IProtocolVersionReader>();
        if (reader is null)
        {
            return [];
        }

        var studyDesigns = await reader.GetDesignsForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (studyDesigns.TryGetValue(governingVersionId, out var governing))
        {
            return [governing];
        }

        if (studyDesigns.Count > 0)
        {
            return [.. studyDesigns.Values];
        }

        if (!governingVersionId.IsEmpty
            && await reader.GetDesignAsync(tenant, governingVersionId, cancellationToken).ConfigureAwait(false) is { } fallback)
        {
            return [fallback];
        }

        return [];
    }

    private static Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadItemConceptsAsync(
        TenantId tenant,
        IReadOnlyList<CollectionItem> items,
        IServiceProvider? services,
        CancellationToken cancellationToken) =>
        BlindedConceptMetadata.ResolveManyAsync(
            services?.GetService<IConceptRegistry>(),
            tenant,
            items.Select(i => i.Concept),
            cancellationToken);
}

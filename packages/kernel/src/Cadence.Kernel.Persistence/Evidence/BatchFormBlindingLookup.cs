using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Persistence.Evidence;

internal sealed class BatchFormBlindingLookup(
    IBlindedTreatmentClassifier blinding,
    IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptsById,
    IReadOnlyList<ProtocolDesign> designs)
{
    private readonly Dictionary<SpecKey, SpecBlindingEntry> _specCache = [];
    private readonly Dictionary<MemberBlindingKey, bool> _memberCache = [];
    private readonly Dictionary<UnmappedSignedItemKey, bool> _unmappedSignedItemCache = [];
    private readonly Dictionary<ObservationBlindingKey, bool> _queryObservationCache = [];

    public static BatchFormBlindingLookup FromConcepts(
        IBlindedTreatmentClassifier blinding,
        IReadOnlyDictionary<ConceptId, Concept> concepts,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var metadata = new Dictionary<ConceptId, BlindedConceptMetadata>(concepts.Count);
        foreach (var (id, concept) in concepts)
        {
            if (BlindedObservationClassifier.ToMetadata(concept) is { } meta)
            {
                metadata[id] = meta;
            }
        }

        return new BatchFormBlindingLookup(blinding, metadata, designs);
    }

    public (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyForm(
        CollectionInstance instance,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyList<FormAttestationView> attestations)
    {
        var specEntry = GetOrCreateSpecEntry(instance.ProtocolVersionId, instance.CollectionSpecId, collectionTitle, items);
        HashSet<string>? extraBlindedItems = null;
        HashSet<ObservationId>? blindedObservations = null;

        foreach (var member in instance.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            if (specEntry.SpecBlinded || specEntry.BlindedItemIds.Contains(member.ItemId))
            {
                blindedObservations ??= [];
                blindedObservations.Add(obs.Id);
                continue;
            }

            specEntry.ItemsById.TryGetValue(member.ItemId, out var specItem);
            bool memberBlinded;
            if (HasMaskedValue(obs))
            {
                // Evaluate per-observation/per-revision masked values directly so masked values never taint
                // structural memo entries shared with unmasked sibling observations (K-AUTH-4).
                var conceptMeta = ResolveConcept(obs.ConceptId);
                memberBlinded = CollectionMemberBlindingClassifier.IsCurrentOrHistoricalMemberBlinded(
                    blinding,
                    instance.CollectionSpecId,
                    collectionTitle,
                    member.ItemId,
                    specItem,
                    obs,
                    conceptMeta,
                    designs);
            }
            else
            {
                memberBlinded = IsStructurallyBlindedMember(
                    instance.ProtocolVersionId,
                    instance.CollectionSpecId,
                    collectionTitle,
                    member.ItemId,
                    specItem,
                    obs.ConceptId);
            }

            if (memberBlinded)
            {
                extraBlindedItems ??= new HashSet<string>(StringComparer.Ordinal);
                extraBlindedItems.Add(member.ItemId);
                blindedObservations ??= [];
                blindedObservations.Add(obs.Id);
            }
        }

        for (var a = 0; a < attestations.Count; a++)
        {
            var signedValues = attestations[a].SignedValues;
            if (signedValues is null)
            {
                continue;
            }

            for (var s = 0; s < signedValues.Count; s++)
            {
                var signedValue = signedValues[s];
                if (specEntry.SpecBlinded
                    || specEntry.BlindedItemIds.Contains(signedValue.ItemId)
                    || (extraBlindedItems is not null && extraBlindedItems.Contains(signedValue.ItemId)))
                {
                    blindedObservations ??= [];
                    blindedObservations.Add(signedValue.ObservationId);
                    continue;
                }

                specEntry.ItemsById.TryGetValue(signedValue.ItemId, out var specItem);
                var conceptMeta = specItem is not null ? ResolveConcept(specItem.Concept.ConceptId) : null;
                bool signedBlinded;
                if (signedValue.Value is MissingValue { Reason: NullFlavor.Masked })
                {
                    signedBlinded = blinding.IsBlindedCollectionItem(
                        instance.CollectionSpecId,
                        collectionTitle,
                        specItem,
                        signedValue.Value,
                        conceptMeta,
                        designs);
                }
                else if (specItem is not null)
                {
                    // Non-masked mapped spec items were already classified in specEntry.BlindedItemIds.
                    signedBlinded = false;
                }
                else
                {
                    var unmappedKey = new UnmappedSignedItemKey(instance.ProtocolVersionId, instance.CollectionSpecId, collectionTitle, signedValue.ItemId);
                    if (!_unmappedSignedItemCache.TryGetValue(unmappedKey, out signedBlinded))
                    {
                        signedBlinded = blinding.IsBlindedCollectionItem(
                            instance.CollectionSpecId,
                            collectionTitle,
                            item: null,
                            value: null,
                            concept: null,
                            designs);
                        _unmappedSignedItemCache[unmappedKey] = signedBlinded;
                    }
                }

                if (signedBlinded)
                {
                    extraBlindedItems ??= new HashSet<string>(StringComparer.Ordinal);
                    extraBlindedItems.Add(signedValue.ItemId);
                    blindedObservations ??= [];
                    blindedObservations.Add(signedValue.ObservationId);
                }
            }
        }

        IReadOnlySet<string> finalItems;
        if (extraBlindedItems is null)
        {
            finalItems = specEntry.BlindedItemIds;
        }
        else
        {
            extraBlindedItems.UnionWith(specEntry.BlindedItemIds);
            finalItems = extraBlindedItems.ToFrozenSet(StringComparer.Ordinal);
        }

        IReadOnlySet<ObservationId> finalObservations = blindedObservations is null
            ? FrozenSet<ObservationId>.Empty
            : blindedObservations.ToFrozenSet();
        return (finalItems, finalObservations);
    }

    internal (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyQueryTarget(
        QueryTargetFormRow form,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, QueryTargetObservationRow> observations)
    {
        var specEntry = GetOrCreateSpecEntry(form.ProtocolVersionId, form.CollectionSpecId, collectionTitle, items);
        HashSet<string>? extraBlindedItems = null;
        HashSet<ObservationId>? blindedObservations = null;

        for (var i = 0; i < form.Members.Count; i++)
        {
            var member = form.Members[i];
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            if (specEntry.SpecBlinded || specEntry.BlindedItemIds.Contains(member.ItemId))
            {
                blindedObservations ??= [];
                blindedObservations.Add(obs.Id);
                continue;
            }

            bool obsBlinded;
            if (obs.Value is MissingValue { Reason: NullFlavor.Masked })
            {
                obsBlinded = blinding.IsBlindedObservation(
                    obs.ConceptId,
                    obs.Value,
                    ResolveConcept(obs.ConceptId),
                    form.CollectionSpecId,
                    member.ItemId,
                    designs);
            }
            else
            {
                var obsKey = new ObservationBlindingKey(form.CollectionSpecId, member.ItemId, obs.ConceptId);
                if (!_queryObservationCache.TryGetValue(obsKey, out obsBlinded))
                {
                    obsBlinded = blinding.IsBlindedObservation(
                        obs.ConceptId,
                        value: null,
                        ResolveConcept(obs.ConceptId),
                        form.CollectionSpecId,
                        member.ItemId,
                        designs);
                    _queryObservationCache[obsKey] = obsBlinded;
                }
            }

            if (obsBlinded)
            {
                extraBlindedItems ??= new HashSet<string>(StringComparer.Ordinal);
                extraBlindedItems.Add(member.ItemId);
                blindedObservations ??= [];
                blindedObservations.Add(obs.Id);
            }
        }

        IReadOnlySet<string> finalItems;
        if (extraBlindedItems is null)
        {
            finalItems = specEntry.BlindedItemIds;
        }
        else
        {
            extraBlindedItems.UnionWith(specEntry.BlindedItemIds);
            finalItems = extraBlindedItems.ToFrozenSet(StringComparer.Ordinal);
        }

        IReadOnlySet<ObservationId> finalObservations = blindedObservations is null
            ? FrozenSet<ObservationId>.Empty
            : blindedObservations.ToFrozenSet();
        return (finalItems, finalObservations);
    }

    private SpecBlindingEntry GetOrCreateSpecEntry(
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items)
    {
        var key = new SpecKey(protocolVersionId, collectionSpecId, collectionTitle);
        if (_specCache.TryGetValue(key, out var existing) && ReferenceEquals(existing.SourceItems, items))
        {
            return existing;
        }

        var specBlinded = blinding.IsBlindedCollectionSpec(collectionSpecId, collectionTitle, designs: designs);
        var itemsById = new Dictionary<string, CollectionItem>(items.Count, StringComparer.Ordinal);
        HashSet<string>? blindedItems = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            itemsById.TryAdd(item.Id, item);
            if (specBlinded
                || blinding.IsBlindedCollectionItem(
                    collectionSpecId,
                    collectionTitle,
                    item,
                    value: null,
                    ResolveConcept(item.Concept.ConceptId),
                    designs))
            {
                blindedItems ??= new HashSet<string>(StringComparer.Ordinal);
                blindedItems.Add(item.Id);
            }
        }

        var frozenBlinded = blindedItems is null
            ? FrozenSet<string>.Empty
            : blindedItems.ToFrozenSet(StringComparer.Ordinal);
        var created = new SpecBlindingEntry(specBlinded, items, itemsById, frozenBlinded);
        _specCache[key] = created;
        return created;
    }

    private bool IsStructurallyBlindedMember(
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? collectionTitle,
        string itemId,
        CollectionItem? specItem,
        ConceptId observationConceptId)
    {
        var key = new MemberBlindingKey(
            protocolVersionId,
            collectionSpecId,
            collectionTitle,
            itemId,
            observationConceptId,
            specItem is not null);
        if (_memberCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var conceptMeta = ResolveConcept(observationConceptId);
        var isBlinded = blinding.IsBlindedCollectionItem(
                collectionSpecId,
                collectionTitle,
                specItem,
                value: null,
                conceptMeta,
                designs)
            || blinding.IsBlindedObservation(
                observationConceptId,
                value: null,
                conceptMeta,
                collectionSpecId,
                itemId,
                designs);
        _memberCache[key] = isBlinded;
        return isBlinded;
    }

    private BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
        conceptsById.TryGetValue(id, out var found) ? found : null;

    private static bool HasMaskedValue(Observation obs)
    {
        if (obs.Value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        foreach (var revision in obs.Revisions)
        {
            if (revision.Value is MissingValue { Reason: NullFlavor.Masked })
            {
                return true;
            }
        }

        return false;
    }

    private sealed record SpecBlindingEntry(
        bool SpecBlinded,
        IReadOnlyList<CollectionItem> SourceItems,
        Dictionary<string, CollectionItem> ItemsById,
        FrozenSet<string> BlindedItemIds);

    private readonly record struct SpecKey(ProtocolVersionId ProtocolVersionId, string CollectionSpecId, string? CollectionTitle);

    private readonly record struct MemberBlindingKey(
        ProtocolVersionId ProtocolVersionId,
        string CollectionSpecId,
        string? CollectionTitle,
        string ItemId,
        ConceptId ObservationConceptId,
        bool HasSpecItem);

    private readonly record struct UnmappedSignedItemKey(
        ProtocolVersionId ProtocolVersionId,
        string CollectionSpecId,
        string? CollectionTitle,
        string ItemId);

    private readonly record struct ObservationBlindingKey(string CollectionSpecId, string ItemId, ConceptId ConceptId);
}

using System;
using System.Collections.Generic;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Rules.Evaluation;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// Pre-indexed protocol blinding markers built once per snapshot masking or validation call so observation, item, and
/// finding checks run in constant time without rescanning <see cref="ProtocolDesign"/> trees.
/// </summary>
internal sealed class ProtocolBlindingIndex
{
    private readonly HashSet<ConceptId> _interventionProductConcepts;
    private readonly HashSet<ConceptId> _blindedRawConcepts;
    private readonly HashSet<string> _blindedDesignSpecIds;
    private readonly HashSet<ConceptId> _conceptsInBlindedSpecs;
    private readonly HashSet<(string SpecId, string ItemId)> _blindedSpecItemPairs;
    private readonly HashSet<(string SpecId, ConceptId ConceptId)> _blindedSpecConceptPairs;
    private readonly HashSet<string> _blindedAnySpecItemIds;
    private readonly HashSet<ConceptId> _blindedAnySpecItemConcepts;
    private readonly HashSet<(string SpecId, string ItemId)> _blindedVariablesBySpecAndItem;
    private readonly HashSet<string> _blindedVariablesAnySpecByItem;
    private readonly Dictionary<string, bool> _markerMemo = new(StringComparer.Ordinal);

    private ProtocolBlindingIndex(
        HashSet<ConceptId> interventionProductConcepts,
        HashSet<ConceptId> blindedRawConcepts,
        HashSet<string> blindedDesignSpecIds,
        HashSet<ConceptId> conceptsInBlindedSpecs,
        HashSet<(string SpecId, string ItemId)> blindedSpecItemPairs,
        HashSet<(string SpecId, ConceptId ConceptId)> blindedSpecConceptPairs,
        HashSet<string> blindedAnySpecItemIds,
        HashSet<ConceptId> blindedAnySpecItemConcepts,
        HashSet<(string SpecId, string ItemId)> blindedVariablesBySpecAndItem,
        HashSet<string> blindedVariablesAnySpecByItem)
    {
        _interventionProductConcepts = interventionProductConcepts;
        _blindedRawConcepts = blindedRawConcepts;
        _blindedDesignSpecIds = blindedDesignSpecIds;
        _conceptsInBlindedSpecs = conceptsInBlindedSpecs;
        _blindedSpecItemPairs = blindedSpecItemPairs;
        _blindedSpecConceptPairs = blindedSpecConceptPairs;
        _blindedAnySpecItemIds = blindedAnySpecItemIds;
        _blindedAnySpecItemConcepts = blindedAnySpecItemConcepts;
        _blindedVariablesBySpecAndItem = blindedVariablesBySpecAndItem;
        _blindedVariablesAnySpecByItem = blindedVariablesAnySpecByItem;
    }

    internal sealed record IndexedDesignSpec(
        CollectionSpec Spec,
        Dictionary<string, CollectionItem> ItemsById);

    public static Dictionary<string, IndexedDesignSpec> IndexFirstSpecsById(IReadOnlyList<ProtocolDesign> designs)
    {
        var map = new Dictionary<string, IndexedDesignSpec>(StringComparer.Ordinal);
        for (var d = 0; d < designs.Count; d++)
        {
            var specs = designs[d].CollectionSpecs;
            for (var s = 0; s < specs.Count; s++)
            {
                var spec = specs[s];
                if (map.ContainsKey(spec.Id))
                {
                    continue;
                }

                var itemsById = new Dictionary<string, CollectionItem>(spec.Items.Count, StringComparer.Ordinal);
                for (var i = 0; i < spec.Items.Count; i++)
                {
                    var item = spec.Items[i];
                    itemsById.TryAdd(item.Id, item);
                }

                map.Add(spec.Id, new IndexedDesignSpec(spec, itemsById));
            }
        }

        return map;
    }

    public static ProtocolBlindingIndex Create(
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts)
    {
        var interventionProductConcepts = new HashSet<ConceptId>();
        var blindedRawConcepts = new HashSet<ConceptId>();
        var blindedDesignSpecIds = new HashSet<string>(StringComparer.Ordinal);
        var conceptsInBlindedSpecs = new HashSet<ConceptId>();
        var blindedSpecItemPairs = new HashSet<(string SpecId, string ItemId)>();
        var blindedSpecConceptPairs = new HashSet<(string SpecId, ConceptId ConceptId)>();
        var blindedAnySpecItemIds = new HashSet<string>(StringComparer.Ordinal);
        var blindedAnySpecItemConcepts = new HashSet<ConceptId>();
        var blindedVariablesBySpecAndItem = new HashSet<(string SpecId, string ItemId)>();
        var blindedVariablesAnySpecByItem = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (conceptId, meta) in concepts)
        {
            if (MatchesConceptMarkers(meta))
            {
                blindedRawConcepts.Add(conceptId);
            }
        }

        for (var d = 0; d < designs.Count; d++)
        {
            var design = designs[d];
            var designInterventions = new HashSet<ConceptId>();
            for (var i = 0; i < design.Interventions.Count; i++)
            {
                if (design.Interventions[i].Product is { } product)
                {
                    interventionProductConcepts.Add(product.ConceptId);
                    designInterventions.Add(product.ConceptId);
                }
            }

            for (var s = 0; s < design.CollectionSpecs.Count; s++)
            {
                var spec = design.CollectionSpecs[s];
                var specBlinded = MatchesSpecMarkers(spec);
                if (specBlinded)
                {
                    blindedDesignSpecIds.Add(spec.Id);
                }

                var seenSpecItems = new HashSet<string>(spec.Items.Count, StringComparer.Ordinal);
                for (var i = 0; i < spec.Items.Count; i++)
                {
                    var item = spec.Items[i];
                    var itemConceptId = item.Concept.ConceptId;
                    if (specBlinded)
                    {
                        conceptsInBlindedSpecs.Add(itemConceptId);
                    }

                    var itemBlinded = MatchesItemMarkers(item);
                    if (itemBlinded)
                    {
                        blindedSpecItemPairs.Add((spec.Id, item.Id));
                        blindedSpecConceptPairs.Add((spec.Id, itemConceptId));
                        blindedAnySpecItemIds.Add(item.Id);
                        blindedAnySpecItemConcepts.Add(itemConceptId);
                    }

                    if (seenSpecItems.Add(item.Id)
                        && (specBlinded
                            || itemBlinded
                            || designInterventions.Contains(itemConceptId)
                            || blindedRawConcepts.Contains(itemConceptId)))
                    {
                        blindedVariablesBySpecAndItem.Add((spec.Id, item.Id));
                        blindedVariablesAnySpecByItem.Add(item.Id);
                    }
                }
            }
        }

        return new ProtocolBlindingIndex(
            interventionProductConcepts,
            blindedRawConcepts,
            blindedDesignSpecIds,
            conceptsInBlindedSpecs,
            blindedSpecItemPairs,
            blindedSpecConceptPairs,
            blindedAnySpecItemIds,
            blindedAnySpecItemConcepts,
            blindedVariablesBySpecAndItem,
            blindedVariablesAnySpecByItem);
    }

    public bool IsBlindedObservation(
        ConceptId conceptId,
        ObservationValue? value,
        ConceptId metadataConceptId,
        string? collectionSpecId,
        string? itemId)
    {
        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (MatchesMarkersCached(collectionSpecId) || MatchesMarkersCached(itemId))
        {
            return true;
        }

        if (_interventionProductConcepts.Contains(conceptId))
        {
            return true;
        }

        if (collectionSpecId is not null)
        {
            if (_blindedDesignSpecIds.Contains(collectionSpecId))
            {
                return true;
            }

            var itemMatch = itemId is not null
                ? _blindedSpecItemPairs.Contains((collectionSpecId, itemId))
                : _blindedSpecConceptPairs.Contains((collectionSpecId, conceptId));
            if (itemMatch)
            {
                return true;
            }
        }
        else
        {
            if (_conceptsInBlindedSpecs.Contains(conceptId))
            {
                return true;
            }

            var anySpecItemMatch = itemId is not null
                ? _blindedAnySpecItemIds.Contains(itemId)
                : _blindedAnySpecItemConcepts.Contains(conceptId);
            if (anySpecItemMatch)
            {
                return true;
            }
        }

        return _blindedRawConcepts.Contains(metadataConceptId);
    }

    public bool IsBlindedCollectionSpec(string? collectionSpecId, string? collectionTitle = null) =>
        MatchesMarkersCached(collectionSpecId)
        || MatchesMarkersCached(collectionTitle)
        || (!string.IsNullOrWhiteSpace(collectionSpecId) && _blindedDesignSpecIds.Contains(collectionSpecId));

    public bool IsBlindedCollectionItem(
        string? collectionSpecId,
        string? collectionTitle,
        CollectionItem? item,
        ObservationValue? value,
        ConceptId valueConceptId)
    {
        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (IsBlindedCollectionSpec(collectionSpecId, collectionTitle))
        {
            return true;
        }

        if (item is not null)
        {
            if (MatchesItemMarkersCached(item))
            {
                return true;
            }

            return IsBlindedObservation(item.Concept.ConceptId, value, valueConceptId, collectionSpecId, item.Id);
        }

        return _blindedRawConcepts.Contains(valueConceptId);
    }

    public bool IsBlindedViolation(RuleViolation violation)
    {
        if (IsBlindedCollectionSpec(violation.Dataset))
        {
            return true;
        }

        var observed = violation.Observed;
        if (observed.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < observed.Count; i++)
        {
            if (IsBlindedVariable(violation.Dataset, observed[i].Variable))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBlindedVariable(string? defaultDataset, string? variable)
    {
        if (MatchesMarkersCached(defaultDataset) || MatchesMarkersCached(variable))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        string? targetSpec = defaultDataset;
        var targetItem = variable;
        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            targetSpec = variable[..dot];
            targetItem = variable[(dot + 1)..];
            if (MatchesMarkersCached(targetSpec) || MatchesMarkersCached(targetItem))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(targetSpec)
            ? _blindedVariablesBySpecAndItem.Contains((targetSpec, targetItem))
            : _blindedVariablesAnySpecByItem.Contains(targetItem);
    }

    private bool MatchesItemMarkersCached(CollectionItem item) =>
        MatchesMarkersCached(item.Id)
        || MatchesMarkersCached(item.Purpose?.OperationalNeed)
        || MatchesMarkersCached(item.Prompt);

    private bool MatchesMarkersCached(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!_markerMemo.TryGetValue(text, out var matched))
        {
            matched = BlindedTreatmentClassifier.MatchesMarkers(text);
            _markerMemo[text] = matched;
        }

        return matched;
    }

    private static bool MatchesSpecMarkers(CollectionSpec spec) =>
        BlindedTreatmentClassifier.MatchesMarkers(spec.Id)
        || BlindedTreatmentClassifier.MatchesMarkers(spec.Name)
        || BlindedTreatmentClassifier.MatchesMarkers(spec.Purpose?.OperationalNeed);

    private static bool MatchesItemMarkers(CollectionItem item) =>
        BlindedTreatmentClassifier.MatchesMarkers(item.Id)
        || BlindedTreatmentClassifier.MatchesMarkers(item.Purpose?.OperationalNeed)
        || BlindedTreatmentClassifier.MatchesMarkers(item.Prompt);

    private static bool MatchesConceptMarkers(BlindedConceptMetadata concept) =>
        BlindedTreatmentClassifier.MatchesMarkers(concept.ExternalCode)
        || BlindedTreatmentClassifier.MatchesMarkers(concept.PreferredLabel)
        || BlindedTreatmentClassifier.MatchesMarkers(concept.Definition);
}

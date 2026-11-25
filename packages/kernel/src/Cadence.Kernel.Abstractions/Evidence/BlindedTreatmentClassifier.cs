using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Concept metadata fields inspected when classifying whether a concept represents blinded treatment allocation,
/// investigational product assignment, or masked dispensation (K-AUTH-4).
/// </summary>
/// <param name="ExternalCode">Optional external terminology or concept code.</param>
/// <param name="PreferredLabel">Human-readable preferred label.</param>
/// <param name="Definition">Human-readable definition text.</param>
public readonly record struct BlindedConceptMetadata(
    string? ExternalCode,
    string? PreferredLabel,
    string? Definition)
{
    /// <summary>
    /// Resolves <see cref="BlindedConceptMetadata"/> in batch for every non-empty <see cref="ConceptRef"/>,
    /// falling back to the current concept revision for any unresolved pinned revision.
    /// </summary>
    public static async Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> ResolveManyAsync(
        IConceptRegistry? concepts,
        TenantId tenant,
        IEnumerable<ConceptRef> conceptRefs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conceptRefs);
        if (concepts is null)
        {
            return new Dictionary<ConceptId, BlindedConceptMetadata>();
        }

        var distinctRefs = conceptRefs
            .Where(c => !c.ConceptId.IsEmpty)
            .Distinct()
            .ToList();
        if (distinctRefs.Count == 0)
        {
            return new Dictionary<ConceptId, BlindedConceptMetadata>();
        }

        var resolved = new Dictionary<ConceptId, BlindedConceptMetadata>(distinctRefs.Count);
        var byRef = await concepts.ResolveManyAsync(tenant, distinctRefs, cancellationToken).ConfigureAwait(false);
        foreach (var (conceptRef, view) in byRef)
        {
            resolved[conceptRef.ConceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
        }

        var unresolvedIds = distinctRefs
            .Select(r => r.ConceptId)
            .Where(id => !resolved.ContainsKey(id))
            .Distinct()
            .ToList();
        if (unresolvedIds.Count > 0)
        {
            var currentById = await concepts.GetCurrentManyAsync(tenant, unresolvedIds, cancellationToken).ConfigureAwait(false);
            foreach (var (conceptId, view) in currentById)
            {
                resolved[conceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
            }
        }

        return resolved;
    }
}

/// <summary>
/// Classifies whether clinical observations, collection specifications, collection items, rule variables, or validation
/// findings represent blinded treatment allocation, investigational intervention, or masked dispensation under K-AUTH-4.
/// </summary>
public interface IBlindedTreatmentClassifier
{
    /// <summary>
    /// True when an observation records blinded treatment allocation, investigational intervention, or masked
    /// dispensation according to its value, concept metadata, governing collection item, or protocol designs.
    /// </summary>
    bool IsBlindedObservation(
        ConceptId conceptId,
        ObservationValue? value,
        BlindedConceptMetadata? concept,
        string? collectionSpecId,
        string? itemId,
        IReadOnlyList<ProtocolDesign> designs);

    /// <summary>
    /// True when a collection specification's identifier, title, or stated operational need denotes blinded treatment
    /// allocation or dispensation.
    /// </summary>
    bool IsBlindedCollectionSpec(
        string? collectionSpecId,
        string? collectionSpecName = null,
        string? operationalNeed = null,
        IReadOnlyList<ProtocolDesign>? designs = null);

    /// <summary>
    /// True when a collection item (or its governing collection specification, concept, or current value) represents
    /// blinded treatment allocation or dispensation.
    /// </summary>
    bool IsBlindedCollectionItem(
        string? collectionSpecId,
        string? collectionTitle,
        CollectionItem? item,
        ObservationValue? value = null,
        BlindedConceptMetadata? concept = null,
        IReadOnlyList<ProtocolDesign>? designs = null);

    /// <summary>
    /// True when a validation rule variable (<c>item</c> or <c>spec.item</c>) on <paramref name="defaultDataset"/>
    /// references a blinded treatment field, concept, or intervention.
    /// </summary>
    bool IsBlindedVariable(
        string? defaultDataset,
        string? variable,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null);

    /// <summary>
    /// True when a validation finding targets or observes any blinded treatment dataset, item, concept, or intervention.
    /// </summary>
    bool IsBlindedFinding(
        FindingTarget target,
        IReadOnlyList<ObservedVariable> observedValues,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null);
}

/// <summary>
/// Default stateless implementation of <see cref="IBlindedTreatmentClassifier"/> shared across authorization,
/// persistence, exports, eCRF form projections, and rule-preview evaluation (K-AUTH-4).
/// </summary>
public sealed class BlindedTreatmentClassifier : IBlindedTreatmentClassifier
{
    private static readonly string[] TreatmentMarkers =
    [
        "treatment_assignment",
        "treatment assignment",
        "treatment_group",
        "treatment group",
        "treatment_allocation",
        "treatment allocation",
        "treatment_arm",
        "treatment arm",
        "rand_arm",
        "randomization",
        "randomisation",
        "randomized_to",
        "assigned_intervention",
        "assigned intervention",
        "investigational_product",
        "investigational product",
        "kit_number",
        "kit number",
        "dispensation",
        "unblind",
        "blinded_treatment",
        "blinded treatment",
        "study_drug",
        "study drug",
    ];

    private static readonly SearchValues<string> TreatmentMarkerSearchValues =
        SearchValues.Create(TreatmentMarkers, StringComparison.OrdinalIgnoreCase);

    /// <summary>Shared singleton instance.</summary>
    public static BlindedTreatmentClassifier Instance { get; } = new();

    /// <inheritdoc />
    public bool IsBlindedObservation(
        ConceptId conceptId,
        ObservationValue? value,
        BlindedConceptMetadata? concept,
        string? collectionSpecId,
        string? itemId,
        IReadOnlyList<ProtocolDesign> designs)
    {
        ArgumentNullException.ThrowIfNull(designs);

        if (value is MissingValue { Reason: NullFlavor.Masked }
            || MatchesMarkers(collectionSpecId)
            || MatchesMarkers(itemId)
            || MatchesConceptMarkers(concept))
        {
            return true;
        }

        for (var d = 0; d < designs.Count; d++)
        {
            if (DesignMatchesObservation(designs[d], conceptId, collectionSpecId, itemId))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsBlindedCollectionSpec(
        string? collectionSpecId,
        string? collectionSpecName = null,
        string? operationalNeed = null,
        IReadOnlyList<ProtocolDesign>? designs = null)
    {
        if (MatchesMarkers(collectionSpecId) || MatchesMarkers(collectionSpecName) || MatchesMarkers(operationalNeed))
        {
            return true;
        }

        if (designs is null || string.IsNullOrWhiteSpace(collectionSpecId))
        {
            return false;
        }

        for (var d = 0; d < designs.Count; d++)
        {
            var specs = designs[d].CollectionSpecs;
            for (var s = 0; s < specs.Count; s++)
            {
                var spec = specs[s];
                if (string.Equals(spec.Id, collectionSpecId, StringComparison.Ordinal) && MatchesSpecMarkers(spec))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsBlindedCollectionItem(
        string? collectionSpecId,
        string? collectionTitle,
        CollectionItem? item,
        ObservationValue? value = null,
        BlindedConceptMetadata? concept = null,
        IReadOnlyList<ProtocolDesign>? designs = null)
    {
        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (IsBlindedCollectionSpec(collectionSpecId, collectionTitle, designs: designs))
        {
            return true;
        }

        if (item is not null && MatchesItemMarkers(item))
        {
            return true;
        }

        if (item is not null && designs is not null)
        {
            return IsBlindedObservation(item.Concept.ConceptId, value, concept, collectionSpecId, item.Id, designs);
        }

        return MatchesConceptMarkers(concept);
    }

    /// <inheritdoc />
    public bool IsBlindedVariable(
        string? defaultDataset,
        string? variable,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null)
    {
        ArgumentNullException.ThrowIfNull(designs);

        if (MatchesMarkers(defaultDataset) || MatchesMarkers(variable))
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
            if (MatchesMarkers(targetSpec) || MatchesMarkers(targetItem))
            {
                return true;
            }
        }

        for (var d = 0; d < designs.Count; d++)
        {
            if (DesignMatchesVariable(designs[d], targetSpec, targetItem, resolveConcept))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsBlindedFinding(
        FindingTarget target,
        IReadOnlyList<ObservedVariable> observedValues,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observedValues);
        ArgumentNullException.ThrowIfNull(designs);

        if (IsBlindedCollectionSpec(target.Dataset, designs: designs))
        {
            return true;
        }

        if (IsBlindedVariable(target.Dataset, target.Variable, designs, resolveConcept))
        {
            return true;
        }

        for (var i = 0; i < observedValues.Count; i++)
        {
            if (IsBlindedVariable(target.Dataset, observedValues[i].Variable, designs, resolveConcept))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="text"/> contains any configured treatment-blinding marker.</summary>
    public static bool MatchesMarkers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.AsSpan().ContainsAny(TreatmentMarkerSearchValues);
    }

    private static bool DesignMatchesObservation(
        ProtocolDesign design,
        ConceptId conceptId,
        string? collectionSpecId,
        string? itemId)
    {
        if (HasInterventionProduct(design.Interventions, conceptId))
        {
            return true;
        }

        if (TryAsSpan(design.CollectionSpecs, out var specSpan))
        {
            for (var s = 0; s < specSpan.Length; s++)
            {
                var spec = specSpan[s];
                if (collectionSpecId is not null && !string.Equals(spec.Id, collectionSpecId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (SpecMatchesObservation(spec, conceptId, collectionSpecId, itemId))
                {
                    return true;
                }
            }

            return false;
        }

        var specs = design.CollectionSpecs;
        for (var s = 0; s < specs.Count; s++)
        {
            var spec = specs[s];
            if (collectionSpecId is not null && !string.Equals(spec.Id, collectionSpecId, StringComparison.Ordinal))
            {
                continue;
            }

            if (SpecMatchesObservation(spec, conceptId, collectionSpecId, itemId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SpecMatchesObservation(
        CollectionSpec spec,
        ConceptId conceptId,
        string? collectionSpecId,
        string? itemId)
    {
        if (collectionSpecId is not null && MatchesSpecMarkers(spec))
        {
            return true;
        }

        var specHasConcept = false;
        if (TryAsSpan(spec.Items, out var itemSpan))
        {
            for (var i = 0; i < itemSpan.Length; i++)
            {
                var item = itemSpan[i];
                var matchesConcept = item.Concept.ConceptId == conceptId;
                if (matchesConcept)
                {
                    specHasConcept = true;
                }

                var sameItem = itemId is not null
                    ? string.Equals(item.Id, itemId, StringComparison.Ordinal)
                    : matchesConcept;
                if (sameItem && MatchesItemMarkers(item))
                {
                    return true;
                }
            }

            return collectionSpecId is null && specHasConcept && MatchesSpecMarkers(spec);
        }

        var items = spec.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var matchesConcept = item.Concept.ConceptId == conceptId;
            if (matchesConcept)
            {
                specHasConcept = true;
            }

            var sameItem = itemId is not null
                ? string.Equals(item.Id, itemId, StringComparison.Ordinal)
                : matchesConcept;
            if (sameItem && MatchesItemMarkers(item))
            {
                return true;
            }
        }

        return collectionSpecId is null && specHasConcept && MatchesSpecMarkers(spec);
    }

    private static bool TryAsSpan<T>(IReadOnlyList<T> source, out ReadOnlySpan<T> span)
    {
        if (source is T[] array)
        {
            span = array;
            return true;
        }

        if (source is List<T> list)
        {
            span = CollectionsMarshal.AsSpan(list);
            return true;
        }

        span = default;
        return false;
    }

    private static bool DesignMatchesVariable(
        ProtocolDesign design,
        string? targetSpec,
        string targetItem,
        Func<ConceptId, BlindedConceptMetadata?>? resolveConcept)
    {
        var specs = design.CollectionSpecs;
        for (var s = 0; s < specs.Count; s++)
        {
            var spec = specs[s];
            if (!string.IsNullOrWhiteSpace(targetSpec)
                && !string.Equals(spec.Id, targetSpec, StringComparison.Ordinal))
            {
                continue;
            }

            var matchedItem = FindItemById(spec.Items, targetItem);
            if (matchedItem is null)
            {
                continue;
            }

            if (MatchesSpecMarkers(spec)
                || MatchesItemMarkers(matchedItem)
                || HasInterventionProduct(design.Interventions, matchedItem.Concept.ConceptId))
            {
                return true;
            }

            if (resolveConcept is not null && MatchesConceptMarkers(resolveConcept(matchedItem.Concept.ConceptId)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInterventionProduct(IReadOnlyList<Intervention> interventions, ConceptId conceptId)
    {
        for (var i = 0; i < interventions.Count; i++)
        {
            if (interventions[i].Product?.ConceptId == conceptId)
            {
                return true;
            }
        }

        return false;
    }

    private static CollectionItem? FindItemById(IReadOnlyList<CollectionItem> items, string itemId)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.Equals(item.Id, itemId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static bool MatchesSpecMarkers(CollectionSpec spec) =>
        MatchesMarkers(spec.Id)
        || MatchesMarkers(spec.Name)
        || MatchesMarkers(spec.Purpose?.OperationalNeed);

    private static bool MatchesItemMarkers(CollectionItem item) =>
        MatchesMarkers(item.Id)
        || MatchesMarkers(item.Purpose?.OperationalNeed)
        || MatchesMarkers(item.Prompt);

    private static bool MatchesConceptMarkers(BlindedConceptMetadata? concept) =>
        concept is { } c
        && (MatchesMarkers(c.ExternalCode)
            || MatchesMarkers(c.PreferredLabel)
            || MatchesMarkers(c.Definition));
}

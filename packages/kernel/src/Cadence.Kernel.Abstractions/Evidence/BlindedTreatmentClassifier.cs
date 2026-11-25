using System;
using System.Collections.Generic;
using System.Linq;
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

        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (MatchesMarkers(collectionSpecId) || MatchesMarkers(itemId))
        {
            return true;
        }

        foreach (var design in designs)
        {
            if (design.Interventions.Any(i => i.Product?.ConceptId == conceptId))
            {
                return true;
            }

            foreach (var spec in design.CollectionSpecs)
            {
                if (collectionSpecId is not null && !string.Equals(spec.Id, collectionSpecId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (MatchesSpecMarkers(spec)
                    && (collectionSpecId is not null || spec.Items.Any(i => i.Concept.ConceptId == conceptId)))
                {
                    return true;
                }

                foreach (var item in spec.Items)
                {
                    var sameItem = itemId is not null
                        ? string.Equals(item.Id, itemId, StringComparison.Ordinal)
                        : item.Concept.ConceptId == conceptId;
                    if (sameItem && MatchesItemMarkers(item))
                    {
                        return true;
                    }
                }
            }
        }

        return MatchesConceptMarkers(concept);
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

        return designs.Any(design =>
            design.CollectionSpecs.Any(spec =>
                string.Equals(spec.Id, collectionSpecId, StringComparison.Ordinal) && MatchesSpecMarkers(spec)));
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

        foreach (var design in designs)
        {
            foreach (var spec in design.CollectionSpecs)
            {
                if (!string.IsNullOrWhiteSpace(targetSpec)
                    && !string.Equals(spec.Id, targetSpec, StringComparison.Ordinal))
                {
                    continue;
                }

                var matchedItem = spec.Items.FirstOrDefault(i => string.Equals(i.Id, targetItem, StringComparison.Ordinal));
                if (matchedItem is null)
                {
                    continue;
                }

                if (MatchesSpecMarkers(spec)
                    || MatchesItemMarkers(matchedItem)
                    || design.Interventions.Any(i => i.Product?.ConceptId == matchedItem.Concept.ConceptId))
                {
                    return true;
                }

                if (resolveConcept is not null && MatchesConceptMarkers(resolveConcept(matchedItem.Concept.ConceptId)))
                {
                    return true;
                }
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

        return observedValues.Any(observed => IsBlindedVariable(target.Dataset, observed.Variable, designs, resolveConcept));
    }

    /// <summary>True when <paramref name="text"/> contains any configured treatment-blinding marker.</summary>
    public static bool MatchesMarkers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Array.Exists(TreatmentMarkers, marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
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

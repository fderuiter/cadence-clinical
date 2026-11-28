using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Rules.Expressions;

namespace Cadence.Rules.Evaluation;

internal readonly record struct StructuralRuleItemKey(
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? CollectionTitle,
    string ItemId,
    ConceptId ConceptId,
    string? ItemPrompt,
    string? ItemOperationalNeed);

internal sealed class CollectionBlindingClassifier(
    IBlindedTreatmentClassifier blinding,
    IProtocolVersionReader? protocols,
    IConceptRegistry? concepts,
    INodeVisibility? visibility)
{
    internal IndexedCollectionForm[] IndexStudyForms(
        IReadOnlyList<CollectionInstanceRecord> instances,
        Func<EnrollmentId, IReadOnlyList<CollectionInstanceRecord>, bool>? isEnrollmentUnblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks,
        Dictionary<StructuralRuleItemKey, bool>? structuralItemCache = null)
    {
        var byEnrollment = new Dictionary<EnrollmentId, List<CollectionInstanceRecord>>();
        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (!byEnrollment.TryGetValue(instance.EnrollmentId, out var group))
            {
                group = [];
                byEnrollment.Add(instance.EnrollmentId, group);
            }

            group.Add(instance);
        }

        var itemCache = structuralItemCache ?? new Dictionary<StructuralRuleItemKey, bool>();
        var ownValuesByFormId = new Dictionary<CollectionInstanceId, Dictionary<string, RuleValue>>(instances.Count);
        var siblingIndexByEnrollment = new Dictionary<EnrollmentId, EnrollmentSiblingIndex>(byEnrollment.Count);
        var unblindedByEnrollment = new Dictionary<EnrollmentId, bool>(byEnrollment.Count);

        foreach (var (enrollmentId, enrollmentForms) in byEnrollment)
        {
            var enrollmentUnblinded = isEnrollmentUnblinded is null || isEnrollmentUnblinded(enrollmentId, enrollmentForms);
            unblindedByEnrollment.Add(enrollmentId, enrollmentUnblinded);

            for (var i = 0; i < enrollmentForms.Count; i++)
            {
                var form = enrollmentForms[i];
                if (!ownValuesByFormId.ContainsKey(form.CollectionInstanceId))
                {
                    ownValuesByFormId.Add(
                        form.CollectionInstanceId,
                        BuildServiceOwnItemValues(form, draftOverlay: null, enrollmentUnblinded, designs, conceptMap, masks, itemCache));
                }
            }

            var latestBySpec = CollectionRuleRecordBuilder.SelectLatestFormsBySpec(
                enrollmentForms,
                static f => f.CollectionInstanceId,
                static f => f.CollectionSpecId,
                static f => f.OpenedAt);
            siblingIndexByEnrollment.Add(
                enrollmentId,
                CollectionRuleRecordBuilder.BuildSiblingIndex(
                    latestBySpec,
                    static sibling => sibling.CollectionSpecId,
                    sibling => ownValuesByFormId[sibling.CollectionInstanceId]));
        }

        var indexedForms = new IndexedCollectionForm[instances.Count];
        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            indexedForms[i] = new IndexedCollectionForm(
                instance,
                ownValuesByFormId[instance.CollectionInstanceId],
                siblingIndexByEnrollment[instance.EnrollmentId],
                unblindedByEnrollment[instance.EnrollmentId]);
        }

        return indexedForms;
    }

    internal Dictionary<string, RuleValue> BuildServiceOwnItemValues(
        CollectionInstanceRecord form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        bool unblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks,
        Dictionary<StructuralRuleItemKey, bool>? structuralItemCache = null)
    {
        var recordedByItem = new Dictionary<string, (ObservationValue? Value, ObservationId? ObservationId)>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = (recorded.Value, recorded.ObservationId);
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem.TryGetValue(itemId, out var existing);
                recordedByItem[itemId] = (draftValue, existing.ObservationId);
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var specItem = form.Items[i];
            if (ownValues.ContainsKey(specItem.Id))
            {
                continue;
            }

            recordedByItem.TryGetValue(specItem.Id, out var entry);
            var itemBlindedIds = draftOverlay is not null && draftOverlay.ContainsKey(specItem.Id) ? null : form.BlindedItemIds;
            var converted = !unblinded && IsBlindedItemFast(form.ProtocolVersionId, form.CollectionSpecId, form.CollectionTitle, specItem.Id, specItem, entry.Value, entry.ObservationId, designs, conceptMap, masks, itemBlindedIds, structuralItemCache)
                ? RuleValue.Null
                : CollectionValidationService.ToRuleValue(entry.Value);
            ownValues.Add(specItem.Id, converted);
        }

        foreach (var (itemId, entry) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            var itemBlindedIds = draftOverlay is not null && draftOverlay.ContainsKey(itemId) ? null : form.BlindedItemIds;
            var converted = !unblinded && IsBlindedItemFast(form.ProtocolVersionId, form.CollectionSpecId, form.CollectionTitle, itemId, specItem: null, entry.Value, entry.ObservationId, designs, conceptMap, masks, itemBlindedIds, structuralItemCache)
                ? RuleValue.Null
                : CollectionValidationService.ToRuleValue(entry.Value);
            ownValues.Add(itemId, converted);
        }

        return ownValues;
    }

    internal Dictionary<string, RuleValue> BuildServiceOwnItemValues(
        CollectionRuleInput form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        bool unblinded,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        var recordedByItem = new Dictionary<string, ObservationValue?>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = recorded.Value;
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var specItem = form.Items[i];
            if (ownValues.ContainsKey(specItem.Id))
            {
                continue;
            }

            recordedByItem.TryGetValue(specItem.Id, out var raw);
            var converted = !unblinded && IsBlindedItemFast(form.ProtocolVersionId, form.CollectionSpecId, form.CollectionTitle, specItem.Id, specItem, raw, observationId: null, designs, conceptMap, masks: null)
                ? RuleValue.Null
                : CollectionValidationService.ToRuleValue(raw);
            ownValues.Add(specItem.Id, converted);
        }

        foreach (var (itemId, raw) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            var converted = !unblinded && IsBlindedItemFast(form.ProtocolVersionId, form.CollectionSpecId, form.CollectionTitle, itemId, specItem: null, raw, observationId: null, designs, conceptMap, masks: null)
                ? RuleValue.Null
                : CollectionValidationService.ToRuleValue(raw);
            ownValues.Add(itemId, converted);
        }

        return ownValues;
    }

    internal bool IsEnrollmentUnblinded(
        EnrollmentId enrollmentId,
        IReadOnlyList<CollectionInstanceRecord> enrollmentForms,
        Actor? caller,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments,
        IReadOnlyDictionary<NodeRef, MaskReason> masks,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        Dictionary<StructuralRuleItemKey, bool>? structuralItemCache = null)
    {
        if (caller is null)
        {
            return true;
        }

        var hasMaskedBlindedObservation = enrollmentForms
            .SelectMany(f => f.Values)
            .Any(v => masks.ContainsKey(NodeRef.For(v.ObservationId)));
        if (hasMaskedBlindedObservation)
        {
            return false;
        }

        if (unblindedEnrollments is not null)
        {
            return unblindedEnrollments.Contains(enrollmentId);
        }

        if (visibility is null)
        {
            return false;
        }

        return enrollmentForms.Any(form =>
            form.Values.Any(v => IsBlindedFormVariable(form, v.ItemId, v.Value, designs, conceptMap, masks: null, structuralItemCache)));
    }

    internal bool IsBlindedViolation(
        string dataset,
        ReadOnlySpan<RuleObservedValue> observed,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap)
    {
        if (blinding.IsBlindedCollectionSpec(dataset, designs: designs))
        {
            return true;
        }

        BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var meta) ? meta : null;
        for (var i = 0; i < observed.Length; i++)
        {
            if (blinding.IsBlindedVariable(dataset, observed[i].Variable, designs, ResolveConcept))
            {
                return true;
            }
        }

        return false;
    }

    internal async Task<IReadOnlyList<ProtocolDesign>> LoadDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        IEnumerable<ProtocolVersionId> versionIds,
        CancellationToken cancellationToken)
    {
        if (protocols is null)
        {
            return [];
        }

        var distinctVersionIds = versionIds.Distinct().ToList();
        var studyDesigns = await protocols.GetDesignsForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (studyDesigns.Count > 0)
        {
            var formDesigns = new List<ProtocolDesign>();
            foreach (var versionId in distinctVersionIds)
            {
                if (studyDesigns.TryGetValue(versionId, out var design))
                {
                    formDesigns.Add(design);
                }
            }

            return formDesigns.Count > 0 ? formDesigns : [.. studyDesigns.Values];
        }

        var fallbackDesigns = new List<ProtocolDesign>();
        foreach (var versionId in distinctVersionIds.Where(id => !id.IsEmpty))
        {
            if (await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                fallbackDesigns.Add(design);
            }
        }

        return fallbackDesigns;
    }

    internal Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadConceptsAsync(
        TenantId tenant,
        IEnumerable<CollectionItem> items,
        IReadOnlyList<ProtocolDesign> designs,
        CancellationToken cancellationToken) =>
        BlindedConceptMetadata.ResolveManyAsync(
            concepts,
            tenant,
            items.Select(i => i.Concept)
                .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept)),
            cancellationToken);

    private bool IsBlindedFormVariable(
        CollectionInstanceRecord form,
        string itemId,
        ObservationValue? rawValue,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks = null,
        Dictionary<StructuralRuleItemKey, bool>? structuralItemCache = null)
    {
        if (ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance)
            && form.BlindedItemIds is not null
            && form.BlindedItemIds.Contains(itemId))
        {
            return true;
        }

        ObservationId? observationId = null;
        if (masks is not null)
        {
            var recorded = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal));
            observationId = recorded?.ObservationId;
        }

        var specItem = form.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        return IsBlindedItemFast(form.ProtocolVersionId, form.CollectionSpecId, form.CollectionTitle, itemId, specItem, rawValue, observationId, designs, conceptMap, masks, form.BlindedItemIds, structuralItemCache);
    }

    private bool IsBlindedItemFast(
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? collectionTitle,
        string itemId,
        CollectionItem? specItem,
        ObservationValue? rawValue,
        ObservationId? observationId,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptMap,
        IReadOnlyDictionary<NodeRef, MaskReason>? masks,
        IReadOnlySet<string>? blindedItemIds = null,
        Dictionary<StructuralRuleItemKey, bool>? structuralItemCache = null)
    {
        if (masks is not null && observationId is { } obsId && masks.ContainsKey(NodeRef.For(obsId)))
        {
            return true;
        }

        var isDefaultClassifier = ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance);
        if (isDefaultClassifier && blindedItemIds is not null && blindedItemIds.Contains(itemId))
        {
            return true;
        }

        if (rawValue is MissingValue { Reason: NullFlavor.Masked } && isDefaultClassifier)
        {
            return true;
        }

        BlindedConceptMetadata? conceptMeta = specItem is not null && conceptMap.TryGetValue(specItem.Concept.ConceptId, out var meta)
            ? meta
            : null;
        var canUseStructuralCache = structuralItemCache is not null
            && (isDefaultClassifier || rawValue is null);
        StructuralRuleItemKey cacheKey = default;
        if (canUseStructuralCache)
        {
            cacheKey = new StructuralRuleItemKey(
                protocolVersionId,
                collectionSpecId,
                collectionTitle,
                itemId,
                specItem?.Concept.ConceptId ?? ConceptId.Empty,
                specItem?.Prompt,
                specItem?.Purpose?.OperationalNeed);
            if (structuralItemCache!.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var classificationValue = canUseStructuralCache ? null : rawValue;
        bool isBlinded;
        if (blinding.IsBlindedCollectionItem(collectionSpecId, collectionTitle, specItem, classificationValue, conceptMeta, designs))
        {
            isBlinded = true;
        }
        else
        {
            BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMap.TryGetValue(id, out var m) ? m : null;
            isBlinded = blinding.IsBlindedVariable(collectionSpecId, itemId, designs, ResolveConcept);
        }

        if (canUseStructuralCache)
        {
            structuralItemCache![cacheKey] = isBlinded;
        }

        return isBlinded;
    }
}

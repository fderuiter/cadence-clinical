using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Rules.Packs;

namespace Cadence.Ddf;

internal static class ProtocolDesignMerger
{
    public static ProtocolDesignImportResult MergeUsdmDesignOntoBase(
        ProtocolDesign baseDesign,
        ProtocolDesign incomingDesign,
        IReadOnlyList<CollectionSpec> incomingSpecs,
        IReadOnlyList<ProtocolImportNote> incomingNotes,
        string fallbackTitle)
    {
        var mergedObjectives = MergeElementsById(baseDesign.Objectives, incomingDesign.Objectives);
        var mergedEndpoints = MergeElementsById(baseDesign.Endpoints, incomingDesign.Endpoints);
        var mergedEstimands = MergeElementsById(baseDesign.Estimands, incomingDesign.Estimands);
        var mergedPopulations = MergeElementsById(baseDesign.Populations, incomingDesign.Populations);
        var mergedCriteria = MergeElementsById(baseDesign.EligibilityCriteria, incomingDesign.EligibilityCriteria);
        var mergedArms = MergeElementsById(baseDesign.Arms, incomingDesign.Arms);
        var mergedCohorts = MergeElementsById(baseDesign.Cohorts, incomingDesign.Cohorts);
        var mergedInterventions = MergeElementsById(baseDesign.Interventions, incomingDesign.Interventions);
        var mergedSafetyRules = MergeElementsById(baseDesign.SafetyRules, incomingDesign.SafetyRules);
        var mergedDiscontinuationRules = MergeElementsById(baseDesign.DiscontinuationRules, incomingDesign.DiscontinuationRules);
        var mergedRandomization = incomingDesign.RandomizationScheme ?? baseDesign.RandomizationScheme;
        var mergedStatistical = incomingDesign.StatisticalDesign ?? baseDesign.StatisticalDesign;

        var preScheduleDesign = new ProtocolDesign
        {
            Title = FirstNonBlank(baseDesign.Title, fallbackTitle, "Protocol Draft")!,
            Objectives = mergedObjectives,
            Endpoints = mergedEndpoints,
            Estimands = mergedEstimands,
            Populations = mergedPopulations,
            EligibilityCriteria = mergedCriteria,
            Arms = mergedArms,
            Epochs = baseDesign.Epochs,
            Cohorts = mergedCohorts,
            Interventions = mergedInterventions,
            PlannedActivities = baseDesign.PlannedActivities,
            CollectionSpecs = baseDesign.CollectionSpecs,
            SafetyRules = mergedSafetyRules,
            DiscontinuationRules = mergedDiscontinuationRules,
            RandomizationScheme = mergedRandomization,
            StatisticalDesign = mergedStatistical,
            ImportNotes = baseDesign.ImportNotes,
        };

        return FinalizeMergedFormsDesign(
            preScheduleDesign,
            incomingSpecs,
            incomingNotes,
            fallbackTitle,
            replaceForms: false,
            incomingEpochs: incomingDesign.Epochs,
            incomingActivities: incomingDesign.PlannedActivities);
    }

    public static ProtocolDesignImportResult FinalizeMergedFormsDesign(
        ProtocolDesign? baseDesign,
        IReadOnlyList<CollectionSpec> incomingSpecs,
        IReadOnlyList<ProtocolImportNote> incomingNotes,
        string fallbackTitle,
        IReadOnlyList<Rule>? incomingRules = null,
        string? studyOid = null,
        bool replaceForms = false,
        IReadOnlyList<Epoch>? incomingEpochs = null,
        IReadOnlyList<PlannedActivity>? incomingActivities = null)
    {
        var idRenameMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var extraNotes = new List<ProtocolImportNote>();
        List<Epoch> finalEpochs;
        List<PlannedActivity> preRemapActivities;
        var incomingActivityIds = new HashSet<string>(StringComparer.Ordinal);

        if (replaceForms && baseDesign is not null)
        {
            finalEpochs = [.. baseDesign.Epochs];
            preRemapActivities = [.. baseDesign.PlannedActivities];
        }
        else
        {
            var nonScheduleIds = NonScheduleAndNonCollectionElementIds(baseDesign);
            finalEpochs = MergeEpochs(baseDesign?.Epochs ?? [], incomingEpochs ?? [], nonScheduleIds, out var epochIdMap);
            foreach (var epoch in finalEpochs)
            {
                nonScheduleIds.Add(epoch.Id);
            }

            preRemapActivities = MergePlannedActivities(
                baseDesign?.PlannedActivities ?? [],
                incomingActivities ?? [],
                epochIdMap,
                nonScheduleIds,
                incomingActivityIds);
        }

        var usedIds = NonScheduleAndNonCollectionElementIds(baseDesign);
        foreach (var epoch in finalEpochs)
        {
            usedIds.Add(epoch.Id);
        }

        foreach (var activity in preRemapActivities)
        {
            usedIds.Add(activity.Id);
        }

        var mergedSpecs = replaceForms
            ? AllocateReplacementSpecs(incomingSpecs, usedIds, idRenameMap)
            : MergeCollectionSpecsById(baseDesign?.CollectionSpecs ?? [], incomingSpecs, usedIds, idRenameMap);

        var validSpecIds = mergedSpecs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var finalActivities = new List<PlannedActivity>(preRemapActivities.Count);
        foreach (var activity in preRemapActivities)
        {
            var remappedSpecIds = incomingActivityIds.Contains(activity.Id)
                ? activity.CollectionSpecIds
                    .Select(id => idRenameMap.GetValueOrDefault(id, id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
                : activity.CollectionSpecIds;

            if (replaceForms && baseDesign is not null)
            {
                var removedIds = remappedSpecIds.Where(id => !validSpecIds.Contains(id)).ToList();
                if (removedIds.Count > 0)
                {
                    var keptIds = remappedSpecIds.Where(validSpecIds.Contains).ToList();
                    finalActivities.Add(activity with { CollectionSpecIds = keptIds });
                    extraNotes.Add(new ProtocolImportNote(
                        activity.Id,
                        $"Pruned removed form reference(s) [{string.Join(", ", removedIds)}] from planned activity '{activity.Name}' ({activity.Id}) after replacing draft forms."));
                    continue;
                }
            }

            finalActivities.Add(ReferenceEquals(remappedSpecIds, activity.CollectionSpecIds)
                ? activity
                : activity with { CollectionSpecIds = remappedSpecIds });
        }

        var mergedNotes = DeduplicateNotes((baseDesign?.ImportNotes ?? []).Concat(incomingNotes).Concat(extraNotes));
        var title = FirstNonBlank(baseDesign?.Title, fallbackTitle, "Protocol Draft")!;
        var candidateDesign = (baseDesign ?? new ProtocolDesign()) with
        {
            Title = title,
            Epochs = finalEpochs,
            PlannedActivities = finalActivities,
            CollectionSpecs = mergedSpecs,
            ImportNotes = mergedNotes.Count > 0 ? mergedNotes : null,
        };

        var check = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(candidateDesign));
        if (!check.IsValid)
        {
            return new ProtocolDesignImportResult(null, check.Problems, mergedNotes);
        }

        var (importedPack, ruleProblems) = ProtocolRulePackMerger.CompileImportedRulePack(
            incomingRules,
            idRenameMap,
            title,
            studyOid);
        if (ruleProblems.Count > 0)
        {
            return new ProtocolDesignImportResult(null, ruleProblems, mergedNotes);
        }

        return new ProtocolDesignImportResult(check.Design, [], mergedNotes, RulePack: importedPack);
    }

    public static HashSet<string> NonCollectionElementIds(ProtocolDesign? design)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (design is null)
        {
            return ids;
        }

        foreach (var entry in design.Elements().Where(e => e.Kind is not (ProtocolElementKind.CollectionSpec or ProtocolElementKind.CollectionItem)))
        {
            ids.Add(entry.Id);
        }

        return ids;
    }

    public static HashSet<string> NonScheduleAndNonCollectionElementIds(ProtocolDesign? design)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (design is null)
        {
            return ids;
        }

        foreach (var entry in design.Elements().Where(e => e.Kind is not (
            ProtocolElementKind.CollectionSpec
            or ProtocolElementKind.CollectionItem
            or ProtocolElementKind.Epoch
            or ProtocolElementKind.PlannedActivity)))
        {
            ids.Add(entry.Id);
        }

        return ids;
    }

    public static List<ProtocolImportNote> DeduplicateNotes(IEnumerable<ProtocolImportNote> notes) =>
        [.. notes.Distinct()];

    public static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private static List<CollectionSpec> AllocateReplacementSpecs(
        IReadOnlyList<CollectionSpec> incomingSpecs,
        HashSet<string> usedIds,
        Dictionary<string, string> idRenameMap)
    {
        var specs = new List<CollectionSpec>(incomingSpecs.Count);
        foreach (var spec in incomingSpecs)
        {
            specs.Add(AllocateSpecAndItems(spec, priorSpec: null, usedIds, idRenameMap));
        }

        return specs;
    }

    private static List<CollectionSpec> MergeCollectionSpecsById(
        IReadOnlyList<CollectionSpec> baseSpecs,
        IReadOnlyList<CollectionSpec> incomingSpecs,
        HashSet<string> usedIds,
        Dictionary<string, string> idRenameMap)
    {
        var incomingIds = incomingSpecs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var matchedExistingById = new Dictionary<string, CollectionSpec>(StringComparer.Ordinal);

        foreach (var existing in baseSpecs)
        {
            if (incomingIds.Contains(existing.Id))
            {
                matchedExistingById[existing.Id] = existing;
                continue;
            }

            usedIds.Add(existing.Id);
            foreach (var item in existing.Items)
            {
                usedIds.Add(item.Id);
            }
        }

        var updatedByExistingId = new Dictionary<string, CollectionSpec>(StringComparer.Ordinal);
        var appendedSpecs = new List<CollectionSpec>();

        foreach (var spec in incomingSpecs)
        {
            var updatesExisting = matchedExistingById.TryGetValue(spec.Id, out var priorSpec)
                && !updatedByExistingId.ContainsKey(spec.Id);
            var allocated = AllocateSpecAndItems(spec, updatesExisting ? priorSpec : null, usedIds, idRenameMap);
            if (updatesExisting && string.Equals(allocated.Id, spec.Id, StringComparison.Ordinal))
            {
                updatedByExistingId[spec.Id] = allocated;
            }
            else
            {
                appendedSpecs.Add(allocated);
            }
        }

        var merged = new List<CollectionSpec>(baseSpecs.Count + appendedSpecs.Count);
        foreach (var existing in baseSpecs)
        {
            merged.Add(updatedByExistingId.GetValueOrDefault(existing.Id, existing));
        }

        merged.AddRange(appendedSpecs);
        return merged;
    }

    private static CollectionSpec AllocateSpecAndItems(
        CollectionSpec spec,
        CollectionSpec? priorSpec,
        HashSet<string> usedIds,
        Dictionary<string, string> idRenameMap)
    {
        var specId = OdmDesignImporter.AllocateUniqueElementId(spec.Id, "Form", usedIds);
        idRenameMap[spec.Id] = specId;
        var priorItemsById = priorSpec?.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var adjustedItems = spec.Items.Select(item =>
        {
            var candidate = usedIds.Contains(item.Id) && !item.Id.StartsWith($"{specId}.", StringComparison.Ordinal)
                ? $"{specId}.{item.Id}"
                : item.Id;
            var itemId = OdmDesignImporter.AllocateUniqueElementId(candidate, "Item", usedIds);
            idRenameMap[item.Id] = itemId;
            idRenameMap[$"{spec.Id}.{item.Id}"] = $"{specId}.{itemId}";
            var adjusted = itemId == item.Id ? item : item with { Id = itemId };
            if (adjusted.Purpose is null
                && priorItemsById is not null
                && priorItemsById.TryGetValue(itemId, out var priorItem)
                && priorItem.Purpose is not null)
            {
                adjusted = adjusted with { Purpose = priorItem.Purpose };
            }

            return adjusted;
        }).ToList();

        return spec with
        {
            Id = specId,
            Items = adjustedItems,
            Purpose = spec.Purpose ?? priorSpec?.Purpose,
        };
    }

    private static List<T> MergeElementsById<T>(IReadOnlyList<T> baseElements, IReadOnlyList<T> incomingElements)
        where T : IProtocolElement
    {
        if (incomingElements.Count == 0)
        {
            return [.. baseElements];
        }

        if (baseElements.Count == 0)
        {
            return [.. incomingElements];
        }

        var incomingById = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in incomingElements)
        {
            incomingById[item.Id] = item;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<T>(baseElements.Count + incomingElements.Count);
        foreach (var existing in baseElements)
        {
            seenIds.Add(existing.Id);
            merged.Add(incomingById.TryGetValue(existing.Id, out var replacement) ? replacement : existing);
        }

        foreach (var incoming in incomingElements)
        {
            if (seenIds.Add(incoming.Id))
            {
                merged.Add(incoming);
            }
        }

        return merged;
    }

    private static List<Epoch> MergeEpochs(
        IReadOnlyList<Epoch> baseEpochs,
        IReadOnlyList<Epoch> incomingEpochs,
        HashSet<string> reservedNonScheduleIds,
        out Dictionary<string, string> epochIdMap)
    {
        epochIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (incomingEpochs.Count == 0)
        {
            return [.. baseEpochs];
        }

        var usedEpochAndOtherIds = new HashSet<string>(reservedNonScheduleIds, StringComparer.Ordinal);
        var merged = new List<Epoch>(baseEpochs.Count + incomingEpochs.Count);
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var existing in baseEpochs)
        {
            usedEpochAndOtherIds.Add(existing.Id);
            indexById[existing.Id] = merged.Count;
            merged.Add(existing);
        }

        int nextOrder = merged.Count > 0 ? merged.Max(e => e.Order) + 1 : 1;
        foreach (var incoming in incomingEpochs)
        {
            if (indexById.TryGetValue(incoming.Id, out var existingIdx))
            {
                var prior = merged[existingIdx];
                epochIdMap[incoming.Id] = prior.Id;
                merged[existingIdx] = new Epoch(prior.Id, incoming.Name, prior.Order);
                continue;
            }

            var allocatedId = OdmDesignImporter.AllocateUniqueElementId(incoming.Id, "Epoch", usedEpochAndOtherIds);
            epochIdMap[incoming.Id] = allocatedId;
            var order = baseEpochs.Count == 0 ? incoming.Order : nextOrder++;
            indexById[allocatedId] = merged.Count;
            merged.Add(new Epoch(allocatedId, incoming.Name, order));
        }

        return merged;
    }

    private static List<PlannedActivity> MergePlannedActivities(
        IReadOnlyList<PlannedActivity> baseActivities,
        IReadOnlyList<PlannedActivity> incomingActivities,
        IReadOnlyDictionary<string, string> epochIdMap,
        HashSet<string> usedNonCollectionIds,
        HashSet<string> incomingActivityIds)
    {
        if (incomingActivities.Count == 0)
        {
            return [.. baseActivities];
        }

        var usedIds = new HashSet<string>(usedNonCollectionIds, StringComparer.Ordinal);
        var merged = new List<PlannedActivity>(baseActivities.Count + incomingActivities.Count);
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var existing in baseActivities)
        {
            usedIds.Add(existing.Id);
            indexById[existing.Id] = merged.Count;
            merged.Add(existing);
        }

        foreach (var incoming in incomingActivities)
        {
            var mappedEpochId = epochIdMap.GetValueOrDefault(incoming.EpochId, incoming.EpochId);
            if (indexById.TryGetValue(incoming.Id, out var existingIdx))
            {
                var prior = merged[existingIdx];
                incomingActivityIds.Add(prior.Id);
                merged[existingIdx] = incoming with
                {
                    Id = prior.Id,
                    EpochId = mappedEpochId,
                    Timing = incoming.Timing ?? prior.Timing,
                    Purpose = incoming.Purpose ?? prior.Purpose,
                    Repeating = incoming.Repeating || prior.Repeating,
                };
                continue;
            }

            var allocatedId = OdmDesignImporter.AllocateUniqueElementId(incoming.Id, "Activity", usedIds);
            incomingActivityIds.Add(allocatedId);
            indexById[allocatedId] = merged.Count;
            merged.Add(incoming with
            {
                Id = allocatedId,
                EpochId = mappedEpochId,
            });
        }

        return merged;
    }
}

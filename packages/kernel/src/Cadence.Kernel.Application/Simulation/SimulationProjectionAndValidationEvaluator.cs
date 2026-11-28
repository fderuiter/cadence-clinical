using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// Pure evaluator of dataset <see cref="ProjectionDatasetChange"/>s via <see cref="IMappingEngine"/> and edit-check
/// <see cref="ValidationDeltaSummary"/> via <see cref="IRuleEvaluator"/> (ADR-2480 §4.1, K-PROJ-2, K-RULE-4, K-FIND-1).
/// </summary>
internal static class SimulationProjectionAndValidationEvaluator
{
    public static readonly EngineVersion MappingEngineVersion = new("standards.mapping", "1");
    public static readonly EngineVersion RuleEvaluatorVersion = new("rules.evaluator", "1");

    private const int MaxSamplesPerDataset = 10;

    public static (
        IReadOnlyList<ProjectionDatasetChange> Changes,
        IReadOnlyList<RuleRecord> BaselineProjectedRecords,
        IReadOnlyList<RuleRecord> CandidateProjectedRecords,
        HashSet<EnrollmentId> ImpactedEnrollments) EvaluateProjections(
        IMappingEngine mappingEngine,
        MaskedSimulationSnapshot snapshot,
        IReadOnlyList<MappingSpecification> baselineSpecs,
        IReadOnlyList<MappingSpecification> candidateSpecs,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign)
    {
        var decoder = new SnapshotCodelistDecoder(snapshot.CodelistDecodes);
        if (CanShareProjections(baselineSpecs, candidateSpecs, baselineDesign, candidateDesign))
        {
            var sharedRecords = ProjectRuleRecordsSinglePass(
                mappingEngine,
                snapshot.VisibleObservations,
                baselineSpecs,
                baselineDesign,
                decoder);
            return ([], sharedRecords, sharedRecords, []);
        }

        var baselineProjected = ProjectObservations(mappingEngine, snapshot.VisibleObservations, baselineSpecs, baselineDesign, decoder);
        var candidateProjected = ProjectObservations(mappingEngine, snapshot.VisibleObservations, candidateSpecs, candidateDesign, decoder);

        var allKeys = baselineProjected.Keys.Union(candidateProjected.Keys).Order(StringComparer.Ordinal).ToList();
        var byDataset = new SortedDictionary<string, DatasetAccumulator>(StringComparer.Ordinal);
        var impactedEnrollments = new HashSet<EnrollmentId>();

        foreach (var key in allKeys)
        {
            var before = baselineProjected.GetValueOrDefault(key);
            var after = candidateProjected.GetValueOrDefault(key);
            if (before is null && after is not null)
            {
                GetDataset(byDataset, after.Dataset).RecordAdded(after.RecordKey, after.Variables);
                impactedEnrollments.Add(after.EnrollmentId);
            }
            else if (before is not null && after is null)
            {
                GetDataset(byDataset, before.Dataset).RecordRemoved(before.RecordKey, before.Variables);
                impactedEnrollments.Add(before.EnrollmentId);
            }
            else if (before is not null && after is not null)
            {
                if (!string.Equals(before.Dataset, after.Dataset, StringComparison.Ordinal))
                {
                    GetDataset(byDataset, before.Dataset).RecordRemoved(before.RecordKey, before.Variables);
                    GetDataset(byDataset, after.Dataset).RecordAdded(after.RecordKey, after.Variables);
                    impactedEnrollments.Add(after.EnrollmentId);
                }
                else if (!VariablesEqual(before.Variables, after.Variables))
                {
                    GetDataset(byDataset, after.Dataset).RecordChanged(after.RecordKey, before.Variables, after.Variables);
                    impactedEnrollments.Add(after.EnrollmentId);
                }
            }
        }

        var changes = byDataset
            .Where(kv => kv.Value.HasChanges)
            .Select(kv => kv.Value.ToSummary(kv.Key))
            .ToList();

        return (
            changes,
            [.. baselineProjected.Values.OrderBy(p => p.RecordKey, StringComparer.Ordinal).Select(ToRuleRecord)],
            [.. candidateProjected.Values.OrderBy(p => p.RecordKey, StringComparer.Ordinal).Select(ToRuleRecord)],
            impactedEnrollments);
    }

    public static (ValidationDeltaSummary Delta, HashSet<EnrollmentId> ImpactedEnrollments, bool MaskedBlindedFinding) EvaluateValidationDelta(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        MaskedSimulationSnapshot snapshot,
        DateOnly asOfDate,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RulePack> candidatePacks,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign,
        IReadOnlyList<RuleRecord> baselineProjectedRecords,
        IReadOnlyList<RuleRecord> candidateProjectedRecords) =>
        SimulationValidationDeltaEvaluator.EvaluateValidationDelta(
            evaluator,
            blinding,
            snapshot,
            asOfDate,
            baselinePacks,
            candidatePacks,
            baselineDesign,
            candidateDesign,
            baselineProjectedRecords,
            candidateProjectedRecords);

    private static IReadOnlyList<RuleRecord> ProjectRuleRecordsSinglePass(
        IMappingEngine engine,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<MappingSpecification> specifications,
        ProtocolDesign? designOverlay,
        ICodelistDecoder decoder)
    {
        if (specifications.Count == 0 || observations.Count == 0)
        {
            return [];
        }

        var winningSpecByConcept = IndexWinningMappingSpecs(specifications);
        if (winningSpecByConcept.Count == 0)
        {
            return [];
        }

        var designBindings = designOverlay is not null ? IndexDesignConceptBindings(designOverlay) : null;
        var records = new List<RuleRecord>();
        var state = new SinglePassProjectionState(engine, winningSpecByConcept, designBindings, decoder, records);

        ZeroLohOrdering.ForEachOrderedByGuid(
            observations,
            static o => o.Id.Value,
            state,
            static (s, obs) =>
            {
                if (!TryMapObservation(obs, s.Engine, s.WinningSpecByConcept, s.DesignBindings, s.Decoder, out var mapped))
                {
                    return;
                }

                var obsKey = obs.Id.Value.ToString("D", CultureInfo.InvariantCulture);
                var ruleRecord = ToRuleRecordFromMapped(mapped.Dataset!, obsKey, mapped.Values);
                if (s.HasPrevious && s.PreviousObservationId == obs.Id)
                {
                    s.Records[^1] = ruleRecord;
                }
                else
                {
                    s.HasPrevious = true;
                    s.PreviousObservationId = obs.Id;
                    s.Records.Add(ruleRecord);
                }
            });

        return records;
    }

    private static Dictionary<string, ProjectedObservation> ProjectObservations(
        IMappingEngine engine,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<MappingSpecification> specifications,
        ProtocolDesign? designOverlay,
        ICodelistDecoder decoder)
    {
        var projected = new Dictionary<string, ProjectedObservation>(StringComparer.Ordinal);
        if (specifications.Count == 0 || observations.Count == 0)
        {
            return projected;
        }

        var winningSpecByConcept = IndexWinningMappingSpecs(specifications);
        if (winningSpecByConcept.Count == 0)
        {
            return projected;
        }

        var designBindings = designOverlay is not null ? IndexDesignConceptBindings(designOverlay) : null;
        var state = (Engine: engine, WinningSpecByConcept: winningSpecByConcept, DesignBindings: designBindings, Decoder: decoder, Projected: projected);

        ZeroLohOrdering.ForEachOrderedByGuid(
            observations,
            static o => o.Id.Value,
            state,
            static (s, obs) =>
            {
                if (!TryMapObservation(obs, s.Engine, s.WinningSpecByConcept, s.DesignBindings, s.Decoder, out var mapped))
                {
                    return;
                }

                var obsKey = obs.Id.Value.ToString("D", CultureInfo.InvariantCulture);
                var vars = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                for (var i = 0; i < mapped.Values.Count; i++)
                {
                    var mv = mapped.Values[i];
                    vars[mv.Variable] = mv.Value;
                }

                s.Projected[obsKey] = new ProjectedObservation(obsKey, obs.EnrollmentId, mapped.Dataset!, vars);
            });

        return projected;
    }

    private static bool TryMapObservation(
        SimulationObservationSnapshot obs,
        IMappingEngine engine,
        Dictionary<ConceptId, MappingSpecification> winningSpecByConcept,
        Dictionary<string, Dictionary<string, ConceptRef>>? designBindings,
        ICodelistDecoder decoder,
        out MappingResult mapped)
    {
        var concept = obs.Concept;
        if (designBindings is not null
            && obs.CollectionSpecId is not null
            && obs.ItemId is not null
            && (!designBindings.TryGetValue(obs.CollectionSpecId, out var specItems)
                || !specItems.TryGetValue(obs.ItemId, out concept)))
        {
            mapped = default!;
            return false;
        }

        if (!winningSpecByConcept.TryGetValue(concept.ConceptId, out var winningSpec))
        {
            mapped = default!;
            return false;
        }

        var input = MappingInput.From(concept, obs.Value);
        mapped = engine.Map(winningSpec, input, decoder);
        return mapped.Succeeded && !string.IsNullOrWhiteSpace(mapped.Dataset) && mapped.Values.Count > 0;
    }

    private static Dictionary<ConceptId, MappingSpecification> IndexWinningMappingSpecs(IReadOnlyList<MappingSpecification> specifications)
    {
        var map = new Dictionary<ConceptId, MappingSpecification>();
        for (var s = 0; s < specifications.Count; s++)
        {
            var spec = specifications[s];
            for (var m = 0; m < spec.Mappings.Count; m++)
            {
                map[spec.Mappings[m].Concept.ConceptId] = spec;
            }
        }

        return map;
    }

    private static Dictionary<string, Dictionary<string, ConceptRef>> IndexDesignConceptBindings(ProtocolDesign design)
    {
        var bySpec = new Dictionary<string, Dictionary<string, ConceptRef>>(design.CollectionSpecs.Count, StringComparer.Ordinal);
        for (var s = 0; s < design.CollectionSpecs.Count; s++)
        {
            var spec = design.CollectionSpecs[s];
            if (!bySpec.TryGetValue(spec.Id, out var byItem))
            {
                byItem = new Dictionary<string, ConceptRef>(spec.Items.Count, StringComparer.Ordinal);
                bySpec.Add(spec.Id, byItem);
            }

            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                byItem[item.Id] = item.Concept;
            }
        }

        return bySpec;
    }

    private static bool CanShareProjections(
        IReadOnlyList<MappingSpecification> baselineSpecs,
        IReadOnlyList<MappingSpecification> candidateSpecs,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign) =>
        MappingSpecsMatch(baselineSpecs, candidateSpecs)
        && DesignConceptBindingsMatch(baselineDesign, candidateDesign);

    private static bool MappingSpecsMatch(
        IReadOnlyList<MappingSpecification> left,
        IReadOnlyList<MappingSpecification> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!MappingSpecificationEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MappingSpecificationEqual(MappingSpecification left, MappingSpecification right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            || !string.Equals(left.Overrides, right.Overrides, StringComparison.Ordinal)
            || !left.ControlledTerminology.SequenceEqual(right.ControlledTerminology)
            || !left.Units.SequenceEqual(right.Units)
            || left.Mappings.Count != right.Mappings.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Mappings.Count; i++)
        {
            var lm = left.Mappings[i];
            var rm = right.Mappings[i];
            if (!string.Equals(lm.Id, rm.Id, StringComparison.Ordinal)
                || lm.Concept != rm.Concept
                || !string.Equals(lm.Dataset, rm.Dataset, StringComparison.Ordinal)
                || lm.Result != rm.Result
                || !string.Equals(lm.StandardUnit, rm.StandardUnit, StringComparison.Ordinal)
                || lm.Decimals != rm.Decimals
                || !string.Equals(lm.Codelist, rm.Codelist, StringComparison.Ordinal)
                || !lm.Conversions.SequenceEqual(rm.Conversions)
                || !StringDictionaryEqual(lm.Assign, rm.Assign))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringDictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        ReferenceEquals(left, right)
        || (left.Count == right.Count
            && left.All(kv => right.TryGetValue(kv.Key, out var rv) && string.Equals(kv.Value, rv, StringComparison.Ordinal)));

    private static bool DesignConceptBindingsMatch(ProtocolDesign? left, ProtocolDesign? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.CollectionSpecs.Count != right.CollectionSpecs.Count)
        {
            return false;
        }

        var leftBindings = IndexDesignConceptBindings(left);
        var rightBindings = IndexDesignConceptBindings(right);
        if (leftBindings.Count != rightBindings.Count)
        {
            return false;
        }

        foreach (var (specId, leftItems) in leftBindings)
        {
            if (!rightBindings.TryGetValue(specId, out var rightItems)
                || leftItems.Count != rightItems.Count)
            {
                return false;
            }

            foreach (var (itemId, leftConcept) in leftItems)
            {
                if (!rightItems.TryGetValue(itemId, out var rightConcept) || leftConcept != rightConcept)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static RuleRecord ToRuleRecord(ProjectedObservation projected)
    {
        var values = new Dictionary<string, RuleValue>(projected.Variables.Count, StringComparer.Ordinal);
        foreach (var (variable, raw) in projected.Variables)
        {
            values[variable] = ConvertProjectedVariableValue(raw);
        }

        return new RuleRecord(projected.Dataset, projected.RecordKey, values);
    }

    private static RuleRecord ToRuleRecordFromMapped(
        string dataset,
        string recordKey,
        IReadOnlyList<MappedValue> mappedValues)
    {
        var values = new Dictionary<string, RuleValue>(mappedValues.Count, StringComparer.Ordinal);
        for (var i = 0; i < mappedValues.Count; i++)
        {
            var mv = mappedValues[i];
            values[mv.Variable] = ConvertProjectedVariableValue(mv.Value);
        }

        return new RuleRecord(dataset, recordKey, values);
    }

    private static RuleValue ConvertProjectedVariableValue(string? raw)
    {
        if (raw is null)
        {
            return RuleValue.Null;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric)
            ? RuleValue.FromNumber(numeric)
            : RuleValue.FromText(raw);
    }

    private static bool VariablesEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right) =>
        left.Count == right.Count
        && left.All(kv => right.TryGetValue(kv.Key, out var rv) && string.Equals(kv.Value, rv, StringComparison.Ordinal));

    private static DatasetAccumulator GetDataset(SortedDictionary<string, DatasetAccumulator> map, string dataset)
    {
        if (!map.TryGetValue(dataset, out var acc))
        {
            acc = new DatasetAccumulator();
            map[dataset] = acc;
        }

        return acc;
    }

    private sealed record ProjectedObservation(
        string RecordKey,
        EnrollmentId EnrollmentId,
        string Dataset,
        IReadOnlyDictionary<string, string?> Variables);

    private sealed class SinglePassProjectionState(
        IMappingEngine engine,
        Dictionary<ConceptId, MappingSpecification> winningSpecByConcept,
        Dictionary<string, Dictionary<string, ConceptRef>>? designBindings,
        ICodelistDecoder decoder,
        List<RuleRecord> records)
    {
        public IMappingEngine Engine { get; } = engine;

        public Dictionary<ConceptId, MappingSpecification> WinningSpecByConcept { get; } = winningSpecByConcept;

        public Dictionary<string, Dictionary<string, ConceptRef>>? DesignBindings { get; } = designBindings;

        public ICodelistDecoder Decoder { get; } = decoder;

        public List<RuleRecord> Records { get; } = records;

        public bool HasPrevious { get; set; }

        public ObservationId PreviousObservationId { get; set; }
    }

    private sealed class SnapshotCodelistDecoder(IReadOnlyDictionary<(string Codelist, string Code), string> decodes) : ICodelistDecoder
    {
        public string? SubmissionValue(string codelist, string code) =>
            decodes.TryGetValue((codelist, code), out var submissionValue) ? submissionValue : null;
    }

    private sealed class DatasetAccumulator
    {
        private readonly List<ProjectionRecordChangeSample> _samples = [];
        private int _added;
        private int _removed;
        private int _changed;

        public bool HasChanges => _added > 0 || _removed > 0 || _changed > 0;

        public void RecordAdded(string recordKey, IReadOnlyDictionary<string, string?> after)
        {
            _added++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Added, null, after));
        }

        public void RecordRemoved(string recordKey, IReadOnlyDictionary<string, string?> before)
        {
            _removed++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Removed, before, null));
        }

        public void RecordChanged(
            string recordKey,
            IReadOnlyDictionary<string, string?> before,
            IReadOnlyDictionary<string, string?> after)
        {
            _changed++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Changed, before, after));
        }

        public ProjectionDatasetChange ToSummary(string dataset) =>
            new(dataset, _added, _removed, _changed, _samples);

        private void AddSample(ProjectionRecordChangeSample sample)
        {
            if (_samples.Count < MaxSamplesPerDataset)
            {
                _samples.Add(sample);
            }
        }
    }
}

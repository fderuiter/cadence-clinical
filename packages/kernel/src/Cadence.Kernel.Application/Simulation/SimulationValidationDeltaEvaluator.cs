using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Kernel.Application.Simulation;

internal static class SimulationValidationDeltaEvaluator
{
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
        IReadOnlyList<RuleRecord> candidateProjectedRecords)
    {
        if (baselinePacks.Count == 0 && candidatePacks.Count == 0)
        {
            return (new ValidationDeltaSummary([], [], 0), [], false);
        }

        var sameCollectionDesign = SimulationCollectionIndex.CollectionDesignsMatch(baselineDesign, candidateDesign);
        var sameRulePacks = RulePacksMatch(baselinePacks, candidatePacks);

        var baselineCollectionIndex = SimulationCollectionIndex.Build(snapshot, baselineDesign);
        var candidateCollectionIndex = sameCollectionDesign
            ? baselineCollectionIndex
            : SimulationCollectionIndex.Build(snapshot, candidateDesign);

        var enrollmentLookup = new LazyRecordEnrollmentLookup(
            baselineCollectionIndex,
            candidateCollectionIndex,
            snapshot.VisibleObservations);

        var allDesigns = snapshot.ProtocolDesigns.Values
            .Concat(new[] { baselineDesign, candidateDesign }.OfType<ProtocolDesign>())
            .ToList();
        var blindingIndex = blinding is BlindedTreatmentClassifier
            ? ProtocolBlindingIndex.Create(allDesigns, snapshot.Concepts)
            : null;

        BlindedConceptMetadata? ResolveConcept(ConceptId cid) =>
            snapshot.Concepts.TryGetValue(cid, out var meta) ? meta : null;

        bool IsRecordUnblinded(string recordKey)
        {
            if (enrollmentLookup.TryResolveEnrollment(recordKey, out var enrollmentId))
            {
                return snapshot.UnblindedEnrollments.Contains(enrollmentId);
            }

            return snapshot.CanUnblindStudy;
        }

        var baselineDesignRecords = BuildDesignRuleRecords(baselineDesign);
        var sameDesignRecords = DesignRecordsMatch(baselineDesign, candidateDesign);
        var candidateDesignRecords = sameDesignRecords ? baselineDesignRecords : BuildDesignRuleRecords(candidateDesign);

        var maskedBlindedFinding = false;
        var unchanged = 0;
        var (baselineFindings, candidateFindings) = EvaluateAllStages(
            evaluator,
            blinding,
            blindingIndex,
            allDesigns,
            ResolveConcept,
            IsRecordUnblinded,
            asOfDate,
            snapshot.DeclaredStandards,
            baselinePacks,
            candidatePacks,
            sameRulePacks,
            baselineCollectionIndex,
            candidateCollectionIndex,
            sameCollectionDesign,
            baselineProjectedRecords,
            candidateProjectedRecords,
            baselineDesignRecords,
            candidateDesignRecords,
            sameDesignRecords,
            ref unchanged,
            ref maskedBlindedFinding);

        var opening = new List<SimulatedValidationFinding>();
        var resolving = new List<SimulatedValidationFinding>();
        var impactedEnrollments = new HashSet<EnrollmentId>();

        foreach (var (key, finding) in candidateFindings.OrderBy(kv => kv.Key.RuleId, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Stage)
                     .ThenBy(kv => kv.Key.Dataset, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Subject, StringComparer.Ordinal))
        {
            if (baselineFindings.ContainsKey(key))
            {
                unchanged++;
            }
            else
            {
                opening.Add(finding);
                enrollmentLookup.TrackEnrollment(finding.Subject, impactedEnrollments);
            }
        }

        foreach (var (key, finding) in baselineFindings.OrderBy(kv => kv.Key.RuleId, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Stage)
                     .ThenBy(kv => kv.Key.Dataset, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Subject, StringComparer.Ordinal))
        {
            if (!candidateFindings.ContainsKey(key))
            {
                resolving.Add(finding);
                enrollmentLookup.TrackEnrollment(finding.Subject, impactedEnrollments);
            }
        }

        return (new ValidationDeltaSummary(opening, resolving, unchanged), impactedEnrollments, maskedBlindedFinding);
    }

    private static IReadOnlyList<RuleRecord> BuildDesignRuleRecords(ProtocolDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["title"] = RuleValue.FromText(design.Title),
            ["objective_count"] = RuleValue.FromNumber(design.Objectives.Count),
            ["endpoint_count"] = RuleValue.FromNumber(design.Endpoints.Count),
            ["planned_activity_count"] = RuleValue.FromNumber(design.PlannedActivities.Count),
            ["collection_spec_count"] = RuleValue.FromNumber(design.CollectionSpecs.Count),
        };
        return [new RuleRecord("protocol_design", "design", values)];
    }

    private static (
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> Baseline,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> Candidate) EvaluateAllStages(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RulePack> candidatePacks,
        bool sameRulePacks,
        SimulationCollectionIndex baselineCollectionIndex,
        SimulationCollectionIndex candidateCollectionIndex,
        bool sameCollectionDesign,
        IReadOnlyList<RuleRecord> baselineProjectedRecords,
        IReadOnlyList<RuleRecord> candidateProjectedRecords,
        IReadOnlyList<RuleRecord> baselineDesignRecords,
        IReadOnlyList<RuleRecord> candidateDesignRecords,
        bool sameDesignRecords,
        ref int unchangedCount,
        ref bool maskedBlindedFinding)
    {
        var baseline = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();
        var candidate = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();

        if (sameCollectionDesign && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Collection,
                asOfDate,
                declaredStandards,
                baselinePacks,
                baselineCollectionIndex.CreateCursor(),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Collection,
                asOfDate,
                declaredStandards,
                baselinePacks,
                baselineCollectionIndex.CreateCursor(),
                candidatePacks,
                candidateCollectionIndex.CreateCursor(),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        if (ReferenceEquals(baselineProjectedRecords, candidateProjectedRecords) && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Standardization,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineProjectedRecords),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Standardization,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineProjectedRecords),
                candidatePacks,
                new RuleRecordListCursor(candidateProjectedRecords),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        if (sameDesignRecords && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Design,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineDesignRecords),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Design,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineDesignRecords),
                candidatePacks,
                new RuleRecordListCursor(candidateDesignRecords),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        return (baseline, candidate);
    }

    private static int EvaluateUnchangedStage(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> packs,
        IResettableRuleRecordCursor cursor,
        ref bool maskedBlindedFinding)
    {
        var violations = AmendmentRegressionEngine.EvaluateStage(
            evaluator,
            stage,
            asOfDate,
            packs,
            cursor,
            declaredStandards);
        if (violations.Count == 0)
        {
            return 0;
        }

        var uniqueVisible = new HashSet<DeltaFindingKey>(violations.Count);
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (ShouldMaskViolation(violation, blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded))
            {
                maskedBlindedFinding = true;
                continue;
            }

            uniqueVisible.Add(new DeltaFindingKey(violation.RuleId, stage, violation.Dataset, violation.RecordKey));
        }

        return uniqueVisible.Count;
    }

    private static void EvaluateStagePair(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IResettableRuleRecordCursor baselineCursor,
        IReadOnlyList<RulePack> candidatePacks,
        IResettableRuleRecordCursor candidateCursor,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> baselineSink,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> candidateSink,
        ref bool maskedBlindedFinding)
    {
        var (baselineViolations, targetViolations) = AmendmentRegressionEngine.EvaluateStageDelta(
            evaluator,
            stage,
            asOfDate,
            baselinePacks,
            baselineCursor,
            candidatePacks,
            candidateCursor,
            declaredStandards);
        CollectStageViolations(blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded, stage, baselineViolations, baselineSink, ref maskedBlindedFinding);
        CollectStageViolations(blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded, stage, targetViolations, candidateSink, ref maskedBlindedFinding);
    }

    private static void CollectStageViolations(
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        IReadOnlyList<RuleViolation> violations,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> sink,
        ref bool maskedBlindedFinding)
    {
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (ShouldMaskViolation(violation, blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded))
            {
                maskedBlindedFinding = true;
                continue;
            }

            var key = new DeltaFindingKey(violation.RuleId, stage, violation.Dataset, violation.RecordKey);
            sink.TryAdd(
                key,
                new SimulatedValidationFinding(
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    stage,
                    violation.Dataset,
                    violation.RecordKey,
                    violation.Explanation));
        }
    }

    private static bool ShouldMaskViolation(
        RuleViolation violation,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded)
    {
        if (isRecordUnblinded(violation.RecordKey))
        {
            return false;
        }

        if (blindingIndex is not null)
        {
            return blindingIndex.IsBlindedViolation(violation);
        }

        var target = new FindingTarget(
            FindingTargetKind.DatasetRecord,
            violation.Dataset,
            violation.RecordKey,
            violation.Observed.FirstOrDefault()?.Variable);
        var observedVars = violation.Observed
            .Select(o => new ObservedVariable(o.Variable, o.Value.ToString()))
            .ToList();
        return blinding.IsBlindedFinding(target, observedVars, designs, resolveConcept);
    }

    private static bool DesignRecordsMatch(ProtocolDesign? left, ProtocolDesign? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            && left.Objectives.Count == right.Objectives.Count
            && left.Endpoints.Count == right.Endpoints.Count
            && left.PlannedActivities.Count == right.PlannedActivities.Count
            && left.CollectionSpecs.Count == right.CollectionSpecs.Count;
    }

    private static bool RulePacksMatch(IReadOnlyList<RulePack> left, IReadOnlyList<RulePack> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var p = 0; p < left.Count; p++)
        {
            var lp = left[p];
            var rp = right[p];
            if (ReferenceEquals(lp, rp))
            {
                continue;
            }

            if (lp.Authority != rp.Authority
                || !string.Equals(lp.Title, rp.Title, StringComparison.Ordinal)
                || lp.Rules.Count != rp.Rules.Count)
            {
                return false;
            }

            for (var r = 0; r < lp.Rules.Count; r++)
            {
                if (!RulesMatch(lp.Rules[r], rp.Rules[r]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RulesMatch(Rule left, Rule right) =>
        ReferenceEquals(left, right)
        || (string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Version == right.Version
            && left.Authority == right.Authority
            && left.Citation == right.Citation
            && left.Applicability == right.Applicability
            && left.Severity == right.Severity
            && string.Equals(left.Condition.ToString(), right.Condition.ToString(), StringComparison.Ordinal)
            && string.Equals(left.Explanation, right.Explanation, StringComparison.Ordinal)
            && string.Equals(left.RemediationGuidance, right.RemediationGuidance, StringComparison.Ordinal)
            && left.Effective == right.Effective);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Simulation;

internal static class SimulationLibraryCandidateRunner
{
    internal static async Task<CommandResult<SimulationResult>> SimulateMappingSpecificationAsync(
        SimulationEngine engine,
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var studyId = candidateRequest.StudyId!.Value;
        if (!await engine.Inputs.StudyExistsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false))
        {
            return SimulationErrors.StudyUnknown;
        }

        var loaded = await SimulationCandidateLoader.LoadMappingCandidateAsync(engine.Inputs, tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candSpec, candRef, candHash, explicitBaseItem, explicitBaseSpec) = loaded.Value;
        var scope = NodeRef.For(studyId);
        if (await engine.AuthorizeRunAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var explicitBaseRef = explicitBaseItem is null ? null : SimulationCandidateLoader.NodeRefForItem(explicitBaseItem);
        var topLevelMask = await engine.CheckTopLevelMaskAsync(tenant, actor, [scope, candRef, explicitBaseRef], cancellationToken).ConfigureAwait(false);
        if (topLevelMask is { } reason)
        {
            var maskedEngines = new List<EngineVersion>
            {
                SimulationSpecificationDiff.MappingDiffEngine,
                SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
            };
            maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            IReadOnlyList<MaskedPart> withheld =
            [
                new MaskedPart(SimulationEngine.DependentsPart, reason),
                new MaskedPart(SimulationEngine.ProjectionChangesPart, reason),
                new MaskedPart(SimulationEngine.StructuralDiffPart, reason),
            ];
            return SimulationResultSealer.SealResult(
                scope,
                asOf,
                new CandidateSummary(CandidateKind.MappingSpecification, null, null, null, null),
                maskedEngines,
                visibleStateDigest: null,
                diff: [],
                schedule: [],
                withheld,
                dependents: null,
                matrixGaps: null,
                operationalImpact: null,
                projectionChanges: null,
                validationDelta: null);
        }

        var rawSnapshot = await engine.Inputs.LoadStudySnapshotAsync(tenant, studyId, asOf, cancellationToken).ConfigureAwait(false);
        var maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
            tenant,
            studyId,
            actor,
            rawSnapshot,
            extraDesigns: [],
            engine.Visibility,
            engine.Blinding,
            engine.Probe,
            cancellationToken).ConfigureAwait(false);

        var (baseItem, baseSpec) = SimulationCandidateLoader.ResolveMappingBase(candSpec, explicitBaseItem, explicitBaseSpec, maskedSnapshot.PinnedLibraryItems);
        var baseRef = baseItem is null ? null : SimulationCandidateLoader.NodeRefForItem(baseItem);

        if (baseRef is not null && explicitBaseRef is null)
        {
            var inferredBaseMask = await engine.CheckTopLevelMaskAsync(tenant, actor, [baseRef], cancellationToken).ConfigureAwait(false);
            if (inferredBaseMask is { } baseReason)
            {
                var maskedEngines = new List<EngineVersion>
                {
                    SimulationSpecificationDiff.MappingDiffEngine,
                    SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
                };
                maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return SimulationResultSealer.SealResult(
                    scope,
                    asOf,
                    new CandidateSummary(CandidateKind.MappingSpecification, null, null, null, null),
                    maskedEngines,
                    visibleStateDigest: null,
                    diff: [],
                    schedule: [],
                    [
                        new MaskedPart(SimulationEngine.DependentsPart, baseReason),
                        new MaskedPart(SimulationEngine.ProjectionChangesPart, baseReason),
                        new MaskedPart(SimulationEngine.StructuralDiffPart, baseReason),
                    ],
                    dependents: null,
                    matrixGaps: null,
                    operationalImpact: null,
                    projectionChanges: null,
                    validationDelta: null);
            }
        }

        var summary = new CandidateSummary(
            CandidateKind.MappingSpecification,
            candRef,
            baseRef,
            candHash,
            baseItem?.ContentHash);
        var diff = SimulationSpecificationDiff.CompareMappingSpecifications(baseSpec, candSpec);
        var engines = new List<EngineVersion>
        {
            SimulationSpecificationDiff.MappingDiffEngine,
            SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
            SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
            SimulationOperationalEvaluator.Engine,
        };

        DependentsReport? dependents = null;
        IReadOnlyList<MaskReason> dependentMaskReasons = [];
        if (diff.Count > 0 && baseItem is not null)
        {
            dependents = await engine.Inputs.DependentsOfNodeAsync(
                tenant,
                actor,
                NodeRef.For(baseItem.Id),
                candRef?.Unversioned(),
                asOf,
                cancellationToken).ConfigureAwait(false);
            (dependents, dependentMaskReasons) = SimulationSnapshotMasking.OmitMaskedDependents(dependents);
            engines.Add(SimulationEngine.DependentsEngine);
        }

        var (baselineSpecs, candidateSpecs) = SimulationCandidateLoader.BuildMappingSpecStacks(maskedSnapshot.PinnedLibraryItems, baseItem, baseSpec, candSpec);
        var activeDesign = SimulationCandidateLoader.SelectActiveProtocolDesign(maskedSnapshot);
        var pinnedPacks = SimulationCandidateLoader.ParsePinnedRulePacks(maskedSnapshot.PinnedRulePacks);

        var (projectionChanges, baseProjected, candProjected, projImpacted) =
            SimulationProjectionAndValidationEvaluator.EvaluateProjections(
                engine.MappingEngine,
                maskedSnapshot,
                baselineSpecs,
                candidateSpecs,
                activeDesign,
                activeDesign);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var (validationDelta, ruleImpacted, maskedBlindedFinding) =
            SimulationProjectionAndValidationEvaluator.EvaluateValidationDelta(
                engine.RuleEvaluator,
                engine.Blinding,
                maskedSnapshot,
                asOfDate,
                pinnedPacks,
                pinnedPacks,
                activeDesign,
                activeDesign,
                baseProjected,
                candProjected);

        var combinedImpacted = new HashSet<EnrollmentId>(projImpacted);
        combinedImpacted.UnionWith(ruleImpacted);

        var mergedHidden = SimulationSnapshotMasking.MergeHidden(
            maskedSnapshot.Hidden,
            dependentMaskReasons.Concat(maskedBlindedFinding ? [MaskReason.Blinded] : []));
        var operational = SimulationOperationalEvaluator.Evaluate(
            maskedSnapshot with { Hidden = mergedHidden },
            asOf,
            activeDesign,
            activeDesign,
            amendment: null,
            soaDiff: null,
            combinedImpacted);

        var maskedParts = SimulationResultSealer.CombineMaskedParts(maskedSnapshot.MaskedParts, dependentMaskReasons, maskedBlindedFinding);
        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return SimulationResultSealer.SealResult(
            scope,
            asOf,
            summary,
            engines,
            maskedSnapshot.VisibleStateDigest,
            diff,
            schedule: [],
            maskedParts,
            dependents,
            matrixGaps: null,
            operational,
            projectionChanges,
            validationDelta);
    }

    internal static async Task<CommandResult<SimulationResult>> SimulateRulePackAsync(
        SimulationEngine engine,
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var studyId = candidateRequest.StudyId!.Value;
        if (!await engine.Inputs.StudyExistsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false))
        {
            return SimulationErrors.StudyUnknown;
        }

        var loaded = await SimulationCandidateLoader.LoadRulePackCandidateAsync(engine.Inputs, tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candPack, candItem, candRef, candHash, explicitBaseItem, explicitBasePack) = loaded.Value;
        var scope = NodeRef.For(studyId);
        if (await engine.AuthorizeRunAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var explicitBaseRef = explicitBaseItem is null ? null : SimulationCandidateLoader.NodeRefForItem(explicitBaseItem);
        var topLevelMask = await engine.CheckTopLevelMaskAsync(tenant, actor, [scope, candRef, explicitBaseRef], cancellationToken).ConfigureAwait(false);
        if (topLevelMask is { } reason)
        {
            var maskedEngines = new List<EngineVersion>
            {
                SimulationSpecificationDiff.RulePackDiffEngine,
                SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
            };
            maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            IReadOnlyList<MaskedPart> withheld =
            [
                new MaskedPart(SimulationEngine.DependentsPart, reason),
                new MaskedPart(SimulationEngine.StructuralDiffPart, reason),
                new MaskedPart(SimulationEngine.ValidationDeltaPart, reason),
            ];
            return SimulationResultSealer.SealResult(
                scope,
                asOf,
                new CandidateSummary(CandidateKind.RulePack, null, null, null, null),
                maskedEngines,
                visibleStateDigest: null,
                diff: [],
                schedule: [],
                withheld,
                dependents: null,
                matrixGaps: null,
                operationalImpact: null,
                projectionChanges: null,
                validationDelta: null);
        }

        var rawSnapshot = await engine.Inputs.LoadStudySnapshotAsync(tenant, studyId, asOf, cancellationToken).ConfigureAwait(false);
        var maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
            tenant,
            studyId,
            actor,
            rawSnapshot,
            extraDesigns: [],
            engine.Visibility,
            engine.Blinding,
            engine.Probe,
            cancellationToken).ConfigureAwait(false);

        var (baseItem, basePack) = SimulationCandidateLoader.ResolveRulePackBase(candPack, candItem, explicitBaseItem, explicitBasePack, maskedSnapshot.PinnedRulePacks);
        var baseRef = baseItem is null ? null : SimulationCandidateLoader.NodeRefForItem(baseItem);

        if (baseRef is not null && explicitBaseRef is null)
        {
            var inferredBaseMask = await engine.CheckTopLevelMaskAsync(tenant, actor, [baseRef], cancellationToken).ConfigureAwait(false);
            if (inferredBaseMask is { } baseReason)
            {
                var maskedEngines = new List<EngineVersion>
                {
                    SimulationSpecificationDiff.RulePackDiffEngine,
                    SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
                };
                maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return SimulationResultSealer.SealResult(
                    scope,
                    asOf,
                    new CandidateSummary(CandidateKind.RulePack, null, null, null, null),
                    maskedEngines,
                    visibleStateDigest: null,
                    diff: [],
                    schedule: [],
                    [
                        new MaskedPart(SimulationEngine.DependentsPart, baseReason),
                        new MaskedPart(SimulationEngine.StructuralDiffPart, baseReason),
                        new MaskedPart(SimulationEngine.ValidationDeltaPart, baseReason),
                    ],
                    dependents: null,
                    matrixGaps: null,
                    operationalImpact: null,
                    projectionChanges: null,
                    validationDelta: null);
            }
        }

        var summary = new CandidateSummary(
            CandidateKind.RulePack,
            candRef,
            baseRef,
            candHash,
            baseItem?.ContentHash);
        var diff = SimulationSpecificationDiff.CompareRulePacks(basePack, candPack);
        var engines = new List<EngineVersion>
        {
            SimulationSpecificationDiff.RulePackDiffEngine,
            SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
            SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
            SimulationOperationalEvaluator.Engine,
        };

        DependentsReport? dependents = null;
        IReadOnlyList<MaskReason> dependentMaskReasons = [];
        if (diff.Count > 0 && baseItem is not null)
        {
            dependents = await engine.Inputs.DependentsOfNodeAsync(
                tenant,
                actor,
                NodeRef.For(baseItem.Id),
                candRef?.Unversioned(),
                asOf,
                cancellationToken).ConfigureAwait(false);
            (dependents, dependentMaskReasons) = SimulationSnapshotMasking.OmitMaskedDependents(dependents);
            engines.Add(SimulationEngine.DependentsEngine);
        }

        var pinnedSpecs = SimulationCandidateLoader.ParsePinnedMappingSpecs(maskedSnapshot.PinnedLibraryItems);
        var activeDesign = SimulationCandidateLoader.SelectActiveProtocolDesign(maskedSnapshot);
        var (baselinePacks, candidatePacks) = SimulationCandidateLoader.BuildRulePackStacks(maskedSnapshot.PinnedRulePacks, baseItem, basePack, candPack);

        var (projectionChanges, baseProjected, candProjected, projImpacted) =
            SimulationProjectionAndValidationEvaluator.EvaluateProjections(
                engine.MappingEngine,
                maskedSnapshot,
                pinnedSpecs,
                pinnedSpecs,
                activeDesign,
                activeDesign);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var (validationDelta, ruleImpacted, maskedBlindedFinding) =
            SimulationProjectionAndValidationEvaluator.EvaluateValidationDelta(
                engine.RuleEvaluator,
                engine.Blinding,
                maskedSnapshot,
                asOfDate,
                baselinePacks,
                candidatePacks,
                activeDesign,
                activeDesign,
                baseProjected,
                candProjected);

        var combinedImpacted = new HashSet<EnrollmentId>(projImpacted);
        combinedImpacted.UnionWith(ruleImpacted);

        var mergedHidden = SimulationSnapshotMasking.MergeHidden(
            maskedSnapshot.Hidden,
            dependentMaskReasons.Concat(maskedBlindedFinding ? [MaskReason.Blinded] : []));
        var operational = SimulationOperationalEvaluator.Evaluate(
            maskedSnapshot with { Hidden = mergedHidden },
            asOf,
            activeDesign,
            activeDesign,
            amendment: null,
            soaDiff: null,
            combinedImpacted);

        var maskedParts = SimulationResultSealer.CombineMaskedParts(maskedSnapshot.MaskedParts, dependentMaskReasons, maskedBlindedFinding);
        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return SimulationResultSealer.SealResult(
            scope,
            asOf,
            summary,
            engines,
            maskedSnapshot.VisibleStateDigest,
            diff,
            schedule: [],
            maskedParts,
            dependents,
            matrixGaps: null,
            operational,
            projectionChanges,
            validationDelta);
    }
}

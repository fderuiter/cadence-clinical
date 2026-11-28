using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;
using Cadence.Standards.Mapping;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>A protocol version as simulation reads it.</summary>
/// <param name="Id">The version.</param>
/// <param name="StudyId">Its study.</param>
/// <param name="Number">Its number in the study.</param>
/// <param name="Status">Its lifecycle status.</param>
/// <param name="ParentVersionId">The version it was forked from, or null for the first version.</param>
/// <param name="Content">Its content, a JSON object.</param>
/// <param name="ContentHash">Its frozen content hash, or null while it is a draft.</param>
/// <param name="VersionIndex">Its audit version, which pins references to it.</param>
/// <param name="Amendment">Its amendment metadata when forked from a parent version.</param>
public sealed record ProtocolVersionSnapshot(
    ProtocolVersionId Id,
    StudyId StudyId,
    int Number,
    ProtocolVersionStatus Status,
    ProtocolVersionId? ParentVersionId,
    string Content,
    string? ContentHash,
    long VersionIndex,
    ProtocolAmendment? Amendment = null);

/// <summary>
/// The reads simulation makes. Implemented by kernel persistence with no-tracking queries under the tenant's Row-Level
/// Security; it has no way to write (K-SIM-1).
/// </summary>
public interface ISimulationInputs
{
    /// <summary>The protocol version, or null when the tenant has none with that id.</summary>
    Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken);

    /// <summary>
    /// What depends on any version of <paramref name="baseVersion"/> at <paramref name="asOf"/>, through the kernel's one
    /// dependents read, without walking through <paramref name="candidate"/> (a stored fork depends on its parent).
    /// From the base version the read enters only the design elements in <paramref name="impactedElements"/>
    /// (<see cref="ProtocolElementImpact"/>, ADR-2474). Objects <paramref name="reader"/> may not see are
    /// counted and masked.
    /// </summary>
    Task<DependentsReport> DependentsAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId baseVersion,
        ProtocolVersionId? candidate,
        IReadOnlySet<NodeRef> impactedElements,
        SimulationAsOf asOf,
        CancellationToken cancellationToken);

    /// <summary>True when <paramref name="studyId"/> exists in <paramref name="tenant"/>.</summary>
    Task<bool> StudyExistsAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>Reads a stored library item version in <paramref name="tenant"/>, or null when unknown.</summary>
    Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
        Task.FromResult<LibraryItemView?>(null);

    /// <summary>Reads what depends on <paramref name="start"/> at <paramref name="asOf"/>, excluding <paramref name="excludedCandidate"/>.</summary>
    Task<DependentsReport> DependentsOfNodeAsync(
        TenantId tenant,
        Actor reader,
        NodeRef start,
        NodeRef? excludedCandidate,
        SimulationAsOf asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(new DependentsReport(start.ToString(), LineageQuery.DefaultDepth, asOf.ValidAt, asOf.KnownAt, [], 0, false));

    /// <summary>Reads the bitemporal operational, clinical, and standards snapshot of <paramref name="studyId"/> at <paramref name="asOf"/>.</summary>
    Task<SimulationStudySnapshot> LoadStudySnapshotAsync(
        TenantId tenant,
        StudyId studyId,
        SimulationAsOf asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(SimulationStudySnapshot.Empty);
}

/// <summary>
/// The simulation engine (ADR-2480). It reads the candidate and its base, checks <c>simulation.run</c> at the
/// candidate's study, masks what the actor may not see through <see cref="INodeVisibility"/> and
/// <see cref="IBlindedTreatmentClassifier"/>, evaluates the pure functions and seals the result with its input and
/// result hashes. It depends on no command sender, unit of work or persistence type, and an architecture test keeps it
/// that way (K-SIM-1).
/// </summary>
public sealed class SimulationEngine(
    ISimulationInputs inputs,
    IAuthorizationProbe probe,
    INodeVisibility visibility,
    ICurrentActor currentActor,
    IClock clock,
    IMappingEngine? mappingEngine = null,
    IRuleEvaluator? ruleEvaluator = null,
    IBlindedTreatmentClassifier? blindingClassifier = null) : ISimulationEngine
{
    /// <summary>The section the structural diff is reported in, and masked as.</summary>
    public const string StructuralDiffPart = "structural_diff";

    /// <summary>The section the dependents are reported in, and masked as.</summary>
    public const string DependentsPart = "dependents";

    /// <summary>The section the schedule changes are reported in, and masked as.</summary>
    public const string ScheduleChangesPart = "schedule_changes";

    /// <summary>The section dataset projections are reported in, and masked as.</summary>
    public const string ProjectionChangesPart = "projection_changes";

    /// <summary>The section validation finding deltas are reported in, and masked as.</summary>
    public const string ValidationDeltaPart = "validation_delta";

    /// <summary>The engine version recorded for matrix gap analysis; bump it when its output changes.</summary>
    public static readonly EngineVersion MatrixGapEngine = new("protocol.matrix_gap", "1");

    /// <summary>The section the matrix gaps are reported in, and masked as.</summary>
    public static readonly string MatrixGapsPart = "matrix_gaps";

    /// <summary>The engine version recorded for the expected schedule (K-TIME-7); bump it when its output changes.</summary>
    public static readonly EngineVersion ScheduleEngine = new("protocol.expected_schedule", "1");

    /// <summary>The engine version recorded when a result carries dependents.</summary>
    public static readonly EngineVersion DependentsEngine = new("lineage.dependents", "2");

    private const int MaxReportedProblems = 10;

    private readonly IMappingEngine _mappingEngine = mappingEngine ?? new MappingEngine();
    private readonly IRuleEvaluator _ruleEvaluator = ruleEvaluator ?? new DotNetRuleEvaluator();
    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? new BlindedTreatmentClassifier();

    public async Task<CommandResult<SimulationResult>> SimulateAsync(
        TenantId tenant, SimulationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("Simulation runs within a tenant.", nameof(tenant));
        }

        if (currentActor.Actor is not { } actor)
        {
            return KernelErrors.ActorUnresolved;
        }

        var now = clock.UtcNow;
        var asOf = new SimulationAsOf(
            KernelTimestamps.ToStoredPrecision(request.ValidAt ?? now),
            KernelTimestamps.ToStoredPrecision(request.KnownAt ?? now));

        return request.Candidate.Kind switch
        {
            CandidateKind.ProtocolVersion => await SimulateProtocolAsync(tenant, actor, request.Candidate, asOf, cancellationToken).ConfigureAwait(false),
            CandidateKind.MappingSpecification => await SimulateMappingSpecificationAsync(tenant, actor, request.Candidate, asOf, cancellationToken).ConfigureAwait(false),
            CandidateKind.RulePack => await SimulateRulePackAsync(tenant, actor, request.Candidate, asOf, cancellationToken).ConfigureAwait(false),
            _ => SimulationErrors.ContentInvalid,
        };
    }

    private async Task<CommandResult<SimulationResult>> SimulateProtocolAsync(
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadProtocolAsync(tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candidate, baseVersion) = loaded.Value;
        var scope = NodeRef.For(candidate.StudyId);
        if (await AuthorizeRunAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var versionRef = candidate.Id is { } storedId ? NodeRef.For(storedId, candidate.VersionIndex) : null;
        var baseRef = baseVersion is null ? null : NodeRef.For(baseVersion.Id, baseVersion.VersionIndex);
        var topLevelMask = await CheckTopLevelMaskAsync(tenant, actor, [scope, versionRef, baseRef], cancellationToken).ConfigureAwait(false);

        if (topLevelMask is { } reason)
        {
            var maskedEngines = new List<EngineVersion> { ProtocolStructuralDiff.Engine, ScheduleEngine };
            maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var maskedSummary = new CandidateSummary(CandidateKind.ProtocolVersion, null, null, null, null);
            IReadOnlyList<MaskedPart> withheld =
            [
                new MaskedPart(DependentsPart, reason),
                new MaskedPart(MatrixGapsPart, reason),
                new MaskedPart(ScheduleChangesPart, reason),
                new MaskedPart(StructuralDiffPart, reason),
            ];
            return SealResult(
                scope,
                asOf,
                maskedSummary,
                maskedEngines,
                visibleStateDigest: null,
                diff: [],
                schedule: [],
                withheld,
                dependents: null,
                matrixGaps: [],
                operationalImpact: null,
                projectionChanges: null,
                validationDelta: null);
        }

        var compared = ProtocolStructuralDiff.Compare(baseVersion?.Content, candidate.Content);
        if (compared.Error is { } diffError)
        {
            return diffError;
        }

        var summary = new CandidateSummary(
            CandidateKind.ProtocolVersion, versionRef, baseRef, candidate.ContentHash, baseVersion?.ContentHash);
        var diff = compared.Value!;
        var schedule = ScheduleChanges(baseVersion?.Content, candidate.Content);
        var matrixGaps = EvaluateMatrixGaps(candidate.Content);
        var engines = new List<EngineVersion> { ProtocolStructuralDiff.Engine, ScheduleEngine, MatrixGapEngine };

        DependentsReport? dependents = null;
        if (diff.Count > 0 && baseVersion is not null)
        {
            var impacted = ProtocolElementImpact.Impacted(baseVersion.StudyId, diff);
            dependents = await inputs.DependentsAsync(tenant, actor, baseVersion.Id, candidate.Id, impacted, asOf, cancellationToken)
                .ConfigureAwait(false);
            engines.Add(DependentsEngine);
        }

        var rawSnapshot = await inputs.LoadStudySnapshotAsync(tenant, candidate.StudyId, asOf, cancellationToken).ConfigureAwait(false);
        if (!rawSnapshot.Loaded)
        {
            engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return SealResult(
                scope,
                asOf,
                summary,
                engines,
                visibleStateDigest: null,
                diff,
                schedule,
                masked: [],
                dependents,
                matrixGaps,
                operationalImpact: null,
                projectionChanges: null,
                validationDelta: null);
        }

        var baseDesign = baseVersion is null ? null : ProtocolDesignDocument.Read(baseVersion.Content).Design;
        var candidateDesign = ProtocolDesignDocument.Read(candidate.Content).Design;
        var extraDesigns = new[] { baseDesign, candidateDesign }.OfType<ProtocolDesign>().ToList();
        var maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
            tenant,
            candidate.StudyId,
            actor,
            rawSnapshot,
            extraDesigns,
            visibility,
            _blinding,
            probe,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MaskReason> dependentMaskReasons = [];
        if (dependents is not null)
        {
            (dependents, dependentMaskReasons) = SimulationSnapshotMasking.OmitMaskedDependents(dependents);
        }

        var pinnedSpecs = ParsePinnedMappingSpecs(maskedSnapshot.PinnedLibraryItems);
        var pinnedPacks = ParsePinnedRulePacks(maskedSnapshot.PinnedRulePacks);

        var (projectionChanges, baseProjected, candProjected, projImpacted) =
            SimulationProjectionAndValidationEvaluator.EvaluateProjections(
                _mappingEngine,
                maskedSnapshot,
                pinnedSpecs,
                pinnedSpecs,
                baseDesign,
                candidateDesign);
        engines.Add(SimulationProjectionAndValidationEvaluator.MappingEngineVersion);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var (validationDelta, ruleImpacted, maskedBlindedFinding) =
            SimulationProjectionAndValidationEvaluator.EvaluateValidationDelta(
                _ruleEvaluator,
                _blinding,
                maskedSnapshot,
                asOfDate,
                pinnedPacks,
                pinnedPacks,
                baseDesign,
                candidateDesign,
                baseProjected,
                candProjected);
        engines.Add(SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion);

        var combinedImpacted = new HashSet<EnrollmentId>(projImpacted);
        combinedImpacted.UnionWith(ruleImpacted);

        var soaDiff = candidateDesign is not null
            ? ScheduleOfActivitiesDiff.Compare(baseDesign, candidateDesign)
            : null;
        var mergedHidden = SimulationSnapshotMasking.MergeHidden(
            maskedSnapshot.Hidden,
            dependentMaskReasons.Concat(maskedBlindedFinding ? [MaskReason.Blinded] : []));
        var operationalSnapshot = maskedSnapshot with { Hidden = mergedHidden };
        var operational = SimulationOperationalEvaluator.Evaluate(
            operationalSnapshot,
            asOf,
            baseDesign,
            candidateDesign,
            candidate.Amendment,
            soaDiff,
            combinedImpacted);
        engines.Add(SimulationOperationalEvaluator.Engine);

        var maskedParts = CombineMaskedParts(maskedSnapshot.MaskedParts, dependentMaskReasons, maskedBlindedFinding);
        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return SealResult(
            scope,
            asOf,
            summary,
            engines,
            maskedSnapshot.VisibleStateDigest,
            diff,
            schedule,
            maskedParts,
            dependents,
            matrixGaps,
            operational,
            projectionChanges,
            validationDelta);
    }

    private async Task<CommandResult<SimulationResult>> SimulateMappingSpecificationAsync(
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var studyId = candidateRequest.StudyId!.Value;
        if (!await inputs.StudyExistsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false))
        {
            return SimulationErrors.StudyUnknown;
        }

        var loaded = await LoadMappingCandidateAsync(tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candSpec, candRef, candHash, explicitBaseItem, explicitBaseSpec) = loaded.Value;
        var scope = NodeRef.For(studyId);
        if (await AuthorizeRunAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var explicitBaseRef = explicitBaseItem is null ? null : NodeRefForItem(explicitBaseItem);
        var mappingMaskedEngines = new[]
        {
            SimulationSpecificationDiff.MappingDiffEngine,
            SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
        };
        var mappingWithheldParts = new[] { DependentsPart, ProjectionChangesPart, StructuralDiffPart };
        var topLevelMask = await CheckTopLevelMaskAsync(tenant, actor, [scope, candRef, explicitBaseRef], cancellationToken).ConfigureAwait(false);
        if (topLevelMask is { } reason)
        {
            return SealMaskedCandidateResult(
                scope,
                asOf,
                CandidateKind.MappingSpecification,
                mappingMaskedEngines,
                mappingWithheldParts,
                reason);
        }

        var rawSnapshot = await inputs.LoadStudySnapshotAsync(tenant, studyId, asOf, cancellationToken).ConfigureAwait(false);
        var maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
            tenant,
            studyId,
            actor,
            rawSnapshot,
            extraDesigns: [],
            visibility,
            _blinding,
            probe,
            cancellationToken).ConfigureAwait(false);

        var (baseItem, baseSpec) = ResolveMappingBase(candSpec, explicitBaseItem, explicitBaseSpec, maskedSnapshot.PinnedLibraryItems);
        var baseRef = baseItem is null ? null : NodeRefForItem(baseItem);

        if (baseRef is not null && explicitBaseRef is null)
        {
            var inferredBaseMask = await CheckTopLevelMaskAsync(tenant, actor, [baseRef], cancellationToken).ConfigureAwait(false);
            if (inferredBaseMask is { } baseReason)
            {
                return SealMaskedCandidateResult(
                    scope,
                    asOf,
                    CandidateKind.MappingSpecification,
                    mappingMaskedEngines,
                    mappingWithheldParts,
                    baseReason);
            }
        }

        var diff = SimulationSpecificationDiff.CompareMappingSpecifications(baseSpec, candSpec);
        return await EvaluateLibraryCandidateImpactAsync(
            tenant,
            actor,
            scope,
            asOf,
            CandidateKind.MappingSpecification,
            candRef,
            candHash,
            baseItem,
            baseRef,
            diff,
            SimulationSpecificationDiff.MappingDiffEngine,
            maskedSnapshot,
            snap =>
            {
                var (baselineSpecs, candidateSpecs) = BuildMappingSpecStacks(snap.PinnedLibraryItems, baseItem, baseSpec, candSpec);
                var pinnedPacks = ParsePinnedRulePacks(snap.PinnedRulePacks);
                return (baselineSpecs, candidateSpecs, pinnedPacks, pinnedPacks);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResult<SimulationResult>> SimulateRulePackAsync(
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var studyId = candidateRequest.StudyId!.Value;
        if (!await inputs.StudyExistsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false))
        {
            return SimulationErrors.StudyUnknown;
        }

        var loaded = await LoadRulePackCandidateAsync(tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candPack, candItem, candRef, candHash, explicitBaseItem, explicitBasePack) = loaded.Value;
        var scope = NodeRef.For(studyId);
        if (await AuthorizeRunAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var explicitBaseRef = explicitBaseItem is null ? null : NodeRefForItem(explicitBaseItem);
        var rulePackMaskedEngines = new[]
        {
            SimulationSpecificationDiff.RulePackDiffEngine,
            SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
        };
        var rulePackWithheldParts = new[] { DependentsPart, StructuralDiffPart, ValidationDeltaPart };
        var topLevelMask = await CheckTopLevelMaskAsync(tenant, actor, [scope, candRef, explicitBaseRef], cancellationToken).ConfigureAwait(false);
        if (topLevelMask is { } reason)
        {
            return SealMaskedCandidateResult(
                scope,
                asOf,
                CandidateKind.RulePack,
                rulePackMaskedEngines,
                rulePackWithheldParts,
                reason);
        }

        var rawSnapshot = await inputs.LoadStudySnapshotAsync(tenant, studyId, asOf, cancellationToken).ConfigureAwait(false);
        var maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
            tenant,
            studyId,
            actor,
            rawSnapshot,
            extraDesigns: [],
            visibility,
            _blinding,
            probe,
            cancellationToken).ConfigureAwait(false);

        var (baseItem, basePack) = ResolveRulePackBase(candPack, candItem, explicitBaseItem, explicitBasePack, maskedSnapshot.PinnedRulePacks);
        var baseRef = baseItem is null ? null : NodeRefForItem(baseItem);

        if (baseRef is not null && explicitBaseRef is null)
        {
            var inferredBaseMask = await CheckTopLevelMaskAsync(tenant, actor, [baseRef], cancellationToken).ConfigureAwait(false);
            if (inferredBaseMask is { } baseReason)
            {
                return SealMaskedCandidateResult(
                    scope,
                    asOf,
                    CandidateKind.RulePack,
                    rulePackMaskedEngines,
                    rulePackWithheldParts,
                    baseReason);
            }
        }

        var diff = SimulationSpecificationDiff.CompareRulePacks(basePack, candPack);
        return await EvaluateLibraryCandidateImpactAsync(
            tenant,
            actor,
            scope,
            asOf,
            CandidateKind.RulePack,
            candRef,
            candHash,
            baseItem,
            baseRef,
            diff,
            SimulationSpecificationDiff.RulePackDiffEngine,
            maskedSnapshot,
            snap =>
            {
                var pinnedSpecs = ParsePinnedMappingSpecs(snap.PinnedLibraryItems);
                var (baselinePacks, candidatePacks) = BuildRulePackStacks(snap.PinnedRulePacks, baseItem, basePack, candPack);
                return (pinnedSpecs, pinnedSpecs, baselinePacks, candidatePacks);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static CommandResult<SimulationResult> SealMaskedCandidateResult(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateKind kind,
        IReadOnlyList<EngineVersion> rawEngines,
        IReadOnlyList<string> withheldPartNames,
        MaskReason reason)
    {
        var maskedEngines = rawEngines.ToList();
        maskedEngines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        IReadOnlyList<MaskedPart> withheld = [.. withheldPartNames.Select(part => new MaskedPart(part, reason))];
        return SealResult(
            scope,
            asOf,
            new CandidateSummary(kind, null, null, null, null),
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

    private async Task<CommandResult<SimulationResult>> EvaluateLibraryCandidateImpactAsync(
        TenantId tenant,
        Actor actor,
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateKind kind,
        NodeRef? candRef,
        string candHash,
        LibraryItemView? baseItem,
        NodeRef? baseRef,
        IReadOnlyList<ElementChange> diff,
        EngineVersion diffEngine,
        MaskedSimulationSnapshot maskedSnapshot,
        Func<MaskedSimulationSnapshot, (IReadOnlyList<MappingSpecification> BaselineSpecs, IReadOnlyList<MappingSpecification> CandidateSpecs, IReadOnlyList<RulePack> BaselinePacks, IReadOnlyList<RulePack> CandidatePacks)> buildStacks,
        CancellationToken cancellationToken)
    {
        var summary = new CandidateSummary(
            kind,
            candRef,
            baseRef,
            candHash,
            baseItem?.ContentHash);
        var engines = new List<EngineVersion>
        {
            diffEngine,
            SimulationProjectionAndValidationEvaluator.MappingEngineVersion,
            SimulationProjectionAndValidationEvaluator.RuleEvaluatorVersion,
            SimulationOperationalEvaluator.Engine,
        };

        DependentsReport? dependents = null;
        IReadOnlyList<MaskReason> dependentMaskReasons = [];
        if (diff.Count > 0 && baseItem is not null)
        {
            dependents = await inputs.DependentsOfNodeAsync(
                tenant,
                actor,
                NodeRef.For(baseItem.Id),
                candRef?.Unversioned(),
                asOf,
                cancellationToken).ConfigureAwait(false);
            (dependents, dependentMaskReasons) = SimulationSnapshotMasking.OmitMaskedDependents(dependents);
            engines.Add(DependentsEngine);
        }

        var (baselineSpecs, candidateSpecs, baselinePacks, candidatePacks) = buildStacks(maskedSnapshot);
        var activeDesign = SelectActiveProtocolDesign(maskedSnapshot);

        var (projectionChanges, baseProjected, candProjected, projImpacted) =
            SimulationProjectionAndValidationEvaluator.EvaluateProjections(
                _mappingEngine,
                maskedSnapshot,
                baselineSpecs,
                candidateSpecs,
                activeDesign,
                activeDesign);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var (validationDelta, ruleImpacted, maskedBlindedFinding) =
            SimulationProjectionAndValidationEvaluator.EvaluateValidationDelta(
                _ruleEvaluator,
                _blinding,
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

        var maskedParts = CombineMaskedParts(maskedSnapshot.MaskedParts, dependentMaskReasons, maskedBlindedFinding);
        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return SealResult(
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

    private async Task<KernelError?> AuthorizeRunAsync(TenantId tenant, NodeRef scope, CancellationToken cancellationToken)
    {
        var decision = await probe.CanIAsync(tenant, SimulationCapabilities.Run, scope, cancellationToken).ConfigureAwait(false);
        if (decision.Permitted)
        {
            return null;
        }

        return KernelErrors.AuthorizationDenied.WithMessage(decision.Reason ?? KernelErrors.AuthorizationDenied.Message)
            with
        { Explanation = decision.Explain(SimulationCapabilities.Run.Token, scope) };
    }

    private async Task<MaskReason?> CheckTopLevelMaskAsync(
        TenantId tenant,
        Actor actor,
        IReadOnlyList<NodeRef?> nodes,
        CancellationToken cancellationToken)
    {
        var nonNull = nodes.OfType<NodeRef>().Distinct().ToArray();
        if (nonNull.Length == 0)
        {
            return null;
        }

        var hidden = await visibility.MaskAsync(tenant, actor, nonNull, cancellationToken).ConfigureAwait(false);
        foreach (var node in nonNull)
        {
            if (hidden.TryGetValue(node, out var reason))
            {
                return reason;
            }
        }

        return null;
    }

    private static CommandResult<SimulationResult> SealResult(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateSummary summary,
        IReadOnlyList<EngineVersion> engines,
        string? visibleStateDigest,
        IReadOnlyList<ElementChange> diff,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyList<MaskedPart> masked,
        DependentsReport? dependents,
        IReadOnlyList<MatrixGapFinding>? matrixGaps,
        OperationalImpactSummary? operationalImpact,
        IReadOnlyList<ProjectionDatasetChange>? projectionChanges,
        ValidationDeltaSummary? validationDelta)
    {
        var inputHash = ComputeInputHash(scope, asOf, summary, engines, masked, visibleStateDigest);
        var preliminary = new SimulationResult(
            scope,
            asOf,
            summary,
            engines,
            inputHash,
            string.Empty,
            diff,
            schedule,
            masked,
            dependents,
            matrixGaps,
            operationalImpact,
            projectionChanges,
            validationDelta);
        var resultHash = SimulationResultHashing.ComputeResultHash(preliminary);
        return CommandResult<SimulationResult>.Success(preliminary with { ResultHash = resultHash });
    }

    private static IReadOnlyList<MaskedPart> CombineMaskedParts(
        IReadOnlyList<MaskedPart> snapshotParts,
        IReadOnlyList<MaskReason> dependentMaskReasons,
        bool maskedBlindedFinding)
    {
        var set = new SortedSet<(string Part, MaskReason Reason)>(
            Comparer<(string Part, MaskReason Reason)>.Create((a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Part, b.Part);
                return cmp != 0 ? cmp : a.Reason.CompareTo(b.Reason);
            }));

        foreach (var part in snapshotParts)
        {
            set.Add((part.Part, part.Reason));
        }

        foreach (var depReason in dependentMaskReasons)
        {
            set.Add((DependentsPart, depReason));
        }

        if (maskedBlindedFinding)
        {
            set.Add((SimulationSnapshotMasking.BlindedTreatmentPart, MaskReason.Blinded));
        }

        return [.. set.Select(x => new MaskedPart(x.Part, x.Reason))];
    }

    private static IReadOnlyList<ScheduleChange> ScheduleChanges(string? baseContent, string candidateContent)
    {
        var before = baseContent is null ? [] : ExpectedSchedule.Plan(DesignOf(baseContent));
        var after = ExpectedSchedule.Plan(DesignOf(candidateContent));
        var beforeById = before.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var changes = new List<ScheduleChange>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).Order(StringComparer.Ordinal))
        {
            var old = beforeById.GetValueOrDefault(id);
            var @new = afterById.GetValueOrDefault(id);
            if (old is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Added, null, @new));
            }
            else if (@new is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Removed, old, null));
            }
            else if (old.EpochId != @new.EpochId
                || ExpectedSchedule.NormalizeStudyDay(old.StudyDay) != ExpectedSchedule.NormalizeStudyDay(@new.StudyDay)
                || old.WindowBefore != @new.WindowBefore
                || old.WindowAfter != @new.WindowAfter)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Changed, old, @new));
            }
        }

        return changes;

        static ProtocolDesign DesignOf(string content) =>
            ProtocolDesignDocument.Read(content).Design
            ?? throw new InvalidOperationException("Simulation compared content that is not a protocol design.");
    }

    private async Task<CommandResult<(ProtocolCandidate Candidate, ProtocolVersionSnapshot? Base)>> LoadProtocolAsync(
        TenantId tenant, SimulationCandidate request, CancellationToken cancellationToken)
    {
        if (request.VersionId is { } versionId)
        {
            var stored = await inputs.FindProtocolVersionAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return SimulationErrors.VersionUnknown;
            }

            if (request.Version is { } expectedVersion && stored.VersionIndex != expectedVersion)
            {
                return SimulationErrors.VersionStale.WithMessage(
                    $"The protocol version is at version {stored.VersionIndex}, not {expectedVersion}; reload and simulate the current version.");
            }

            ProtocolVersionSnapshot? parent = null;
            if (stored.ParentVersionId is { } parentId)
            {
                parent = await inputs.FindProtocolVersionAsync(tenant, parentId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    return SimulationErrors.VersionUnknown.WithMessage("The protocol version's parent does not exist.");
                }
            }

            var hash = stored.ContentHash ?? ContentHash.Compute(stored.Content);
            return CommandResult<(ProtocolCandidate, ProtocolVersionSnapshot?)>.Success(
                (new ProtocolCandidate(stored.Id, stored.StudyId, stored.Content, hash, stored.VersionIndex, stored.Amendment), parent));
        }

        var baseId = request.BaseVersionId!.Value;
        var baseVersion = await inputs.FindProtocolVersionAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
        if (baseVersion is null)
        {
            return SimulationErrors.VersionUnknown;
        }

        if (request.BaseVersion is { } expectedBaseVersion && baseVersion.VersionIndex != expectedBaseVersion)
        {
            return SimulationErrors.VersionStale.WithMessage(
                $"The base protocol version is at version {baseVersion.VersionIndex}, not {expectedBaseVersion}; reload and simulate against the current version.");
        }

        if (baseVersion.ContentHash is null)
        {
            return SimulationErrors.BaseNotFrozen;
        }

        var read = ProtocolDesignDocument.Read(request.Content!);
        if (!read.IsValid)
        {
            var more = read.Problems.Count > MaxReportedProblems ? $" (and {read.Problems.Count - MaxReportedProblems} more)" : string.Empty;
            return SimulationErrors.ContentInvalid.WithMessage(
                $"{SimulationErrors.ContentInvalid.Message} {string.Join(" ", read.Problems.Take(MaxReportedProblems))}{more}");
        }

        return CommandResult<(ProtocolCandidate, ProtocolVersionSnapshot?)>.Success(
            (new ProtocolCandidate(null, baseVersion.StudyId, request.Content!, ContentHash.Compute(request.Content!), 0, null), baseVersion));
    }

    private async Task<CommandResult<(
        MappingSpecification Candidate,
        NodeRef? CandidateRef,
        string CandidateHash,
        LibraryItemView? ExplicitBaseItem,
        MappingSpecification? ExplicitBaseSpec)>> LoadMappingCandidateAsync(
        TenantId tenant,
        SimulationCandidate request,
        CancellationToken cancellationToken)
    {
        LibraryItemView? explicitBaseItem = null;
        MappingSpecification? explicitBaseSpec = null;
        if (request.BaseLibraryItemId is { } baseId)
        {
            explicitBaseItem = await inputs.FindLibraryItemAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
            if (explicitBaseItem is null || explicitBaseItem.Kind != LibraryItemKind.MappingSpecification)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            if (request.LibraryItemId is null && explicitBaseItem.Status == GovernanceStatus.Draft)
            {
                return SimulationErrors.BaseNotFrozen;
            }

            var (parsedBase, baseProblems) = MappingSpecificationJson.Read(explicitBaseItem.Content);
            if (parsedBase is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, baseProblems);
            }

            explicitBaseSpec = parsedBase;
        }

        if (request.LibraryItemId is { } itemId)
        {
            var item = await inputs.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
            if (item is null || item.Kind != LibraryItemKind.MappingSpecification)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            var (parsed, problems) = MappingSpecificationJson.Read(item.Content);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, problems);
            }

            return CommandResult<(MappingSpecification, NodeRef?, string, LibraryItemView?, MappingSpecification?)>.Success(
                (parsed, NodeRefForItem(item), item.ContentHash, explicitBaseItem, explicitBaseSpec));
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.Content!);
        }
        catch (JsonException ex)
        {
            return SimulationErrors.MappingSpecificationInvalid.WithMessage($"{SimulationErrors.MappingSpecificationInvalid.Message} {ex.Message}");
        }

        using (doc)
        {
            var (parsed, problems) = MappingSpecificationJson.Read(doc.RootElement);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, problems);
            }

            return CommandResult<(MappingSpecification, NodeRef?, string, LibraryItemView?, MappingSpecification?)>.Success(
                (parsed, null, ContentHash.Compute(request.Content!), explicitBaseItem, explicitBaseSpec));
        }
    }

    private async Task<CommandResult<(
        RulePack Candidate,
        LibraryItemView? CandidateItem,
        NodeRef? CandidateRef,
        string CandidateHash,
        LibraryItemView? ExplicitBaseItem,
        RulePack? ExplicitBasePack)>> LoadRulePackCandidateAsync(
        TenantId tenant,
        SimulationCandidate request,
        CancellationToken cancellationToken)
    {
        LibraryItemView? explicitBaseItem = null;
        RulePack? explicitBasePack = null;
        if (request.BaseLibraryItemId is { } baseId)
        {
            explicitBaseItem = await inputs.FindLibraryItemAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
            if (explicitBaseItem is null || explicitBaseItem.Kind != LibraryItemKind.RulePack)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            if (request.LibraryItemId is null && explicitBaseItem.Status == GovernanceStatus.Draft)
            {
                return SimulationErrors.BaseNotFrozen;
            }

            var (parsedBase, baseProblems) = RulePackJson.Read(explicitBaseItem.Content);
            if (parsedBase is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, baseProblems);
            }

            explicitBasePack = parsedBase;
        }

        if (request.LibraryItemId is { } itemId)
        {
            var item = await inputs.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
            if (item is null || item.Kind != LibraryItemKind.RulePack)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            var (parsed, problems) = RulePackJson.Read(item.Content);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, problems);
            }

            return CommandResult<(RulePack, LibraryItemView?, NodeRef?, string, LibraryItemView?, RulePack?)>.Success(
                (parsed, item, NodeRefForItem(item), item.ContentHash, explicitBaseItem, explicitBasePack));
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.Content!);
        }
        catch (JsonException ex)
        {
            return SimulationErrors.RulePackInvalid.WithMessage($"{SimulationErrors.RulePackInvalid.Message} {ex.Message}");
        }

        using (doc)
        {
            var (parsed, problems) = RulePackJson.Read(doc.RootElement);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, problems);
            }

            return CommandResult<(RulePack, LibraryItemView?, NodeRef?, string, LibraryItemView?, RulePack?)>.Success(
                (parsed, null, null, ContentHash.Compute(request.Content!), explicitBaseItem, explicitBasePack));
        }
    }

    private static (LibraryItemView? BaseItem, MappingSpecification? BaseSpec) ResolveMappingBase(
        MappingSpecification candidate,
        LibraryItemView? explicitBaseItem,
        MappingSpecification? explicitBaseSpec,
        IReadOnlyList<LibraryItemView> pinnedItems)
    {
        if (explicitBaseItem is not null && explicitBaseSpec is not null)
        {
            return (explicitBaseItem, explicitBaseSpec);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Overrides))
        {
            var overridden = pinnedItems.FirstOrDefault(i =>
                i.Kind == LibraryItemKind.MappingSpecification
                && string.Equals(i.Key, candidate.Overrides, StringComparison.Ordinal));
            if (overridden is not null && MappingSpecificationJson.Read(overridden.Content).Specification is { } parsedOverridden)
            {
                return (overridden, parsedOverridden);
            }
        }

        var firstPinned = pinnedItems.FirstOrDefault(i => i.Kind == LibraryItemKind.MappingSpecification);
        if (firstPinned is not null && MappingSpecificationJson.Read(firstPinned.Content).Specification is { } parsedFirst)
        {
            return (firstPinned, parsedFirst);
        }

        return (null, null);
    }

    private static (LibraryItemView? BaseItem, RulePack? BasePack) ResolveRulePackBase(
        RulePack candidate,
        LibraryItemView? candidateItem,
        LibraryItemView? explicitBaseItem,
        RulePack? explicitBasePack,
        IReadOnlyList<LibraryItemView> pinnedPacks)
    {
        if (explicitBaseItem is not null && explicitBasePack is not null)
        {
            return (explicitBaseItem, explicitBasePack);
        }

        LibraryItemView? matching = null;
        if (candidateItem is not null)
        {
            matching = pinnedPacks.FirstOrDefault(p =>
                p.Id != candidateItem.Id && string.Equals(p.Key, candidateItem.Key, StringComparison.Ordinal));
        }

        matching ??= pinnedPacks.FirstOrDefault(p => string.Equals(p.Title, candidate.Title, StringComparison.Ordinal))
            ?? pinnedPacks.FirstOrDefault();

        if (matching is not null && RulePackJson.Read(matching.Content).Pack is { } parsed)
        {
            return (matching, parsed);
        }

        return (null, null);
    }

    private static (IReadOnlyList<MappingSpecification> Baseline, IReadOnlyList<MappingSpecification> Candidate) BuildMappingSpecStacks(
        IReadOnlyList<LibraryItemView> pinnedItems,
        LibraryItemView? baseItem,
        MappingSpecification? baseSpec,
        MappingSpecification candidateSpec)
    {
        var pinned = new List<(LibraryItemView Item, MappingSpecification Spec)>();
        foreach (var item in pinnedItems.Where(i => i.Kind == LibraryItemKind.MappingSpecification))
        {
            if (MappingSpecificationJson.Read(item.Content).Specification is { } parsed)
            {
                pinned.Add((item, parsed));
            }
        }

        pinned = [.. pinned
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Spec.Overrides) ? 0 : 1)
            .ThenBy(x => x.Item.Key, StringComparer.Ordinal)];

        if (pinned.Count == 0)
        {
            IReadOnlyList<MappingSpecification> baselineOnly = baseSpec is null ? [] : [baseSpec];
            IReadOnlyList<MappingSpecification> candidateOnly = baseSpec is not null && !string.IsNullOrWhiteSpace(candidateSpec.Overrides)
                ? [baseSpec, candidateSpec]
                : [candidateSpec];
            return (baselineOnly, candidateOnly);
        }

        var baselineList = pinned.Select(p => p.Spec).ToList();
        if (baseItem is not null && baseSpec is not null && pinned.All(p => p.Item.Id != baseItem.Id))
        {
            baselineList.Add(baseSpec);
        }

        var candidateList = new List<MappingSpecification>(pinned.Count + 1);
        var replaced = false;
        foreach (var (item, spec) in pinned)
        {
            if (baseItem is not null && item.Id == baseItem.Id && string.IsNullOrWhiteSpace(candidateSpec.Overrides))
            {
                candidateList.Add(candidateSpec);
                replaced = true;
            }
            else
            {
                candidateList.Add(spec);
            }
        }

        if (!replaced)
        {
            candidateList.Add(candidateSpec);
        }

        return (baselineList, candidateList);
    }

    private static (IReadOnlyList<RulePack> Baseline, IReadOnlyList<RulePack> Candidate) BuildRulePackStacks(
        IReadOnlyList<LibraryItemView> pinnedPackItems,
        LibraryItemView? baseItem,
        RulePack? basePack,
        RulePack candidatePack)
    {
        var pinned = new List<(LibraryItemView Item, RulePack Pack)>();
        foreach (var item in pinnedPackItems.Where(i => i.Kind == LibraryItemKind.RulePack))
        {
            if (RulePackJson.Read(item.Content).Pack is { } parsed)
            {
                pinned.Add((item, parsed));
            }
        }

        if (pinned.Count == 0)
        {
            IReadOnlyList<RulePack> baselineOnly = basePack is null ? [] : [basePack];
            return (baselineOnly, [candidatePack]);
        }

        var baselineList = pinned.Select(p => p.Pack).ToList();
        if (baseItem is not null && basePack is not null && pinned.All(p => p.Item.Id != baseItem.Id))
        {
            baselineList.Add(basePack);
        }

        var candidateList = new List<RulePack>(pinned.Count + 1);
        var replaced = false;
        foreach (var (item, pack) in pinned)
        {
            if (baseItem is not null && item.Id == baseItem.Id)
            {
                candidateList.Add(candidatePack);
                replaced = true;
            }
            else
            {
                candidateList.Add(pack);
            }
        }

        if (!replaced)
        {
            candidateList.Add(candidatePack);
        }

        return (baselineList, candidateList);
    }

    private static IReadOnlyList<MappingSpecification> ParsePinnedMappingSpecs(IReadOnlyList<LibraryItemView> items) =>
        [.. items
            .Where(i => i.Kind == LibraryItemKind.MappingSpecification)
            .Select(i => (Item: i, Spec: MappingSpecificationJson.Read(i.Content).Specification))
            .Where(x => x.Spec is not null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Spec!.Overrides) ? 0 : 1)
            .ThenBy(x => x.Item.Key, StringComparer.Ordinal)
            .Select(x => x.Spec!)];

    private static IReadOnlyList<RulePack> ParsePinnedRulePacks(IReadOnlyList<LibraryItemView> items) =>
        [.. items
            .Where(i => i.Kind == LibraryItemKind.RulePack)
            .Select(i => RulePackJson.Read(i.Content).Pack)
            .OfType<RulePack>()];

    private static ProtocolDesign? SelectActiveProtocolDesign(MaskedSimulationSnapshot snapshot)
    {
        var effectiveVersionIds = snapshot.VisibleEffectivities
            .Where(e => e.StatusAsOf == EffectivityStatus.Effective)
            .Select(e => e.ProtocolVersionId)
            .ToHashSet();

        foreach (var id in effectiveVersionIds.OrderBy(x => x.Value))
        {
            if (snapshot.ProtocolDesigns.TryGetValue(id, out var effectiveDesign))
            {
                return effectiveDesign;
            }
        }

        return snapshot.ProtocolDesigns
            .OrderBy(kv => kv.Key.Value)
            .Select(kv => kv.Value)
            .LastOrDefault();
    }

    private static KernelError FormatInvalid(KernelError error, IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
        {
            return error;
        }

        var more = problems.Count > MaxReportedProblems ? $" (and {problems.Count - MaxReportedProblems} more)" : string.Empty;
        return error.WithMessage($"{error.Message} {string.Join(" ", problems.Take(MaxReportedProblems))}{more}");
    }

    private static string ComputeInputHash(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateSummary candidate,
        IReadOnlyList<EngineVersion> engines,
        IReadOnlyList<MaskedPart> masked,
        string? visibleStateDigest) =>
        ContentHash.ComputeStreaming(writer =>
        {
            writer.WriteStartObject();

            writer.WritePropertyName("as_of"u8);
            writer.WriteStartObject();
            writer.WriteString("known_at"u8, asOf.KnownAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("valid_at"u8, asOf.ValidAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();

            writer.WritePropertyName("candidate"u8);
            writer.WriteStartObject();
            writer.WriteString("base_content_hash"u8, candidate.BaseContentHash);
            writer.WriteString("base_version"u8, candidate.BaseVersion?.ToString());
            writer.WriteString("content_hash"u8, candidate.ContentHash);
            writer.WriteString("kind"u8, candidate.Kind.ToString());
            writer.WriteString("version"u8, candidate.Version?.ToString());
            writer.WriteEndObject();

            writer.WritePropertyName("engines"u8);
            writer.WriteStartArray();
            for (var i = 0; i < engines.Count; i++)
            {
                var engine = engines[i];
                writer.WriteStartObject();
                writer.WriteString("name"u8, engine.Name);
                writer.WriteString("version"u8, engine.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("masked"u8);
            writer.WriteStartArray();
            for (var i = 0; i < masked.Count; i++)
            {
                var part = masked[i];
                writer.WriteStartObject();
                writer.WriteString("part"u8, part.Part);
                writer.WriteString("reason"u8, part.Reason.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteString("scope"u8, scope.ToString());
            if (visibleStateDigest is not null)
            {
                writer.WriteString("visible_state_digest"u8, visibleStateDigest);
            }

            writer.WriteEndObject();
        });

    private static IReadOnlyList<MatrixGapFinding> EvaluateMatrixGaps(string content)
    {
        var read = ProtocolDesignDocument.Read(content);
        return read.Design is { } design ? MatrixGapAnalyzer.Analyze(design) : [];
    }

    private static NodeRef NodeRefForItem(LibraryItemView item) =>
        item.VersionIndex >= 1 ? NodeRef.For(item.Id, item.VersionIndex) : NodeRef.For(item.Id);

    private sealed record ProtocolCandidate(
        ProtocolVersionId? Id,
        StudyId StudyId,
        string Content,
        string ContentHash,
        long VersionIndex,
        ProtocolAmendment? Amendment);
}

/// <summary>Registers the simulation engine.</summary>
public static class SimulationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISimulationEngine"/>. The host also registers kernel persistence's inputs
    /// (<c>AddKernelSimulationInputs()</c>), <see cref="IAuthorizationProbe"/> (<c>AddKernelAuthorization()</c>) and an
    /// <see cref="INodeVisibility"/>.
    /// </summary>
    public static IServiceCollection AddKernelSimulation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBlindedTreatmentClassifier, BlindedTreatmentClassifier>();
        services.AddScoped<ISimulationEngine, SimulationEngine>();
        return services;
    }
}

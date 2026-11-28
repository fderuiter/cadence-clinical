using System;
using System.Collections.Generic;
using System.Linq;
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
using Cadence.Rules.Evaluation;
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

    private readonly IMappingEngine _mappingEngine = mappingEngine ?? new MappingEngine();
    private readonly IRuleEvaluator _ruleEvaluator = ruleEvaluator ?? new DotNetRuleEvaluator();
    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? new BlindedTreatmentClassifier();

    internal ISimulationInputs Inputs => inputs;

    internal IAuthorizationProbe Probe => probe;

    internal INodeVisibility Visibility => visibility;

    internal IMappingEngine MappingEngine => _mappingEngine;

    internal IRuleEvaluator RuleEvaluator => _ruleEvaluator;

    internal IBlindedTreatmentClassifier Blinding => _blinding;

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
            CandidateKind.MappingSpecification => await SimulationLibraryCandidateRunner.SimulateMappingSpecificationAsync(this, tenant, actor, request.Candidate, asOf, cancellationToken).ConfigureAwait(false),
            CandidateKind.RulePack => await SimulationLibraryCandidateRunner.SimulateRulePackAsync(this, tenant, actor, request.Candidate, asOf, cancellationToken).ConfigureAwait(false),
            _ => SimulationErrors.ContentInvalid,
        };
    }

    internal async Task<KernelError?> AuthorizeRunAsync(TenantId tenant, NodeRef scope, CancellationToken cancellationToken)
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

    internal async Task<MaskReason?> CheckTopLevelMaskAsync(
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

    private async Task<CommandResult<SimulationResult>> SimulateProtocolAsync(
        TenantId tenant,
        Actor actor,
        SimulationCandidate candidateRequest,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        var loaded = await SimulationCandidateLoader.LoadProtocolAsync(inputs, tenant, candidateRequest, cancellationToken).ConfigureAwait(false);
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
            return SimulationResultSealer.SealResult(
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
        var schedule = SimulationResultSealer.ScheduleChanges(baseVersion?.Content, candidate.Content);
        var matrixGaps = SimulationResultSealer.EvaluateMatrixGaps(candidate.Content);
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
            return SimulationResultSealer.SealResult(
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

        var pinnedSpecs = SimulationCandidateLoader.ParsePinnedMappingSpecs(maskedSnapshot.PinnedLibraryItems);
        var pinnedPacks = SimulationCandidateLoader.ParsePinnedRulePacks(maskedSnapshot.PinnedRulePacks);

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

        var maskedParts = SimulationResultSealer.CombineMaskedParts(maskedSnapshot.MaskedParts, dependentMaskReasons, maskedBlindedFinding);
        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return SimulationResultSealer.SealResult(
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

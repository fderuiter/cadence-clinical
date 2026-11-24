using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Time;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;
using Cadence.Standards.Mapping;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// Amendment impact (K-PROT-2, CLINICAL_CANVAS §4.2) as one caller of the dependents read (Canvas D2) paired with the
/// bitemporal study operational snapshot: active and pending site counts alongside total, affected, and re-consent
/// participant counts after visibility and blinding masking (ADR-2590).
/// </summary>
internal sealed class KernelAmendmentImpact(
    KernelTenantDbContextFactory contexts,
    KernelLineageStore lineage,
    IServiceScopeFactory? scopes = null,
    IBlindedTreatmentClassifier? blinding = null) : IAmendmentImpact
{
    private readonly IBlindedTreatmentClassifier _blinding = blinding ?? new BlindedTreatmentClassifier();

    public async Task<AmendmentImpact?> ComputeAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId amended,
        int depth = LineageQuery.DefaultDepth,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ProtocolVersion? version;
        string? parentContent = null;
        await using (var db = contexts.Create(tenant))
        {
            version = await db.ProtocolVersions.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == amended, cancellationToken)
                .ConfigureAwait(false);
            if (version?.ParentVersionId is { } parentId)
            {
                parentContent = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.Id == parentId)
                    .Select(v => v.Content)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (version is null)
        {
            return null;
        }

        if (version.ParentVersionId is not { } parent || version.Amendment is not { } amendment)
        {
            throw new InvalidOperationException($"Protocol version {version.Number} is a first version and amends nothing.");
        }

        var baseContent = parentContent
            ?? throw new InvalidOperationException($"Protocol version {version.Number} names a parent the tenant does not have.");
        var diff = ProtocolStructuralDiff.Compare(baseContent, version.Content);
        var changes = diff.IsSuccess
            ? diff.Value!.Where(c => c.ElementId != ProtocolStructuralDiff.RootId).ToList()
            : throw new InvalidOperationException($"Protocol version {version.Number} cannot be compared with its parent: {diff.Error!.Message}");

        var now = lineage.Clock.UtcNow;
        var asOf = new SimulationAsOf(
            KernelTimestamps.ToStoredPrecision(validAt ?? now),
            KernelTimestamps.ToStoredPrecision(knownAt ?? now));

        var rawDependents = await lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(NodeRef.For(parent), depth, ValidAt: asOf.ValidAt, KnownAt: asOf.KnownAt),
            excluded: new HashSet<NodeRef> { NodeRef.For(amended) },
            followFromStart: ProtocolElementImpact.FollowFromBase(ProtocolElementImpact.Impacted(version.StudyId, changes)),
            cancellationToken).ConfigureAwait(false);
        var (_, dependentMaskReasons) = SimulationSnapshotMasking.OmitMaskedDependents(rawDependents);

        var baseDesign = ProtocolDesignDocument.Read(baseContent).Design;
        var candidateDesign = ProtocolDesignDocument.Read(version.Content).Design;
        var soaDiff = candidateDesign is not null
            ? ScheduleOfActivitiesDiff.Compare(baseDesign, candidateDesign)
            : null;

        var rawSnapshot = await KernelSimulationInputs.LoadStudySnapshotCoreAsync(
            contexts,
            tenant,
            version.StudyId,
            asOf,
            cancellationToken).ConfigureAwait(false);

        var extraDesigns = new[] { baseDesign, candidateDesign }.OfType<ProtocolDesign>().ToList();
        MaskedSimulationSnapshot maskedSnapshot;
        IMappingEngine mappingEngine;
        IRuleEvaluator ruleEvaluator;
        if (scopes is not null)
        {
            await using var scope = scopes.CreateAsyncScope();
            var evaluator = scope.ServiceProvider.GetService<IAuthorizationEvaluator>();
            mappingEngine = scope.ServiceProvider.GetService<IMappingEngine>() ?? new MappingEngine();
            ruleEvaluator = scope.ServiceProvider.GetService<IRuleEvaluator>() ?? new DotNetRuleEvaluator();
            maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
                tenant,
                version.StudyId,
                reader,
                rawSnapshot,
                extraDesigns,
                lineage.Visibility,
                _blinding,
                probe: null,
                cancellationToken,
                evaluator,
                asOf).ConfigureAwait(false);
        }
        else
        {
            mappingEngine = new MappingEngine();
            ruleEvaluator = new DotNetRuleEvaluator();
            maskedSnapshot = await SimulationSnapshotMasking.MaskAsync(
                tenant,
                version.StudyId,
                reader,
                rawSnapshot,
                extraDesigns,
                lineage.Visibility,
                _blinding,
                probe: null,
                cancellationToken).ConfigureAwait(false);
        }

        var pinnedSpecs = maskedSnapshot.PinnedLibraryItems
            .Where(i => i.Kind == LibraryItemKind.MappingSpecification)
            .Select(i => (Item: i, Spec: MappingSpecificationJson.Read(i.Content).Specification))
            .Where(x => x.Spec is not null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Spec!.Overrides) ? 0 : 1)
            .ThenBy(x => x.Item.Key, StringComparer.Ordinal)
            .Select(x => x.Spec!)
            .ToList();
        var pinnedPacks = maskedSnapshot.PinnedRulePacks
            .Where(i => i.Kind == LibraryItemKind.RulePack)
            .Select(i => RulePackJson.Read(i.Content).Pack)
            .OfType<RulePack>()
            .ToList();

        var (_, baseProjected, candProjected, projImpacted) =
            SimulationProjectionAndValidationEvaluator.EvaluateProjections(
                mappingEngine,
                maskedSnapshot,
                pinnedSpecs,
                pinnedSpecs,
                baseDesign,
                candidateDesign);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var (_, ruleImpacted, maskedBlindedFinding) =
            SimulationProjectionAndValidationEvaluator.EvaluateValidationDelta(
                ruleEvaluator,
                _blinding,
                maskedSnapshot,
                asOfDate,
                pinnedPacks,
                pinnedPacks,
                baseDesign,
                candidateDesign,
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
            baseDesign,
            candidateDesign,
            amendment,
            soaDiff,
            combinedImpacted);

        return new AmendmentImpact(
            amended,
            parent,
            amendment.ImpactedElements,
            rawDependents,
            changes,
            soaDiff,
            operational.ActiveSiteCount,
            operational.PendingSiteCount,
            operational.TotalParticipantCount,
            operational.AffectedParticipantCount,
            operational.ReconsentParticipantCount,
            operational.Hidden);
    }
}

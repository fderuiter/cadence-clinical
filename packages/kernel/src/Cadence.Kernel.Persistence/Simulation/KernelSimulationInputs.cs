using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Simulation;

/// <summary>
/// Simulation's reads (ADR-2480): no-tracking queries under the tenant's Row-Level Security. It never saves, and an
/// architecture test keeps it from reaching the unit of work or <c>SaveChanges</c> (K-SIM-1). Dependents come from the
/// lineage store's dependents read, the same one as the Impact verb and amendment impact (Canvas D2).
/// Strictly excludes <c>IdentityVault</c> and participant direct identifiers (K-IDN-1).
/// </summary>
internal sealed class KernelSimulationInputs(KernelTenantDbContextFactory contexts, KernelLineageStore lineage) : ISimulationInputs
{
    public async Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(
        TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Id == id)
            .Select(p => new ProtocolVersionSnapshot(
                p.Id, p.StudyId, p.Number, p.Status, p.ParentVersionId, p.Content, p.ContentHash, p.VersionIndex, p.Amendment))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<DependentsReport> DependentsAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId baseVersion,
        ProtocolVersionId? candidate,
        IReadOnlySet<NodeRef> impactedElements,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asOf);
        var excluded = candidate is { } fork ? new HashSet<NodeRef> { NodeRef.For(fork) } : [];
        return lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(NodeRef.For(baseVersion), ValidAt: asOf.ValidAt, KnownAt: asOf.KnownAt),
            excluded,
            ProtocolElementImpact.FollowFromBase(impactedElements),
            cancellationToken);
    }

    public async Task<bool> StudyExistsAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.Studies.AsNoTracking()
            .AnyAsync(s => s.Id == studyId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LibraryItemView?> FindLibraryItemAsync(
        TenantId tenant, LibraryItemId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var item = await db.Set<LibraryItem>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return item is null ? null : SimulationAuditSnapshotLoader.ToLibraryItemView(item);
    }

    public Task<DependentsReport> DependentsOfNodeAsync(
        TenantId tenant,
        Actor reader,
        NodeRef start,
        NodeRef? excludedCandidate,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(asOf);
        var excluded = excludedCandidate is { } candidate ? new HashSet<NodeRef> { candidate } : [];
        return lineage.DependentsAsync(
            tenant,
            reader,
            new DependentsQuery(start, ValidAt: asOf.ValidAt, KnownAt: asOf.KnownAt),
            excluded,
            followFromStart: null,
            cancellationToken);
    }

    public Task<SimulationStudySnapshot> LoadStudySnapshotAsync(
        TenantId tenant,
        StudyId studyId,
        SimulationAsOf asOf,
        CancellationToken cancellationToken) =>
        LoadStudySnapshotCoreAsync(contexts, tenant, studyId, asOf, cancellationToken);

    internal static async Task<SimulationStudySnapshot> LoadStudySnapshotCoreAsync(
        KernelTenantDbContextFactory contexts,
        TenantId tenant,
        StudyId studyId,
        SimulationAsOf asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(asOf);

        var validAt = asOf.ValidAt;
        var knownAt = asOf.KnownAt;
        var knownAtUtc = knownAt.UtcDateTime;

        await using var db = contexts.Create(tenant);

        var siteSnapshots = await SimulationAuditSnapshotLoader.LoadSitesAsync(db, studyId, knownAt, knownAtUtc, cancellationToken).ConfigureAwait(false);

        var effectivitiesRaw = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == studyId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effectivitySnapshots = effectivitiesRaw
            .Where(e => e.Periods.Any(p => p.Period.Recorded.Start <= knownAt))
            .Select(e => new SimulationEffectivitySnapshot(e.Id, e.ProtocolVersionId, e.Scope, e.StatusAsOf(validAt, knownAt)))
            .ToList();

        var enrollmentsRaw = await db.Enrollments.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == studyId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var enrollmentSnapshots = new List<SimulationEnrollmentSnapshot>(enrollmentsRaw.Count);
        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId>();
        foreach (var e in enrollmentsRaw)
        {
            if (e.StatusAsOf(validAt, knownAt) is not { } statusAtCut)
            {
                continue;
            }

            var referenceStart = ResolveReferenceStartDate(e, validAt, knownAt);
            studySiteByEnrollment[e.Id] = e.StudySiteId;
            enrollmentSnapshots.Add(new SimulationEnrollmentSnapshot(
                e.Id,
                e.StudySiteId,
                e.SubjectCode.Value,
                statusAtCut,
                referenceStart));
        }

        var includedEnrollmentIds = studySiteByEnrollment.Keys.ToHashSet();

        var collectionsRaw = includedEnrollmentIds.Count == 0
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == studyId && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
                .OrderBy(c => c.CollectedAt)
                .ThenBy(c => c.OpenedAt)
                .ThenBy(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        collectionsRaw = [.. collectionsRaw.Where(c => includedEnrollmentIds.Contains(c.EnrollmentId))];

        var observationsRaw = includedEnrollmentIds.Count == 0
            ? []
            : await db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => o.StudyId == studyId && o.ObservedAt <= validAt)
                .OrderBy(o => o.ObservedAt)
                .ThenBy(o => o.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        observationsRaw = [.. observationsRaw.Where(o => includedEnrollmentIds.Contains(o.EnrollmentId))];

        var collectionByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in collectionsRaw)
        {
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                collectionByObservation[member.ObservationId] = (instance, member.ItemId);
            }
        }

        var observationSnapshots = new List<SimulationObservationSnapshot>(observationsRaw.Count);
        var collectedByObservation = new Dictionary<ObservationId, SimulationCollectedItemSnapshot>();
        foreach (var obs in observationsRaw)
        {
            var revAtCut = obs.Revisions
                .Where(r => r.RecordedAt <= knownAt)
                .OrderBy(r => r.Number)
                .LastOrDefault();
            if (revAtCut is null)
            {
                continue;
            }

            collectionByObservation.TryGetValue(obs.Id, out var membership);
            var studySiteId = studySiteByEnrollment[obs.EnrollmentId];
            observationSnapshots.Add(new SimulationObservationSnapshot(
                obs.Id,
                obs.EnrollmentId,
                studySiteId,
                obs.Concept,
                revAtCut.Value,
                membership.Instance?.Id,
                membership.Instance?.ProtocolVersionId,
                membership.Instance?.CollectionSpecId,
                membership.Instance?.PlannedActivityId,
                membership.ItemId));
            collectedByObservation[obs.Id] = new SimulationCollectedItemSnapshot(obs.Id, obs.Concept, revAtCut.Value);
        }

        var collectionSnapshots = new List<SimulationCollectionSnapshot>(collectionsRaw.Count);
        foreach (var instance in collectionsRaw)
        {
            var items = new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal);
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                if (collectedByObservation.TryGetValue(member.ObservationId, out var itemSnapshot))
                {
                    items[member.ItemId] = itemSnapshot;
                }
            }

            collectionSnapshots.Add(new SimulationCollectionSnapshot(
                instance.Id,
                instance.EnrollmentId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                instance.PlannedActivityId,
                instance.CollectedAt,
                instance.OpenedAt,
                items));
        }

        var protocolDesigns = await SimulationAuditSnapshotLoader.LoadProtocolDesignsAsync(db, studyId, knownAt, knownAtUtc, cancellationToken).ConfigureAwait(false);

        var conceptIds = observationSnapshots.Select(o => o.Concept.ConceptId)
            .Concat(protocolDesigns.Values.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept.ConceptId))
            .Distinct()
            .ToList();

        var concepts = await SimulationAuditSnapshotLoader.LoadConceptsAsync(db, conceptIds, knownAt, cancellationToken).ConfigureAwait(false);

        var (pinnedLibraryItems, pinnedRulePacks, declaredStandards, declaredStandardMetadata, codelistDecodes) =
            await SimulationAuditSnapshotLoader.LoadStandardsSnapshotAsync(db, studyId, knownAt, knownAtUtc, cancellationToken).ConfigureAwait(false);

        return new SimulationStudySnapshot(
            siteSnapshots,
            effectivitySnapshots,
            enrollmentSnapshots,
            observationSnapshots,
            collectionSnapshots,
            protocolDesigns,
            concepts,
            pinnedLibraryItems,
            pinnedRulePacks,
            declaredStandards,
            codelistDecodes,
            Loaded: true,
            DeclaredStandards: declaredStandardMetadata);
    }

    private static DateOnly ResolveReferenceStartDate(Enrollment enrollment, DateTimeOffset validAt, DateTimeOffset knownAt)
    {
        var knownPeriods = enrollment.Periods
            .Where(p => p.Period.Recorded.Contains(knownAt) && p.Period.Valid.Start <= validAt)
            .ToList();

        var enrolledStart = knownPeriods
            .Where(p => p.Status == EnrollmentStatus.Enrolled)
            .Select(p => (DateTimeOffset?)p.Period.Valid.Start)
            .Min();
        if (enrolledStart is { } enrolledAt)
        {
            return DateOnly.FromDateTime(enrolledAt.UtcDateTime);
        }

        var earliestStart = knownPeriods
            .Select(p => (DateTimeOffset?)p.Period.Valid.Start)
            .Min();
        return earliestStart is { } firstAt
            ? DateOnly.FromDateTime(firstAt.UtcDateTime)
            : DateOnly.FromDateTime(validAt.UtcDateTime);
    }
}

/// <summary>Registers simulation's reads.</summary>
public static class KernelSimulationPersistenceServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ISimulationInputs"/> over the kernel tables. Requires <c>AddKernelPersistence</c>.</summary>
    public static IServiceCollection AddKernelSimulationInputs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISimulationInputs, KernelSimulationInputs>();
        return services;
    }
}

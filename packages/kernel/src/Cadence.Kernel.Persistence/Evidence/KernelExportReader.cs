using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Projects canonical kernel truth at <c>(validAt, knownAt)</c> for study exports and participant casebooks (K-TRUTH-1).
/// Strictly excludes <c>IdentityVault</c> direct identifiers; only pseudonymous kernel identifiers and
/// <c>Enrollment.SubjectCode</c> are read.
/// </summary>
internal sealed class KernelExportReader(KernelTenantDbContextFactory contexts) : IKernelExportReader
{
    public async Task<StudyExportSnapshot?> ReadStudySnapshotAsync(
        TenantId tenant,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var foundStudy = await db.Studies.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == study, cancellationToken)
            .ConfigureAwait(false);
        if (foundStudy is null)
        {
            return null;
        }

        var studySites = await db.StudySites.AsNoTracking()
            .Where(ss => ss.StudyId == study && ss.CreatedAt <= knownAt.UtcDateTime)
            .OrderBy(ss => ss.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var siteIds = studySites.Select(ss => ss.SiteId).Distinct().ToList();
        var sitesById = siteIds.Count == 0
            ? new Dictionary<SiteId, Site>()
            : await db.Sites.AsNoTracking()
                .Where(s => siteIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken)
                .ConfigureAwait(false);

        var siteSnapshots = studySites
            .Select(ss =>
            {
                sitesById.TryGetValue(ss.SiteId, out var site);
                return new ExportSiteSnapshot(
                    ss.Id,
                    ss.SiteId,
                    site?.SiteCode ?? ss.SiteId.ToString(),
                    site?.Name ?? ss.SiteId.ToString(),
                    site?.Country ?? string.Empty,
                    ss.Status);
            })
            .ToList();

        var siteCodeByStudySite = siteSnapshots.ToDictionary(s => s.StudySiteId, s => s.SiteCode);

        var enrollmentsRaw = await db.Enrollments.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == study)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var enrollmentSnapshots = new List<ExportEnrollmentSnapshot>();
        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId>();
        foreach (var e in enrollmentsRaw)
        {
            if (e.StatusAsOf(validAt, knownAt) is not { } statusAtCut)
            {
                continue;
            }

            studySiteByEnrollment[e.Id] = e.StudySiteId;
            enrollmentSnapshots.Add(new ExportEnrollmentSnapshot(
                e.Id,
                e.ParticipantId,
                e.StudySiteId,
                siteCodeByStudySite.GetValueOrDefault(e.StudySiteId, e.StudySiteId.ToString()),
                e.SubjectCode.Value,
                statusAtCut,
                e.EnrollmentNumber));
        }

        var includedEnrollmentIds = studySiteByEnrollment.Keys.ToHashSet();

        var collectionsRaw = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.StudyId == study && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
            .OrderBy(c => c.CollectedAt)
            .ThenBy(c => c.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        collectionsRaw = [.. collectionsRaw.Where(c => includedEnrollmentIds.Contains(c.EnrollmentId))];

        var observationsRaw = new List<Observation>();
        var observationStream = db.Observations.AsNoTracking()
            .Include(o => o.Revisions)
            .Where(o => o.StudyId == study && o.ObservedAt <= validAt)
            .OrderBy(o => o.ObservedAt)
            .ThenBy(o => o.Id)
            .AsAsyncEnumerable();
        await foreach (var obs in observationStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (includedEnrollmentIds.Contains(obs.EnrollmentId))
            {
                observationsRaw.Add(obs);
            }
        }

        var assertionsRaw = new List<Assertion>();
        var assertionStream = db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study && a.AssertedAt <= knownAt)
            .OrderBy(a => a.AssertedAt)
            .ThenBy(a => a.Id)
            .AsAsyncEnumerable();
        await foreach (var assertion in assertionStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            assertionsRaw.Add(assertion);
        }

        var (appliedLocks, releasesByLock) = IndexLocksAndEarliestReleases(assertionsRaw);

        var referencedProtocolIds = collectionsRaw.Select(c => c.ProtocolVersionId).ToHashSet();
        var protocols = await ExportProtocolHistoryReader.ReadProtocolsAsync(db, study, validAt, knownAt, referencedProtocolIds, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var lockEntries = await KernelLockRecords.HydrateAsync(db, study, appliedLocks, releasesByLock, cancellationToken, validAt, knownAt)
            .ConfigureAwait(false);

        var (collectionSnapshots, observationSnapshots) = await ExportObservationAndCollectionProjector.BuildObservationsAndCollectionsAsync(
            db,
            protocols,
            designs,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var targetIds = new HashSet<Guid> { study.Value };
        foreach (var p in protocols) targetIds.Add(p.ProtocolVersionId.Value);
        foreach (var e in enrollmentSnapshots) targetIds.Add(e.EnrollmentId.Value);
        foreach (var c in collectionSnapshots) targetIds.Add(c.CollectionInstanceId.Value);
        foreach (var o in observationSnapshots) targetIds.Add(o.ObservationId.Value);
        foreach (var l in assertionsRaw) targetIds.Add(l.Id.Value);

        var attestations = await ExportAttestationAndAuditReader.ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);
        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var blindedAuditKeys = ExportAttestationAndAuditReader.BuildBlindedAuditKeys(collectionSnapshots, observationSnapshots, lockEntries, attestations, designs);
        var (audit, blindedAuditSequences, auditAggregateIdsBySequence) = await ExportAttestationAndAuditReader.ReadAuditEntriesAsync(
            db,
            auditKeys,
            blindedAuditKeys,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        return new StudyExportSnapshot(
            foundStudy.Id,
            foundStudy.StudyCode,
            foundStudy.Title,
            foundStudy.Phase,
            validAt,
            knownAt,
            protocols,
            siteSnapshots,
            enrollmentSnapshots,
            collectionSnapshots,
            observationSnapshots,
            lockEntries,
            attestations,
            audit,
            blindedAuditSequences,
            auditAggregateIdsBySequence);
    }

    public async Task<EnrollmentCasebookSnapshot?> ReadEnrollmentCasebookAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var foundEnrollment = await db.Enrollments.AsNoTracking()
            .Include(e => e.Periods)
            .SingleOrDefaultAsync(e => e.Id == enrollment, cancellationToken)
            .ConfigureAwait(false);
        if (foundEnrollment is null)
        {
            return null;
        }

        var foundStudy = await db.Studies.AsNoTracking()
            .SingleAsync(s => s.Id == foundEnrollment.StudyId, cancellationToken)
            .ConfigureAwait(false);

        var studySite = await db.StudySites.AsNoTracking()
            .SingleAsync(ss => ss.Id == foundEnrollment.StudySiteId, cancellationToken)
            .ConfigureAwait(false);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == studySite.SiteId, cancellationToken)
            .ConfigureAwait(false);

        var statusAtCut = foundEnrollment.StatusAsOf(validAt, knownAt);

        var collectionsRaw = statusAtCut is null
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.EnrollmentId == enrollment && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
                .OrderBy(c => c.CollectedAt)
                .ThenBy(c => c.OpenedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var observationsRaw = new List<Observation>();
        if (statusAtCut is not null)
        {
            var observationStream = db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => o.EnrollmentId == enrollment && o.ObservedAt <= validAt)
                .OrderBy(o => o.ObservedAt)
                .ThenBy(o => o.Id)
                .AsAsyncEnumerable();
            await foreach (var obs in observationStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                observationsRaw.Add(obs);
            }
        }

        var assertionsRaw = await ReadEnrollmentAssertionsBoundedAsync(
            db,
            foundStudy.Id,
            foundEnrollment.StudySiteId,
            enrollment,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var (appliedLocks, releasesByLock) = IndexLocksAndEarliestReleases(assertionsRaw);

        var referencedProtocolIds = collectionsRaw.Select(c => c.ProtocolVersionId).ToHashSet();
        var protocols = await ExportProtocolHistoryReader.ReadProtocolsAsync(db, foundStudy.Id, validAt, knownAt, referencedProtocolIds, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var lockEntries = await KernelLockRecords.HydrateAsync(db, foundStudy.Id, appliedLocks, releasesByLock, cancellationToken, validAt, knownAt)
            .ConfigureAwait(false);

        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId> { [enrollment] = foundEnrollment.StudySiteId };
        var (collectionSnapshots, observationSnapshots) = await ExportObservationAndCollectionProjector.BuildObservationsAndCollectionsAsync(
            db,
            protocols,
            designs,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var obsAssertionViews = observationSnapshots
            .SelectMany(o => o.Assertions)
            .DistinctBy(a => a.AssertionId)
            .OrderBy(a => a.AssertedAt)
            .ThenBy(a => a.AssertionId)
            .ToList();

        var targetIds = new HashSet<Guid> { enrollment.Value };
        foreach (var c in collectionSnapshots) targetIds.Add(c.CollectionInstanceId.Value);
        foreach (var o in observationSnapshots) targetIds.Add(o.ObservationId.Value);
        foreach (var l in lockEntries)
        {
            targetIds.Add(l.LockId.Value);
            if (l.ReleaseAssertionId is { } relId)
            {
                targetIds.Add(relId.Value);
            }
        }

        var attestations = await ExportAttestationAndAuditReader.ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);

        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(obsAssertionViews.Select(a => a.AssertionId.ToString()))
            .Concat(collectionSnapshots.Select(c => c.CollectionInstanceId.ToString()))
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .Append(enrollment.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var blindedAuditKeys = ExportAttestationAndAuditReader.BuildBlindedAuditKeys(collectionSnapshots, observationSnapshots, lockEntries, attestations, designs);
        var (audit, blindedAuditSequences, _) = await ExportAttestationAndAuditReader.ReadAuditEntriesAsync(db, auditKeys, blindedAuditKeys, knownAt, cancellationToken).ConfigureAwait(false);

        var canonicalCutAt = ComputeEnrollmentCanonicalCut(
            foundStudy,
            studySite,
            foundEnrollment,
            collectionSnapshots,
            observationSnapshots,
            obsAssertionViews,
            lockEntries,
            attestations,
            audit,
            knownAt);

        return new EnrollmentCasebookSnapshot(
            foundStudy.Id,
            foundStudy.StudyCode,
            foundStudy.Title,
            foundEnrollment.StudySiteId,
            site?.SiteCode ?? studySite.SiteId.ToString(),
            site?.Name ?? studySite.SiteId.ToString(),
            foundEnrollment.Id,
            foundEnrollment.ParticipantId,
            foundEnrollment.SubjectCode.Value,
            statusAtCut,
            validAt,
            knownAt,
            protocols,
            collectionSnapshots,
            observationSnapshots,
            obsAssertionViews,
            lockEntries,
            attestations,
            audit,
            blindedAuditSequences,
            canonicalCutAt);
    }

    internal static Task<IReadOnlyList<ExportProtocolSnapshot>> ReadProtocolsAsync(
        KernelDbContext db,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        IReadOnlySet<ProtocolVersionId> referencedVersionIds,
        CancellationToken cancellationToken) =>
        ExportProtocolHistoryReader.ReadProtocolsAsync(db, study, validAt, knownAt, referencedVersionIds, cancellationToken);

    internal static (IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations) AssembleObservationsAndCollections(
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt) =>
        ExportObservationAndCollectionProjector.AssembleObservationsAndCollections(
            protocols,
            designs,
            conceptsById,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt);

    internal static HashSet<string> BuildBlindedAuditKeys(
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ProtocolDesign> designs) =>
        ExportAttestationAndAuditReader.BuildBlindedAuditKeys(collections, observations, locks, attestations, designs);

    private static DateTimeOffset ComputeEnrollmentCanonicalCut(
        Study study,
        StudySite studySite,
        Enrollment enrollment,
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<AssertionView> assertions,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ObservationAuditView> audit,
        DateTimeOffset knownAt)
    {
        var watermark = new DateTimeOffset(DateTime.SpecifyKind(enrollment.CreatedAt, DateTimeKind.Utc));
        MaxBounded(ref watermark, new DateTimeOffset(DateTime.SpecifyKind(study.CreatedAt, DateTimeKind.Utc)), knownAt);
        MaxBounded(ref watermark, new DateTimeOffset(DateTime.SpecifyKind(studySite.CreatedAt, DateTimeKind.Utc)), knownAt);

        foreach (var period in enrollment.Periods)
        {
            MaxBounded(ref watermark, period.Period.Recorded.Start, knownAt);
            MaxBounded(ref watermark, period.Period.Valid.Start, knownAt);
        }

        foreach (var coll in collections)
        {
            MaxBounded(ref watermark, coll.OpenedAt, knownAt);
            MaxBounded(ref watermark, coll.CollectedAt, knownAt);
        }

        foreach (var obs in observations)
        {
            MaxBounded(ref watermark, obs.ObservedAt, knownAt);
            foreach (var rev in obs.Revisions)
            {
                MaxBounded(ref watermark, rev.RecordedAt, knownAt);
            }
        }

        foreach (var assertion in assertions)
        {
            MaxBounded(ref watermark, assertion.AssertedAt, knownAt);
            if (assertion.RetractedAt is { } retractedAt)
            {
                MaxBounded(ref watermark, retractedAt, knownAt);
            }
        }

        foreach (var lk in locks)
        {
            MaxBounded(ref watermark, lk.LockedAt, knownAt);
            if (lk.ReleasedAt is { } releasedAt)
            {
                MaxBounded(ref watermark, releasedAt, knownAt);
            }
        }

        foreach (var att in attestations)
        {
            MaxBounded(ref watermark, att.RecordedAt, knownAt);
        }

        foreach (var entry in audit)
        {
            MaxBounded(ref watermark, entry.RecordedAt, knownAt);
        }

        return watermark;
    }

    private static void MaxBounded(ref DateTimeOffset current, DateTimeOffset candidate, DateTimeOffset ceiling)
    {
        if (candidate <= ceiling && candidate > current)
        {
            current = candidate;
        }
    }

    private static async Task<IReadOnlyList<Assertion>> ReadEnrollmentAssertionsBoundedAsync(
        KernelDbContext db,
        StudyId study,
        StudySiteId studySite,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var enrollmentAssertions = await db.Assertions.AsNoTracking()
            .Where(a => a.EnrollmentId == enrollment && a.AssertedAt <= knownAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] coveringScopeIds = [study.Value, studySite.Value, enrollment.Value];
        var coveringScopeLocks = await db.Assertions.AsNoTracking()
            .Where(a => coveringScopeIds.Contains(a.TargetId)
                && (a.Kind == AssertionKind.Locked || a.Kind == AssertionKind.Frozen)
                && a.AssertedAt <= knownAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var queryIds = enrollmentAssertions
            .Where(a => a.StudyId == study && a.Kind == AssertionKind.Queried && a.RetractsId == null)
            .Select(a => a.Id.Value)
            .ToList();

        var queryAnswers = queryIds.Count == 0
            ? []
            : await db.Assertions.AsNoTracking()
                .Where(a => queryIds.Contains(a.TargetId)
                    && a.Kind == AssertionKind.QueryAnswered
                    && a.TargetKind == NodeKinds.Assertion
                    && a.AssertedAt <= knownAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var byId = new Dictionary<AssertionId, Assertion>(enrollmentAssertions.Count + coveringScopeLocks.Count + queryAnswers.Count);
        foreach (var assertion in enrollmentAssertions)
        {
            if (assertion.StudyId == study)
            {
                byId[assertion.Id] = assertion;
            }
        }

        foreach (var assertion in coveringScopeLocks)
        {
            if (assertion.StudyId == study)
            {
                byId.TryAdd(assertion.Id, assertion);
            }
        }

        foreach (var assertion in queryAnswers)
        {
            if (assertion.StudyId == study)
            {
                byId.TryAdd(assertion.Id, assertion);
            }
        }

        var merged = byId.Values.ToList();
        merged.Sort(AssertionOrderComparer.Instance);
        return merged;
    }

    private static (List<Assertion> AppliedLocks, Dictionary<AssertionId, Assertion> ReleasesByLock) IndexLocksAndEarliestReleases(
        IReadOnlyList<Assertion> assertions)
    {
        var appliedLocks = new List<Assertion>();
        var releasesByLock = new Dictionary<AssertionId, Assertion>();
        foreach (var assertion in assertions)
        {
            if (!assertion.IsLock)
            {
                continue;
            }

            if (assertion.RetractsId is { } retractedLockId)
            {
                if (!releasesByLock.TryGetValue(retractedLockId, out var existingRelease)
                    || ExportObservationAndCollectionProjector.IsEarlierAssertion(assertion, existingRelease))
                {
                    releasesByLock[retractedLockId] = assertion;
                }
            }
            else
            {
                appliedLocks.Add(assertion);
            }
        }

        return (appliedLocks, releasesByLock);
    }
}

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

        var protocols = await ReadProtocolsAsync(db, study, knownAt, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var studySites = await db.StudySites.AsNoTracking()
            .Where(ss => ss.StudyId == study && ss.CreatedAt <= knownAt)
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

        var observationsRaw = await db.Observations.AsNoTracking()
            .Include(o => o.Revisions)
            .Where(o => o.StudyId == study && o.ObservedAt <= validAt)
            .OrderBy(o => o.ObservedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        observationsRaw = [.. observationsRaw.Where(o => includedEnrollmentIds.Contains(o.EnrollmentId))];

        var assertionsRaw = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study && a.AssertedAt <= validAt && a.AssertedAt <= knownAt)
            .OrderBy(a => a.AssertedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var appliedLocks = assertionsRaw.Where(a => a.IsLock && a.RetractsId is null).ToList();
        var releasesByLock = assertionsRaw
            .Where(a => a.IsLock && a.RetractsId is not null)
            .GroupBy(a => a.RetractsId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AssertedAt).First());

        var lockEntries = await KernelLockRecords.HydrateAsync(db, study, appliedLocks, releasesByLock, cancellationToken)
            .ConfigureAwait(false);

        var (collectionSnapshots, observationSnapshots) = await BuildObservationsAndCollectionsAsync(
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

        var attestations = await ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);
        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var audit = await ReadAuditEntriesAsync(db, auditKeys, knownAt, cancellationToken).ConfigureAwait(false);

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
            audit);
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
        var protocols = await ReadProtocolsAsync(db, foundStudy.Id, knownAt, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var collectionsRaw = statusAtCut is null
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.EnrollmentId == enrollment && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
                .OrderBy(c => c.CollectedAt)
                .ThenBy(c => c.OpenedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var observationsRaw = statusAtCut is null
            ? []
            : await db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => o.EnrollmentId == enrollment && o.ObservedAt <= validAt)
                .OrderBy(o => o.ObservedAt)
                .ThenBy(o => o.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var studyUuid = foundStudy.Id.Value;
        var siteUuid = foundEnrollment.StudySiteId.Value;
        var enrollmentUuid = enrollment.Value;
        var assertionsRaw = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == foundStudy.Id
                && a.AssertedAt <= validAt
                && a.AssertedAt <= knownAt
                && (a.EnrollmentId == enrollment
                    || a.TargetId == studyUuid
                    || a.TargetId == siteUuid
                    || a.TargetId == enrollmentUuid
                    || a.TargetKind == NodeKinds.Assertion))
            .OrderBy(a => a.AssertedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var appliedLocks = assertionsRaw.Where(a => a.IsLock && a.RetractsId is null).ToList();
        var releasesByLock = assertionsRaw
            .Where(a => a.IsLock && a.RetractsId is not null)
            .GroupBy(a => a.RetractsId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AssertedAt).First());

        var lockEntries = await KernelLockRecords.HydrateAsync(db, foundStudy.Id, appliedLocks, releasesByLock, cancellationToken)
            .ConfigureAwait(false);

        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId> { [enrollment] = foundEnrollment.StudySiteId };
        var (collectionSnapshots, observationSnapshots) = await BuildObservationsAndCollectionsAsync(
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

        var attestations = await ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);
        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(collectionSnapshots.Select(c => c.CollectionInstanceId.ToString()))
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .Append(enrollment.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var audit = await ReadAuditEntriesAsync(db, auditKeys, knownAt, cancellationToken).ConfigureAwait(false);

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
            audit);
    }

    private static async Task<IReadOnlyList<ExportProtocolSnapshot>> ReadProtocolsAsync(
        KernelDbContext db,
        StudyId study,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == study && v.CreatedAt <= knownAt)
            .OrderBy(v => v.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (versions.Count == 0)
        {
            versions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == study)
                .OrderBy(v => v.Number)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var snapshots = await db.FormVersionSnapshots.AsNoTracking()
            .Where(s => s.StudyId == study && (s.RecordedAt == default || s.RecordedAt <= knownAt))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var odmByVersion = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.OdmXml))
            .GroupBy(s => s.ProtocolVersionId)
            .ToDictionary(g => g.Key, g => g.First().OdmXml);

        return versions
            .Select(v => new ExportProtocolSnapshot(
                v.Id,
                v.Number,
                v.Status,
                ProtocolDesignDocument.Read(v.Content).Design,
                v.Content,
                v.ContentHash,
                odmByVersion.GetValueOrDefault(v.Id)))
            .ToList();
    }

    private static async Task<(IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations)> BuildObservationsAndCollectionsAsync(
        KernelDbContext db,
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var designByVersion = protocols
            .Where(p => p.Design is not null)
            .ToDictionary(p => p.ProtocolVersionId, p => p.Design!);

        var conceptIds = observationsRaw.Select(o => o.ConceptId).Distinct().ToList();
        var conceptsById = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Include(c => c.Revisions)
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var collectionByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in collectionsRaw)
        {
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                collectionByObservation[member.ObservationId] = (instance, member.ItemId);
            }
        }

        var activeLocks = lockEntries.Where(l => !l.Released).ToList();
        var nonLockStated = assertionsRaw.Where(a => !a.IsLock && a.RetractsId is null).ToList();
        var nonLockRetractedBy = assertionsRaw
            .Where(a => !a.IsLock && a.RetractsId is not null)
            .GroupBy(a => a.RetractsId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AssertedAt).First());

        var answersByQuery = nonLockStated
            .Where(a => a.Kind == AssertionKind.QueryAnswered && a.TargetKind == NodeKinds.Assertion)
            .GroupBy(a => AssertionId.FromGuid(a.TargetId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var currentAtCutByObservation = new Dictionary<ObservationId, (int Revision, ObservationValue Value)>();
        var observationSnapshots = new List<ExportObservationSnapshot>();

        foreach (var obs in observationsRaw)
        {
            var revsAtCut = obs.Revisions
                .Where(r => r.RecordedAt <= knownAt)
                .OrderBy(r => r.Number)
                .ToList();
            if (revsAtCut.Count == 0)
            {
                continue;
            }

            var views = revsAtCut
                .Select((r, idx) => new ObservationRevisionView(
                    r.Number,
                    r.Value,
                    r.ReasonForChange,
                    r.RecordedBy,
                    r.RecordedAt,
                    idx + 1 < revsAtCut.Count ? revsAtCut[idx + 1].RecordedAt : null))
                .ToList();
            var currentRev = views[^1];
            currentAtCutByObservation[obs.Id] = (currentRev.Number, currentRev.Value);

            collectionByObservation.TryGetValue(obs.Id, out var membership);
            var studySiteId = studySiteByEnrollment[obs.EnrollmentId];

            conceptsById.TryGetValue(obs.ConceptId, out var concept);
            var revLabel = concept?.Revisions.FirstOrDefault(r => r.Version == obs.ConceptVersion)?.PreferredLabel
                ?? concept?.PreferredLabel
                ?? obs.ConceptId.ToString();
            var conceptCode = concept?.ExternalCode ?? obs.ConceptId.ToString();

            bool isBlinded = BlindedObservationClassifier.IsBlindedTreatment(
                obs.ConceptId,
                currentRev.Value,
                concept,
                membership.Instance?.CollectionSpecId,
                membership.ItemId,
                designs);

            var coveringScopes = LockScopes.Covering(
                obs.StudyId,
                studySiteId,
                obs.EnrollmentId,
                obs.Id,
                membership.Instance?.Id);

            var matchingLocks = activeLocks
                .Where(l => coveringScopes.Any(s => s.Kind == l.Scope.Kind && s.Id == l.Scope.Id))
                .ToList();
            LockKind? effectiveLock = matchingLocks.Count switch
            {
                0 => null,
                _ when matchingLocks.Any(l => l.Kind == LockKind.Locked) => LockKind.Locked,
                _ => LockKind.Frozen,
            };

            var directAssertions = nonLockStated
                .Where(a => a.TargetKind == NodeKinds.Observation && a.TargetId == obs.Id.Value)
                .ToList();
            var queryAnswers = directAssertions
                .Where(a => a.Kind == AssertionKind.Queried)
                .SelectMany(q => answersByQuery.GetValueOrDefault(q.Id, []))
                .ToList();

            var aboutViews = directAssertions
                .Concat(queryAnswers)
                .OrderBy(a => a.AssertedAt)
                .ThenBy(a => a.Id)
                .Select(a => nonLockRetractedBy.TryGetValue(a.Id, out var ret)
                    ? new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, ret.Id, ret.AssertedAt)
                    : new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, null, null))
                .ToList();

            observationSnapshots.Add(new ExportObservationSnapshot(
                obs.Id,
                obs.EnrollmentId,
                obs.ParticipantId,
                studySiteId,
                membership.Instance?.Id,
                membership.Instance?.CollectionSpecId,
                membership.Instance?.PlannedActivityId,
                membership.ItemId,
                obs.Concept,
                conceptCode,
                revLabel,
                isBlinded,
                obs.ObservedAt,
                currentRev,
                views,
                effectiveLock,
                aboutViews));
        }

        var collectionSnapshots = new List<ExportCollectionSnapshot>(collectionsRaw.Count);
        foreach (var instance in collectionsRaw)
        {
            designByVersion.TryGetValue(instance.ProtocolVersionId, out var design);
            var specName = design?.CollectionSpecs.FirstOrDefault(s => s.Id == instance.CollectionSpecId)?.Name
                ?? instance.CollectionSpecId;
            var activityName = instance.PlannedActivityId is null
                ? null
                : design?.PlannedActivities.FirstOrDefault(a => a.Id == instance.PlannedActivityId)?.Name ?? instance.PlannedActivityId;

            var itemsMap = new Dictionary<ObservationId, string>();
            var hashEntries = new List<CollectionContentEntry>();
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                if (currentAtCutByObservation.TryGetValue(member.ObservationId, out var current))
                {
                    itemsMap[member.ObservationId] = member.ItemId;
                    hashEntries.Add(new CollectionContentEntry(member.ObservationId, current.Revision, current.Value));
                }
            }

            var contentHash = hashEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(hashEntries);
            collectionSnapshots.Add(new ExportCollectionSnapshot(
                instance.Id,
                instance.EnrollmentId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                specName,
                instance.PlannedActivityId,
                activityName,
                instance.CollectedAt,
                instance.OpenedAt,
                instance.OpenedBy,
                contentHash,
                itemsMap));
        }

        return (collectionSnapshots, observationSnapshots);
    }

    private static async Task<IReadOnlyList<ExportAttestationSnapshot>> ReadAttestationsAsync(
        KernelDbContext db,
        HashSet<Guid> targetIds,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (targetIds.Count == 0)
        {
            return [];
        }

        var ids = targetIds.ToList();
        var rows = await db.Attestations.AsNoTracking()
            .Where(a => ids.Contains(a.TargetId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(a => new ExportAttestationSnapshot(
                a.Id,
                a.ActorId,
                a.Meaning,
                a.TargetKind,
                a.TargetId,
                a.TargetContentHash,
                a.AuthenticatedAt,
                a.Method,
                a.RecordedAt,
                a.Reason))
            .ToList();
    }

    private static async Task<IReadOnlyList<ObservationAuditView>> ReadAuditEntriesAsync(
        KernelDbContext db,
        HashSet<string> aggregateKeys,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (aggregateKeys.Count == 0)
        {
            return [];
        }

        var keys = aggregateKeys.ToList();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId != null && keys.Contains(a.AggregateId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.Sequence)
            .Select(a => new ObservationAuditView(
                a.Sequence,
                a.RecordedAt,
                a.ActorId,
                a.Action,
                a.AggregateType ?? string.Empty,
                a.AggregateVersion,
                a.ReasonForChange))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

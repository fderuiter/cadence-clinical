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
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads study locks and their release history from canonical kernel assertions (spec §10.5, ADR-2621).
/// </summary>
internal sealed class KernelLockRecords(KernelTenantDbContextFactory contexts) : ILockRecords
{
    public async Task<StudyLockEntry?> FindAsync(TenantId tenant, AssertionId lockId, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var assertion = await db.Assertions.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == lockId && a.RetractsId == null, cancellationToken)
            .ConfigureAwait(false);
        if (assertion is null || !assertion.IsLock)
        {
            return null;
        }

        var release = await db.Assertions.AsNoTracking()
            .Where(a => a.RetractsId == lockId)
            .OrderBy(a => a.AssertedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = await HydrateAsync(
            db,
            assertion.StudyId,
            [assertion],
            release is null ? new Dictionary<AssertionId, Assertion>() : new Dictionary<AssertionId, Assertion> { [lockId] = release },
            cancellationToken).ConfigureAwait(false);
        return entries.SingleOrDefault();
    }

    public async Task<StudyLockList?> ListByStudyAsync(
        TenantId tenant,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        bool activeOnly,
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

        var lockRows = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study
                && (a.Kind == AssertionKind.Frozen || a.Kind == AssertionKind.Locked)
                && a.AssertedAt <= validAt
                && a.AssertedAt <= knownAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var applied = lockRows
            .Where(a => a.RetractsId is null)
            .ToList();
        var releasesByLock = lockRows
            .Where(a => a.RetractsId is not null)
            .GroupBy(a => a.RetractsId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AssertedAt).First());

        if (activeOnly)
        {
            applied = [.. applied.Where(a => !releasesByLock.ContainsKey(a.Id))];
        }

        var hydrated = await HydrateAsync(db, study, applied, releasesByLock, cancellationToken, validAt, knownAt).ConfigureAwait(false);
        var ordered = hydrated
            .OrderBy(e => ScopeOrder(e.Scope.Kind))
            .ThenBy(e => e.LockedAt)
            .ThenBy(e => e.LockId)
            .ToList();

        return new StudyLockList(foundStudy.Id, foundStudy.StudyCode, validAt, knownAt, ordered);
    }

    internal static async Task<IReadOnlyList<StudyLockEntry>> HydrateAsync(
        KernelDbContext db,
        StudyId studyId,
        IReadOnlyList<Assertion> applied,
        IReadOnlyDictionary<AssertionId, Assertion> releasesByLock,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null)
    {
        if (applied.Count == 0)
        {
            return [];
        }

        var attestationIds = applied
            .Select(a => a.AttestationId)
            .Concat(releasesByLock.Values.Select(r => r.AttestationId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var attestations = attestationIds.Count == 0
            ? new Dictionary<AttestationId, Attestation>()
            : await db.Attestations.AsNoTracking()
                .Where(a => attestationIds.Contains(a.Id) && (knownAt == null || a.RecordedAt <= knownAt.Value))
                .ToDictionaryAsync(a => a.Id, cancellationToken)
                .ConfigureAwait(false);

        var observationScopeIds = applied
            .Where(a => a.TargetKind == NodeKinds.Observation)
            .Select(a => ObservationId.FromGuid(a.TargetId))
            .Distinct()
            .ToList();

        Dictionary<ObservationId, Observation> observations;
        if (observationScopeIds.Count == 0)
        {
            observations = [];
        }
        else
        {
            var rawObservations = await db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => observationScopeIds.Contains(o.Id) && (validAt == null || o.ObservedAt <= validAt.Value))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            observations = rawObservations
                .Where(o => knownAt is null || o.Revisions.Any(r => r.RecordedAt <= knownAt.Value))
                .ToDictionary(o => o.Id);
        }

        var collectionScopeIds = applied
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance)
            .Select(a => CollectionInstanceId.FromGuid(a.TargetId))
            .Distinct()
            .ToList();

        var collections = collectionScopeIds.Count == 0
            ? new Dictionary<CollectionInstanceId, CollectionInstance>()
            : await db.CollectionInstances.AsNoTracking()
                .Where(c => collectionScopeIds.Contains(c.Id)
                    && (validAt == null || c.CollectedAt <= validAt.Value)
                    && (knownAt == null || c.OpenedAt <= knownAt.Value))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var enrollmentIds = applied
            .Select(a => a.EnrollmentId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Concat(applied.Where(a => a.TargetKind == NodeKinds.Enrollment).Select(a => EnrollmentId.FromGuid(a.TargetId)))
            .Concat(observations.Values.Select(o => o.EnrollmentId))
            .Concat(collections.Values.Select(c => c.EnrollmentId))
            .Distinct()
            .ToList();

        Dictionary<EnrollmentId, Enrollment> enrollments;
        if (enrollmentIds.Count == 0)
        {
            enrollments = [];
        }
        else
        {
            var rawEnrollments = await db.Enrollments.AsNoTracking()
                .Include(e => e.Periods)
                .Where(e => enrollmentIds.Contains(e.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            enrollments = rawEnrollments
                .Where(e => validAt is null || knownAt is null || e.StatusAsOf(validAt.Value, knownAt.Value) is not null)
                .ToDictionary(e => e.Id);
        }

        var blindedObservationIds = await ResolveBlindedObservationsAsync(
            db, studyId, observations.Values, cancellationToken, validAt, knownAt).ConfigureAwait(false);

        var result = new List<StudyLockEntry>(applied.Count);
        foreach (var lockAssertion in applied)
        {
            EnrollmentId? enrollmentId = lockAssertion.EnrollmentId;
            StudySiteId? studySiteId = null;
            bool blinded = false;

            switch (lockAssertion.TargetKind)
            {
                case NodeKinds.StudySite:
                    studySiteId = StudySiteId.FromGuid(lockAssertion.TargetId);
                    break;
                case NodeKinds.Enrollment:
                    enrollmentId ??= EnrollmentId.FromGuid(lockAssertion.TargetId);
                    break;
                case NodeKinds.CollectionInstance:
                    var cid = CollectionInstanceId.FromGuid(lockAssertion.TargetId);
                    if (collections.TryGetValue(cid, out var coll))
                    {
                        enrollmentId ??= coll.EnrollmentId;
                        studySiteId = coll.StudySiteId;
                    }

                    break;
                case NodeKinds.Observation:
                    var oid = ObservationId.FromGuid(lockAssertion.TargetId);
                    blinded = blindedObservationIds.Contains(oid);
                    if (observations.TryGetValue(oid, out var obs))
                    {
                        enrollmentId ??= obs.EnrollmentId;
                    }

                    break;
            }

            string? subjectCode = null;
            if (enrollmentId is { } eid && enrollments.TryGetValue(eid, out var enrollment))
            {
                studySiteId ??= enrollment.StudySiteId;
                subjectCode = enrollment.SubjectCode.Value;
            }

            var attestationId = lockAssertion.AttestationId ?? AttestationId.Empty;
            var meaning = attestations.TryGetValue(attestationId, out var att) ? att.Meaning : (AttestationMeaning?)null;
            releasesByLock.TryGetValue(lockAssertion.Id, out var release);

            result.Add(new StudyLockEntry(
                lockAssertion.Id,
                lockAssertion.StudyId,
                enrollmentId,
                studySiteId,
                subjectCode,
                (LockKind)lockAssertion.Kind,
                lockAssertion.Target,
                blinded,
                attestationId,
                meaning,
                lockAssertion.AssertedAt,
                lockAssertion.AssertedBy,
                lockAssertion.ReasonForChange,
                release is not null,
                release?.Id,
                release?.AttestationId,
                release?.AssertedAt,
                release?.AssertedBy,
                release?.ReasonForChange));
        }

        return result;
    }

    internal static async Task<HashSet<ObservationId>> ResolveBlindedObservationsAsync(
        KernelDbContext db,
        StudyId studyId,
        IEnumerable<Observation> observations,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null)
    {
        var obsList = observations.ToList();
        if (obsList.Count == 0)
        {
            return [];
        }

        var obsIds = obsList.Select(o => o.Id).ToList();
        var instances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.StudyId == studyId
                && (validAt == null || c.CollectedAt <= validAt.Value)
                && (knownAt == null || c.OpenedAt <= knownAt.Value)
                && c.Members.Any(m => obsIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ProtocolDesign> designs;
        if (validAt is { } vAt && knownAt is { } kAt)
        {
            var referencedVersionIds = instances
                .Where(c => c.Members.Any(m => m.JoinedAt <= kAt && obsIds.Contains(m.ObservationId)))
                .Select(c => c.ProtocolVersionId)
                .ToHashSet();
            var protocols = await KernelExportReader.ReadProtocolsAsync(db, studyId, vAt, kAt, referencedVersionIds, cancellationToken)
                .ConfigureAwait(false);
            designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();
        }
        else
        {
            var versions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == studyId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            designs = versions
                .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
                .Where(d => d is not null)
                .Select(d => d!)
                .ToList();
        }

        var conceptIds = obsList.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var itemByObs = new Dictionary<ObservationId, (string SpecId, string ItemId)>();
        foreach (var instance in instances)
        {
            foreach (var member in instance.Members.Where(m => knownAt is null || m.JoinedAt <= knownAt.Value))
            {
                itemByObs[member.ObservationId] = (instance.CollectionSpecId, member.ItemId);
            }
        }

        var blinded = new HashSet<ObservationId>();
        foreach (var obs in obsList)
        {
            ObservationValue valueAtCut = obs.Value;
            if (knownAt is { } kCut && obs.Revisions.Count > 0)
            {
                var latestAtCut = obs.Revisions
                    .Where(r => r.RecordedAt <= kCut)
                    .OrderBy(r => r.Number)
                    .LastOrDefault();
                if (latestAtCut is null)
                {
                    continue;
                }

                valueAtCut = latestAtCut.Value;
            }

            concepts.TryGetValue(obs.ConceptId, out var concept);
            itemByObs.TryGetValue(obs.Id, out var membership);
            if (BlindedObservationClassifier.IsBlindedTreatment(
                    obs.ConceptId,
                    valueAtCut,
                    concept,
                    membership.SpecId,
                    membership.ItemId,
                    designs))
            {
                blinded.Add(obs.Id);
            }
        }

        return blinded;
    }

    private static int ScopeOrder(string kind)
    {
        for (var i = 0; i < LockScopes.Kinds.Count; i++)
        {
            if (string.Equals(LockScopes.Kinds[i], kind, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return LockScopes.Kinds.Count;
    }
}

/// <summary>
/// Classifies whether an observation records blinded treatment allocation, investigational intervention, or masked
/// drug dispensation according to protocol design elements and concept metadata (K-AUTH-4).
/// </summary>
internal static class BlindedObservationClassifier
{
    public static bool IsBlindedTreatment(
        ConceptId conceptId,
        ObservationValue valueAtCut,
        Concept? concept,
        string? collectionSpecId,
        string? itemId,
        IReadOnlyList<ProtocolDesign> designs) =>
        BlindedTreatmentClassifier.Instance.IsBlindedObservation(
            conceptId,
            valueAtCut,
            ToMetadata(concept),
            collectionSpecId,
            itemId,
            designs);

    public static BlindedConceptMetadata? ToMetadata(Concept? concept) =>
        concept is null
            ? null
            : new BlindedConceptMetadata(concept.ExternalCode, concept.PreferredLabel, concept.Definition);
}

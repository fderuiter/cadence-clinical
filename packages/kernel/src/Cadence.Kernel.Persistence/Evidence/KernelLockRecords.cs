using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads study locks and their release history from canonical kernel assertions (spec §10.5, ADR-2621).
/// </summary>
internal sealed class KernelLockRecords(KernelTenantDbContextFactory contexts, IClock clock) : ILockRecords
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

        var lockRows = new List<Assertion>();
        var lockStream = db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study
                && (a.Kind == AssertionKind.Frozen || a.Kind == AssertionKind.Locked)
                && a.AssertedAt <= knownAt)
            .AsAsyncEnumerable();
        await foreach (var row in lockStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            lockRows.Add(row);
        }

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
            .OrderBy(e => LockScopes.OrderOf(e.Scope.Kind))
            .ThenBy(e => e.LockedAt)
            .ThenBy(e => e.LockId)
            .ToList();

        return new StudyLockList(foundStudy.Id, foundStudy.StudyCode, validAt, knownAt, ordered);
    }

    public async Task<LockReadinessSnapshot?> ReadReadinessAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var db = contexts.Create(tenant);
        var foundStudy = await db.Studies.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == study, cancellationToken)
            .ConfigureAwait(false);
        if (foundStudy is null || !await LockReadinessPersistenceReader.ScopeExistsInStudyAsync(db, study, scope, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await LockReadinessPersistenceReader.EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LockApplyPrecheck> PrecheckApplyAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        LockKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var db = contexts.Create(tenant);
        var foundStudy = await db.Studies.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == study, cancellationToken)
            .ConfigureAwait(false);
        if (foundStudy is null || !await LockReadinessPersistenceReader.ScopeExistsInStudyAsync(db, study, scope, cancellationToken).ConfigureAwait(false))
        {
            return new LockApplyPrecheck(ScopeExistsInStudy: false, AlreadyLocked: false, Readiness: null);
        }

        var targetAssertionKind = (AssertionKind)kind;
        var appliedAtScope = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study
                && a.Kind == targetAssertionKind
                && a.RetractsId == null
                && a.TargetKind == scope.Kind
                && a.TargetId == scope.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var alreadyLocked = false;
        if (appliedAtScope.Count > 0)
        {
            var retracted = await db.Assertions.AsNoTracking()
                .Where(a => a.RetractsId != null && appliedAtScope.Contains(a.RetractsId.Value))
                .Select(a => a.RetractsId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var retractedSet = retracted.ToHashSet();
            alreadyLocked = appliedAtScope.Exists(id => !retractedSet.Contains(id));
        }

        if (alreadyLocked || kind != LockKind.Locked)
        {
            return new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: alreadyLocked, Readiness: null);
        }

        var readiness = await LockReadinessPersistenceReader.EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        return new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: false, Readiness: readiness);
    }

    public Task<ScopeWriteLockCheck> CheckScopeWriteLockAsync(
        TenantId tenant,
        string studyCodeOrId,
        string? siteCodeOrId = null,
        string? subjectCode = null,
        Guid? enrollmentId = null,
        CancellationToken cancellationToken = default) =>
        ScopeWriteLockProbeReader.CheckScopeWriteLockAsync(contexts, tenant, studyCodeOrId, siteCodeOrId, subjectCode, enrollmentId, cancellationToken);

    public Task<BatchScopeWriteLockCheck> CheckBatchScopeWriteLocksAsync(
        TenantId tenant,
        string studyCodeOrId,
        IReadOnlyList<string> siteCodesOrIds,
        CancellationToken cancellationToken = default) =>
        ScopeWriteLockProbeReader.CheckBatchScopeWriteLocksAsync(contexts, tenant, studyCodeOrId, siteCodesOrIds, cancellationToken);

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

        var blindedObservationIds = await BlindedObservationClassifier.ResolveBlindedObservationsAsync(
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
                    else if (validAt is not null || knownAt is not null)
                    {
                        continue;
                    }

                    break;
                case NodeKinds.Observation:
                    var oid = ObservationId.FromGuid(lockAssertion.TargetId);
                    blinded = blindedObservationIds.Contains(oid);
                    if (observations.TryGetValue(oid, out var obs))
                    {
                        enrollmentId ??= obs.EnrollmentId;
                    }
                    else if (validAt is not null || knownAt is not null)
                    {
                        continue;
                    }

                    break;
            }

            string? subjectCode = null;
            if (enrollmentId is { } eid)
            {
                if (enrollments.TryGetValue(eid, out var enrollment))
                {
                    studySiteId ??= enrollment.StudySiteId;
                    subjectCode = enrollment.SubjectCode.Value;
                }
                else if (validAt is not null || knownAt is not null)
                {
                    continue;
                }
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

    internal static Task<HashSet<ObservationId>> ResolveBlindedObservationsAsync(
        KernelDbContext db,
        StudyId studyId,
        IEnumerable<Observation> observations,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null) =>
        BlindedObservationClassifier.ResolveBlindedObservationsAsync(db, studyId, observations, cancellationToken, validAt, knownAt);
}

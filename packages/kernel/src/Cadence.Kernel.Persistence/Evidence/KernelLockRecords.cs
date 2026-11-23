using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Evidence;
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

        var hydrated = await HydrateAsync(db, study, applied, releasesByLock, cancellationToken).ConfigureAwait(false);
        var ordered = hydrated
            .OrderBy(e => ScopeOrder(e.Scope.Kind))
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
        if (foundStudy is null || !await ScopeExistsInStudyAsync(db, study, scope, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, cancellationToken).ConfigureAwait(false);
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
        if (foundStudy is null || !await ScopeExistsInStudyAsync(db, study, scope, cancellationToken).ConfigureAwait(false))
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

        var readiness = await EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, cancellationToken).ConfigureAwait(false);
        return new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: false, Readiness: readiness);
    }

    private static async Task<bool> ScopeExistsInStudyAsync(
        KernelDbContext db,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        scope.Kind switch
        {
            NodeKinds.Study => StudyId.FromGuid(scope.Id) == study,
            NodeKinds.StudySite => await db.StudySites.AsNoTracking()
                .AnyAsync(s => s.Id == StudySiteId.FromGuid(scope.Id) && s.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.Enrollment => await db.Enrollments.AsNoTracking()
                .AnyAsync(e => e.Id == EnrollmentId.FromGuid(scope.Id) && e.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.CollectionInstance => await db.CollectionInstances.AsNoTracking()
                .AnyAsync(c => c.Id == CollectionInstanceId.FromGuid(scope.Id) && c.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.Observation => await db.Observations.AsNoTracking()
                .AnyAsync(o => o.Id == ObservationId.FromGuid(scope.Id) && o.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            _ => false,
        };

    private static async Task<LockReadinessSnapshot> EvaluateReadinessAsync(
        KernelDbContext db,
        StudyId study,
        string studyCode,
        NodeRef scope,
        CancellationToken cancellationToken)
    {
        List<CollectionInstance> instances;
        List<Observation> coveredObservations;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;

            case NodeKinds.StudySite:
                var siteId = StudySiteId.FromGuid(scope.Id);
                var siteEnrollmentIds = await db.Enrollments.AsNoTracking()
                    .Where(e => e.StudyId == study && e.StudySiteId == siteId)
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.StudySiteId == siteId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = siteEnrollmentIds.Count == 0
                    ? []
                    : await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && siteEnrollmentIds.Contains(o.EnrollmentId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                break;

            case NodeKinds.Enrollment:
                var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.EnrollmentId == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study && o.EnrollmentId == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;

            case NodeKinds.CollectionInstance:
                var instanceId = CollectionInstanceId.FromGuid(scope.Id);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.Id == instanceId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var memberIds = instances.SelectMany(i => i.Members.Select(m => m.ObservationId)).Distinct().ToList();
                coveredObservations = memberIds.Count == 0
                    ? []
                    : await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && memberIds.Contains(o.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                break;

            default:
                instances = [];
                var observationId = ObservationId.FromGuid(scope.Id);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study && o.Id == observationId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
        }

        var obsById = coveredObservations.ToDictionary(o => o.Id);
        var missingMemberIds = instances
            .SelectMany(i => i.Members.Select(m => m.ObservationId))
            .Where(id => !obsById.ContainsKey(id))
            .Distinct()
            .ToList();
        if (missingMemberIds.Count > 0)
        {
            var extraObs = await db.Observations.AsNoTracking()
                .Where(o => missingMemberIds.Contains(o.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var extra in extraObs)
            {
                obsById[extra.Id] = extra;
            }
        }

        var enrollmentIds = instances.Select(i => i.EnrollmentId)
            .Concat(coveredObservations.Select(o => o.EnrollmentId))
            .Distinct()
            .ToList();
        var enrollmentsById = enrollmentIds.Count == 0
            ? new Dictionary<EnrollmentId, Enrollment>()
            : await db.Enrollments.AsNoTracking()
                .Where(e => enrollmentIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, cancellationToken)
                .ConfigureAwait(false);

        var versionIds = instances.Select(i => i.ProtocolVersionId).Distinct().ToList();
        var versionsById = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, ProtocolVersion>()
            : await db.ProtocolVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, cancellationToken)
                .ConfigureAwait(false);

        List<Assertion> openQueries = [];
        HashSet<AssertionId> answeredQueryIds = [];
        if (coveredObservations.Count > 0)
        {
            var targetGuids = coveredObservations.Select(o => o.Id.Value).ToList();
            var candidateQueries = await db.Assertions.AsNoTracking()
                .Where(a => a.StudyId == study
                    && a.Kind == AssertionKind.Queried
                    && a.RetractsId == null
                    && a.TargetKind == NodeKinds.Observation
                    && targetGuids.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (candidateQueries.Count > 0)
            {
                var queryIds = candidateQueries.Select(q => q.Id).ToList();
                var retractedQueries = await db.Assertions.AsNoTracking()
                    .Where(a => a.RetractsId != null && queryIds.Contains(a.RetractsId.Value))
                    .Select(a => a.RetractsId!.Value)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var retractedSet = retractedQueries.ToHashSet();
                openQueries = [.. candidateQueries.Where(q => !retractedSet.Contains(q.Id))];

                if (openQueries.Count > 0)
                {
                    var openGuids = openQueries.Select(q => q.Id.Value).ToList();
                    var candidateAnswers = await db.Assertions.AsNoTracking()
                        .Where(a => a.StudyId == study
                            && a.Kind == AssertionKind.QueryAnswered
                            && a.RetractsId == null
                            && a.TargetKind == NodeKinds.Assertion
                            && openGuids.Contains(a.TargetId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (candidateAnswers.Count > 0)
                    {
                        var answerIds = candidateAnswers.Select(a => a.Id).ToList();
                        var retractedAnswers = await db.Assertions.AsNoTracking()
                            .Where(a => a.RetractsId != null && answerIds.Contains(a.RetractsId.Value))
                            .Select(a => a.RetractsId!.Value)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var retractedAnswerSet = retractedAnswers.ToHashSet();
                        answeredQueryIds = candidateAnswers
                            .Where(a => !retractedAnswerSet.Contains(a.Id))
                            .Select(a => AssertionId.FromGuid(a.TargetId))
                            .ToHashSet();
                    }
                }
            }
        }

        var queriedObservations = openQueries
            .Select(q => ObservationId.FromGuid(q.TargetId))
            .Where(obsById.ContainsKey)
            .Select(id => obsById[id])
            .DistinctBy(o => o.Id)
            .ToList();
        var blindedObservations = queriedObservations.Count == 0
            ? new HashSet<ObservationId>()
            : await ResolveBlindedObservationsAsync(db, study, queriedObservations, cancellationToken).ConfigureAwait(false);

        // When evaluating a narrower scope whose observations belong to CollectionInstances not loaded above (e.g. a single observation),
        // load its containing CollectionInstance so the query blocker names its form and item prompt when present.
        IReadOnlyList<CollectionInstance>? containingInstances = null;
        if (scope.Kind == NodeKinds.Observation && openQueries.Count > 0)
        {
            var singleObsId = ObservationId.FromGuid(scope.Id);
            var containing = await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == study && c.Members.Any(m => m.ObservationId == singleObsId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (containing.Count > 0)
            {
                containingInstances = containing;
                var missingVersions = containing.Select(c => c.ProtocolVersionId).Where(id => !versionsById.ContainsKey(id)).Distinct().ToList();
                if (missingVersions.Count > 0)
                {
                    var extraVersions = await db.ProtocolVersions.AsNoTracking()
                        .Where(v => missingVersions.Contains(v.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var v in extraVersions)
                    {
                        versionsById[v.Id] = v;
                    }
                }
            }
        }

        return LockReadinessEvaluator.BuildSnapshot(
            study,
            studyCode,
            scope,
            instances,
            obsById,
            enrollmentsById,
            versionsById,
            openQueries,
            answeredQueryIds,
            blindedObservations,
            containingInstances);
    }

    internal static async Task<IReadOnlyList<StudyLockEntry>> HydrateAsync(
        KernelDbContext db,
        StudyId studyId,
        IReadOnlyList<Assertion> applied,
        IReadOnlyDictionary<AssertionId, Assertion> releasesByLock,
        CancellationToken cancellationToken)
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
                .Where(a => attestationIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken)
                .ConfigureAwait(false);

        var observationScopeIds = applied
            .Where(a => a.TargetKind == NodeKinds.Observation)
            .Select(a => ObservationId.FromGuid(a.TargetId))
            .Distinct()
            .ToList();

        var observations = observationScopeIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : await db.Observations.AsNoTracking()
                .Where(o => observationScopeIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var collectionScopeIds = applied
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance)
            .Select(a => CollectionInstanceId.FromGuid(a.TargetId))
            .Distinct()
            .ToList();

        var collections = collectionScopeIds.Count == 0
            ? new Dictionary<CollectionInstanceId, CollectionInstance>()
            : await db.CollectionInstances.AsNoTracking()
                .Where(c => collectionScopeIds.Contains(c.Id))
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

        var enrollments = enrollmentIds.Count == 0
            ? new Dictionary<EnrollmentId, Enrollment>()
            : await db.Enrollments.AsNoTracking()
                .Where(e => enrollmentIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, cancellationToken)
                .ConfigureAwait(false);

        var blindedObservationIds = await ResolveBlindedObservationsAsync(db, studyId, observations.Values, cancellationToken).ConfigureAwait(false);

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
        CancellationToken cancellationToken)
    {
        var obsList = observations.ToList();
        if (obsList.Count == 0)
        {
            return [];
        }

        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == studyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var designs = versions
            .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        var conceptIds = obsList.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var obsIds = obsList.Select(o => o.Id).ToList();
        var instances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.StudyId == studyId && c.Members.Any(m => obsIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemByObs = new Dictionary<ObservationId, (string SpecId, string ItemId)>();
        foreach (var instance in instances)
        {
            foreach (var member in instance.Members)
            {
                itemByObs[member.ObservationId] = (instance.CollectionSpecId, member.ItemId);
            }
        }

        var blinded = new HashSet<ObservationId>();
        foreach (var obs in obsList)
        {
            concepts.TryGetValue(obs.ConceptId, out var concept);
            itemByObs.TryGetValue(obs.Id, out var membership);
            if (BlindedObservationClassifier.IsBlindedTreatment(
                    obs.ConceptId,
                    obs.Value,
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

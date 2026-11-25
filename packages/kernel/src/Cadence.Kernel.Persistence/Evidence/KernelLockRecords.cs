using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

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
        if (foundStudy is null || !await ScopeExistsInStudyAsync(db, study, scope, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, clock.UtcNow, cancellationToken).ConfigureAwait(false);
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

        var readiness = await EvaluateReadinessAsync(db, foundStudy.Id, foundStudy.StudyCode, scope, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: false, Readiness: readiness);
    }

    public async Task<ScopeWriteLockCheck> CheckScopeWriteLockAsync(
        TenantId tenant,
        string studyCodeOrId,
        string? siteCodeOrId = null,
        string? subjectCode = null,
        Guid? enrollmentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCodeOrId))
        {
            return new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        var trimmedStudy = studyCodeOrId.Trim();
        await using var db = contexts.Create(tenant);

        Study? foundStudy;
        if (NodeRef.TryParse(trimmedStudy, out var studyNode) && studyNode.Kind == NodeKinds.Study)
        {
            var parsedStudyId = StudyId.FromGuid(studyNode.Id);
            foundStudy = await db.Studies.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == parsedStudyId, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (Guid.TryParse(trimmedStudy, out var rawStudyGuid))
        {
            var parsedStudyId = StudyId.FromGuid(rawStudyGuid);
            foundStudy = await db.Studies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == parsedStudyId || s.StudyCode == trimmedStudy, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            foundStudy = await db.Studies.AsNoTracking()
                .SingleOrDefaultAsync(s => s.StudyCode == trimmedStudy, cancellationToken)
                .ConfigureAwait(false);
        }

        if (foundStudy is null)
        {
            return new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        var study = foundStudy.Id;
        var hasExplicitSite = !string.IsNullOrWhiteSpace(siteCodeOrId);
        StudySiteId? resolvedStudySiteId = null;
        if (hasExplicitSite)
        {
            var trimmedSite = siteCodeOrId!.Trim();
            if (NodeRef.TryParse(trimmedSite, out var siteNode) && siteNode.Kind == NodeKinds.StudySite)
            {
                var candidateId = StudySiteId.FromGuid(siteNode.Id);
                var exists = await db.StudySites.AsNoTracking()
                    .AnyAsync(s => s.StudyId == study && s.Id == candidateId, cancellationToken)
                    .ConfigureAwait(false);
                if (exists)
                {
                    resolvedStudySiteId = candidateId;
                }
            }
            else if (Guid.TryParse(trimmedSite, out var rawSiteGuid))
            {
                var asStudySite = StudySiteId.FromGuid(rawSiteGuid);
                var asSite = SiteId.FromGuid(rawSiteGuid);
                resolvedStudySiteId = await db.StudySites.AsNoTracking()
                    .Where(s => s.StudyId == study && (s.Id == asStudySite || s.SiteId == asSite))
                    .Select(s => (StudySiteId?)s.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (resolvedStudySiteId is null)
            {
                resolvedStudySiteId = await (
                        from ss in db.StudySites.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                        where ss.StudyId == study && s.SiteCode == trimmedSite
                        select (StudySiteId?)ss.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        EnrollmentId? resolvedEnrollmentId = null;
        StudySiteId? enrollmentStudySiteId = null;
        if (enrollmentId is { } givenEnrollment && givenEnrollment != Guid.Empty)
        {
            var candidateEnrollmentId = EnrollmentId.FromGuid(givenEnrollment);
            var matchedEnrollment = await db.Enrollments.AsNoTracking()
                .Where(e => e.StudyId == study && e.Id == candidateEnrollmentId)
                .Select(e => new { e.Id, e.StudySiteId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (matchedEnrollment is not null)
            {
                resolvedEnrollmentId = matchedEnrollment.Id;
                enrollmentStudySiteId = matchedEnrollment.StudySiteId;
                if (!hasExplicitSite)
                {
                    resolvedStudySiteId = matchedEnrollment.StudySiteId;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(subjectCode) && SubjectCode.TryFrom(subjectCode.Trim(), out var parsedSubjectCode))
        {
            var matchedEnrollment = await db.Enrollments.AsNoTracking()
                .Where(e => e.StudyId == study && e.SubjectCode == parsedSubjectCode)
                .Select(e => new { e.Id, e.StudySiteId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (matchedEnrollment is not null)
            {
                resolvedEnrollmentId = matchedEnrollment.Id;
                enrollmentStudySiteId = matchedEnrollment.StudySiteId;
                if (!hasExplicitSite)
                {
                    resolvedStudySiteId = matchedEnrollment.StudySiteId;
                }
            }
        }

        var coveringScopes = new List<NodeRef> { NodeRef.For(study) };
        if (resolvedStudySiteId is { } ssid)
        {
            coveringScopes.Add(NodeRef.For(ssid));
        }

        if (enrollmentStudySiteId is { } esid && esid != resolvedStudySiteId)
        {
            coveringScopes.Add(NodeRef.For(esid));
        }

        if (resolvedEnrollmentId is { } eid)
        {
            coveringScopes.Add(NodeRef.For(eid));
        }

        var targetGuids = coveringScopes.Select(s => s.Id).ToList();
        var candidateLocks = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study
                && (a.Kind == AssertionKind.Frozen || a.Kind == AssertionKind.Locked)
                && a.RetractsId == null
                && targetGuids.Contains(a.TargetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        candidateLocks = [.. candidateLocks.Where(a => coveringScopes.Any(s => s.Kind == a.TargetKind && s.Id == a.TargetId))];
        if (candidateLocks.Count == 0)
        {
            return new ScopeWriteLockCheck(
                StudyExists: true,
                Locked: false,
                LockKind: null,
                ScopeKind: null,
                StudyId: study,
                StudySiteId: resolvedStudySiteId,
                EnrollmentId: resolvedEnrollmentId,
                CoveringLocks: [],
                EnrollmentStudySiteId: enrollmentStudySiteId);
        }

        var candidateIds = candidateLocks.Select(l => l.Id).ToList();
        var retracted = await db.Assertions.AsNoTracking()
            .Where(r => r.RetractsId != null && candidateIds.Contains(r.RetractsId.Value))
            .Select(r => r.RetractsId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var retractedSet = retracted.ToHashSet();
        var activeCandidates = candidateLocks
            .Where(l => !retractedSet.Contains(l.Id))
            .OrderByDescending(l => l.Kind == AssertionKind.Locked)
            .ThenBy(l => LockScopes.OrderOf(l.TargetKind))
            .Select(l => new ScopeWriteLockCandidate((LockKind)l.Kind, new NodeRef(l.TargetKind, l.TargetId)))
            .ToList();
        var active = activeCandidates.FirstOrDefault();
        if (active is null)
        {
            return new ScopeWriteLockCheck(
                StudyExists: true,
                Locked: false,
                LockKind: null,
                ScopeKind: null,
                StudyId: study,
                StudySiteId: resolvedStudySiteId,
                EnrollmentId: resolvedEnrollmentId,
                CoveringLocks: [],
                EnrollmentStudySiteId: enrollmentStudySiteId);
        }

        return new ScopeWriteLockCheck(
            StudyExists: true,
            Locked: true,
            LockKind: active.Kind,
            ScopeKind: active.Scope.Kind,
            StudyId: study,
            StudySiteId: resolvedStudySiteId,
            EnrollmentId: resolvedEnrollmentId,
            CoveringLocks: activeCandidates,
            EnrollmentStudySiteId: enrollmentStudySiteId);
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
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        List<CollectionInstance> instances;
        List<Observation> coveredObservations;
        List<Enrollment>? scopedEnrollments = null;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
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
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study && e.StudySiteId == siteId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var siteEnrollmentIds = scopedEnrollments.Select(e => e.Id).ToList();
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
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study && e.Id == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
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

        Dictionary<EnrollmentId, Enrollment> enrollmentsById;
        if (scopedEnrollments is not null)
        {
            enrollmentsById = scopedEnrollments.ToDictionary(e => e.Id);
            var extraEnrollmentIds = instances.Select(i => i.EnrollmentId)
                .Concat(coveredObservations.Select(o => o.EnrollmentId))
                .Where(id => !enrollmentsById.ContainsKey(id))
                .Distinct()
                .ToList();
            if (extraEnrollmentIds.Count > 0)
            {
                var extraEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => extraEnrollmentIds.Contains(e.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var extra in extraEnrollments)
                {
                    enrollmentsById[extra.Id] = extra;
                }
            }
        }
        else
        {
            var enrollmentIds = instances.Select(i => i.EnrollmentId)
                .Concat(coveredObservations.Select(o => o.EnrollmentId))
                .Distinct()
                .ToList();
            enrollmentsById = enrollmentIds.Count == 0
                ? new Dictionary<EnrollmentId, Enrollment>()
                : await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => enrollmentIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, cancellationToken)
                    .ConfigureAwait(false);
        }

        var versionIds = instances.Select(i => i.ProtocolVersionId).Distinct().ToList();
        var versionsById = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, ProtocolVersion>()
            : await db.ProtocolVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, cancellationToken)
                .ConfigureAwait(false);

        List<Assertion> openQueries = [];
        HashSet<AssertionId> answeredQueryIds = [];
        HashSet<(ObservationId ObservationId, int Revision)> verifiedRevisions = [];
        if (coveredObservations.Count > 0)
        {
            var targetGuids = coveredObservations.Select(o => o.Id.Value).ToList();
            var candidateAssertions = await db.Assertions.AsNoTracking()
                .Where(a => a.StudyId == study
                    && (a.Kind == AssertionKind.Queried || a.Kind == AssertionKind.Verified)
                    && a.RetractsId == null
                    && a.TargetKind == NodeKinds.Observation
                    && targetGuids.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (candidateAssertions.Count > 0)
            {
                var assertionIds = candidateAssertions.Select(a => a.Id).ToList();
                var retractedAssertions = await db.Assertions.AsNoTracking()
                    .Where(a => a.RetractsId != null && assertionIds.Contains(a.RetractsId.Value))
                    .Select(a => a.RetractsId!.Value)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var retractedSet = retractedAssertions.ToHashSet();

                openQueries = [.. candidateAssertions.Where(a => a.Kind == AssertionKind.Queried && !retractedSet.Contains(a.Id))];
                verifiedRevisions = candidateAssertions
                    .Where(a => a.Kind == AssertionKind.Verified && !retractedSet.Contains(a.Id) && a.TargetRevision is not null)
                    .Select(a => (ObservationId.FromGuid(a.TargetId), a.TargetRevision!.Value))
                    .ToHashSet();

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

        Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>> formSignaturesByInstance = [];
        if (instances.Count > 0)
        {
            var instanceGuids = instances.Select(i => i.Id.Value).ToList();
            var formAttestations = await db.Attestations.AsNoTracking()
                .Where(a => a.TargetKind == NodeKinds.CollectionInstance
                    && instanceGuids.Contains(a.TargetId)
                    && (a.Meaning == AttestationMeaning.Approved || a.Meaning == AttestationMeaning.Responsibility))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            formSignaturesByInstance = formAttestations
                .GroupBy(a => CollectionInstanceId.FromGuid(a.TargetId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Attestation>)g.ToList());
        }

        Dictionary<StudySiteId, StudySite>? studySitesById = null;
        Dictionary<SiteId, Site>? sitesById = null;
        IReadOnlyList<Effectivity>? studyEffectivities = null;
        if (scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
            && enrollmentsById.Values.Any(LockReadinessEvaluator.NeedsScheduleEvaluation))
        {
            var scheduleStudySiteIds = enrollmentsById.Values
                .Where(LockReadinessEvaluator.NeedsScheduleEvaluation)
                .Select(e => e.StudySiteId)
                .Distinct()
                .ToList();
            studySitesById = await db.StudySites.AsNoTracking()
                .Where(s => scheduleStudySiteIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken)
                .ConfigureAwait(false);
            var rawSiteIds = studySitesById.Values.Select(s => s.SiteId).Distinct().ToList();
            sitesById = rawSiteIds.Count == 0
                ? new Dictionary<SiteId, Site>()
                : await db.Sites.AsNoTracking()
                    .Where(s => rawSiteIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken)
                    .ConfigureAwait(false);
            studyEffectivities = await db.Effectivities.AsNoTracking()
                .Include(e => e.Periods)
                .Where(e => e.StudyId == study)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var extraScheduleVersions = studyEffectivities
                .Select(e => e.ProtocolVersionId)
                .Where(id => !versionsById.ContainsKey(id))
                .Distinct()
                .ToList();
            if (extraScheduleVersions.Count > 0)
            {
                var loadedVersions = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => extraScheduleVersions.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var v in loadedVersions)
                {
                    versionsById[v.Id] = v;
                }
            }
        }

        var activeErrorFindings = await KernelValidationStore.LoadActiveErrorFindingsAsync(db, study, cancellationToken)
            .ConfigureAwait(false);

        // When evaluating a narrower scope whose observations belong to CollectionInstances not loaded above (e.g. a single observation
        // or a single form referencing cross-form edit checks), load the enrollment's CollectionInstances so blockers and blinding resolve accurately.
        IReadOnlyList<CollectionInstance>? containingInstances = null;
        if (scope.Kind == NodeKinds.Observation && coveredObservations.Count > 0)
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
        else if (scope.Kind == NodeKinds.CollectionInstance && activeErrorFindings.Count > 0 && enrollmentsById.Count > 0)
        {
            var siblingEnrollmentIds = enrollmentsById.Keys.ToList();
            var siblingInstances = await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == study && siblingEnrollmentIds.Contains(c.EnrollmentId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (siblingInstances.Count > instances.Count)
            {
                containingInstances = siblingInstances;
                var extraSiblingObsIds = siblingInstances
                    .SelectMany(c => c.Members.Select(m => m.ObservationId))
                    .Where(id => !obsById.ContainsKey(id))
                    .Distinct()
                    .ToList();
                if (extraSiblingObsIds.Count > 0)
                {
                    var extraSiblingObs = await db.Observations.AsNoTracking()
                        .Where(o => extraSiblingObsIds.Contains(o.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var extra in extraSiblingObs)
                    {
                        obsById[extra.Id] = extra;
                    }
                }

                var missingVersions = siblingInstances.Select(c => c.ProtocolVersionId).Where(id => !versionsById.ContainsKey(id)).Distinct().ToList();
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

        var unverifiedObservationIds = coveredObservations
            .Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked } && !verifiedRevisions.Contains((o.Id, o.Revision)))
            .Select(o => o.Id);

        var candidateBlindedObservations = openQueries
            .Select(q => ObservationId.FromGuid(q.TargetId))
            .Concat(unverifiedObservationIds)
            .Concat(activeErrorFindings
                .Select(f => f.Target.Node is { Kind: NodeKinds.Observation } obsNode ? ObservationId.FromGuid(obsNode.Id) : ObservationId.Empty)
                .Where(id => !id.IsEmpty))
            .Concat(activeErrorFindings.Count > 0
                ? coveredObservations.Select(o => o.Id)
                    .Concat((containingInstances ?? instances).SelectMany(i => i.Members.Select(m => m.ObservationId)))
                : [])
            .Where(obsById.ContainsKey)
            .Select(id => obsById[id])
            .DistinctBy(o => o.Id)
            .ToList();
        var blindedObservations = candidateBlindedObservations.Count == 0
            ? new HashSet<ObservationId>()
            : await ResolveBlindedObservationsAsync(db, study, candidateBlindedObservations, cancellationToken).ConfigureAwait(false);

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
            containingInstances,
            activeErrorFindings,
            verifiedRevisions,
            formSignaturesByInstance,
            evaluatedAt,
            studySitesById,
            sitesById,
            studyEffectivities);
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
        var obsIdSet = obsIds.ToHashSet();
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
                .Where(c => c.Members.Any(m => m.JoinedAt <= kAt && obsIdSet.Contains(m.ObservationId)))
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

        var blindingLookup = BlindedObservationClassifier.CreateLookup(concepts, designs);
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

            itemByObs.TryGetValue(obs.Id, out var membership);
            if (blindingLookup.IsBlindedTreatment(
                    obs.ConceptId,
                    valueAtCut,
                    membership.SpecId,
                    membership.ItemId))
            {
                blinded.Add(obs.Id);
            }
        }

        return blinded;
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

    public static BlindedObservationLookup CreateLookup(
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyList<ProtocolDesign> designs) =>
        new(conceptsById, designs);
}

internal sealed class BlindedObservationLookup(
    IReadOnlyDictionary<ConceptId, Concept> conceptsById,
    IReadOnlyList<ProtocolDesign> designs)
{
    private readonly Dictionary<BlindingKey, bool> _cache = new(BlindingKeyComparer.Instance);
    private readonly Dictionary<ConceptId, BlindedConceptMetadata?> _metadataByConcept = [];

    public bool IsBlindedTreatment(
        ConceptId conceptId,
        ObservationValue valueAtCut,
        string? collectionSpecId,
        string? itemId)
    {
        // Check per-observation NullFlavor.Masked before consulting or populating the memo cache so a masked
        // value never marks unmasked siblings on the same (ConceptId, CollectionSpecId, ItemId) as blinded.
        if (valueAtCut is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        var key = new BlindingKey(conceptId, collectionSpecId, itemId);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!_metadataByConcept.TryGetValue(conceptId, out var metadata))
        {
            conceptsById.TryGetValue(conceptId, out var concept);
            metadata = BlindedObservationClassifier.ToMetadata(concept);
            _metadataByConcept[conceptId] = metadata;
        }

        var isBlinded = BlindedTreatmentClassifier.Instance.IsBlindedObservation(
            conceptId,
            value: null,
            metadata,
            collectionSpecId,
            itemId,
            designs);
        _cache[key] = isBlinded;
        return isBlinded;
    }

    private readonly record struct BlindingKey(ConceptId ConceptId, string? CollectionSpecId, string? ItemId);

    private sealed class BlindingKeyComparer : IEqualityComparer<BlindingKey>
    {
        public static readonly BlindingKeyComparer Instance = new();

        public bool Equals(BlindingKey x, BlindingKey y) =>
            x.ConceptId == y.ConceptId
            && string.Equals(x.CollectionSpecId, y.CollectionSpecId, StringComparison.Ordinal)
            && string.Equals(x.ItemId, y.ItemId, StringComparison.Ordinal);

        public int GetHashCode(BlindingKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.ConceptId);
            hash.Add(obj.CollectionSpecId, StringComparer.Ordinal);
            hash.Add(obj.ItemId, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}

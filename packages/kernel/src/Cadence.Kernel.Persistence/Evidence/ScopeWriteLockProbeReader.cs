using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class ScopeWriteLockProbeReader
{
    internal static async Task<ScopeWriteLockCheck> CheckScopeWriteLockAsync(
        KernelTenantDbContextFactory contexts,
        TenantId tenant,
        string studyCodeOrId,
        string? siteCodeOrId,
        string? subjectCode,
        Guid? enrollmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCodeOrId))
        {
            return new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        var trimmedStudy = studyCodeOrId.Trim();
        await using var db = contexts.Create(tenant);

        Study? foundStudy = await FindStudyAsync(db, trimmedStudy, cancellationToken).ConfigureAwait(false);
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

        var activeCandidates = await LoadActiveCoveringLocksAsync(db, study, coveringScopes, cancellationToken).ConfigureAwait(false);
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

    internal static async Task<BatchScopeWriteLockCheck> CheckBatchScopeWriteLocksAsync(
        KernelTenantDbContextFactory contexts,
        TenantId tenant,
        string studyCodeOrId,
        IReadOnlyList<string> siteCodesOrIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteCodesOrIds);
        if (string.IsNullOrWhiteSpace(studyCodeOrId))
        {
            return new BatchScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        var trimmedStudy = studyCodeOrId.Trim();
        await using var db = contexts.Create(tenant);

        Study? foundStudy = await FindStudyAsync(db, trimmedStudy, cancellationToken).ConfigureAwait(false);
        if (foundStudy is null)
        {
            return new BatchScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        var study = foundStudy.Id;
        var distinctTokens = new List<string>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var candidateStudySiteIds = new List<StudySiteId>();
        var candidateRawSiteIds = new List<SiteId>();
        var candidatePlainCodes = new List<string>();

        foreach (string rawToken in siteCodesOrIds)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                continue;
            }

            string trimmedSite = rawToken.Trim();
            if (!seenTokens.Add(trimmedSite))
            {
                continue;
            }

            distinctTokens.Add(trimmedSite);
            if (NodeRef.TryParse(trimmedSite, out var siteNode) && siteNode.Kind == NodeKinds.StudySite)
            {
                candidateStudySiteIds.Add(StudySiteId.FromGuid(siteNode.Id));
            }
            else if (Guid.TryParse(trimmedSite, out var rawSiteGuid))
            {
                candidateStudySiteIds.Add(StudySiteId.FromGuid(rawSiteGuid));
                candidateRawSiteIds.Add(SiteId.FromGuid(rawSiteGuid));
            }

            candidatePlainCodes.Add(trimmedSite);
        }

        var resolvedStudySites = new Dictionary<string, StudySiteId>(StringComparer.Ordinal);
        if (distinctTokens.Count > 0)
        {
            var matchedStudySites = await (
                    from ss in db.StudySites.AsNoTracking()
                    join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                    where ss.StudyId == study
                        && (candidateStudySiteIds.Contains(ss.Id)
                            || candidateRawSiteIds.Contains(ss.SiteId)
                            || candidatePlainCodes.Contains(s.SiteCode))
                    select new { ss.Id, ss.SiteId, s.SiteCode })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var byStudySiteId = new Dictionary<StudySiteId, StudySiteId>();
            var bySiteId = new Dictionary<SiteId, StudySiteId>();
            var bySiteCode = new Dictionary<string, StudySiteId>(StringComparer.Ordinal);
            foreach (var row in matchedStudySites)
            {
                byStudySiteId.TryAdd(row.Id, row.Id);
                bySiteId.TryAdd(row.SiteId, row.Id);
                bySiteCode.TryAdd(row.SiteCode, row.Id);
            }

            foreach (string token in distinctTokens)
            {
                StudySiteId? resolved = null;
                if (NodeRef.TryParse(token, out var siteNode) && siteNode.Kind == NodeKinds.StudySite)
                {
                    var candidateId = StudySiteId.FromGuid(siteNode.Id);
                    if (byStudySiteId.TryGetValue(candidateId, out var matchedByNode))
                    {
                        resolved = matchedByNode;
                    }
                }
                else if (Guid.TryParse(token, out var rawSiteGuid))
                {
                    if (byStudySiteId.TryGetValue(StudySiteId.FromGuid(rawSiteGuid), out var matchedAsStudySite))
                    {
                        resolved = matchedAsStudySite;
                    }
                    else if (bySiteId.TryGetValue(SiteId.FromGuid(rawSiteGuid), out var matchedAsSite))
                    {
                        resolved = matchedAsSite;
                    }
                }

                if (resolved is null && bySiteCode.TryGetValue(token, out var matchedByCode))
                {
                    resolved = matchedByCode;
                }

                if (resolved is { } studySiteId)
                {
                    resolvedStudySites[token] = studySiteId;
                }
            }
        }

        var coveringScopes = new List<NodeRef>(1 + resolvedStudySites.Count) { NodeRef.For(study) };
        var seenSiteIds = new HashSet<StudySiteId>();
        foreach (var resolvedSiteId in resolvedStudySites.Values)
        {
            if (seenSiteIds.Add(resolvedSiteId))
            {
                coveringScopes.Add(NodeRef.For(resolvedSiteId));
            }
        }

        var activeCandidates = await LoadActiveCoveringLocksAsync(db, study, coveringScopes, cancellationToken).ConfigureAwait(false);
        var active = activeCandidates.FirstOrDefault();
        if (active is null)
        {
            return new BatchScopeWriteLockCheck(
                StudyExists: true,
                Locked: false,
                LockKind: null,
                ScopeKind: null,
                StudyId: study,
                ResolvedStudySites: resolvedStudySites,
                CoveringLocks: []);
        }

        return new BatchScopeWriteLockCheck(
            StudyExists: true,
            Locked: true,
            LockKind: active.Kind,
            ScopeKind: active.Scope.Kind,
            StudyId: study,
            ResolvedStudySites: resolvedStudySites,
            CoveringLocks: activeCandidates);
    }

    private static async Task<Study?> FindStudyAsync(
        KernelDbContext db,
        string trimmedStudy,
        CancellationToken cancellationToken)
    {
        if (NodeRef.TryParse(trimmedStudy, out var studyNode) && studyNode.Kind == NodeKinds.Study)
        {
            var parsedStudyId = StudyId.FromGuid(studyNode.Id);
            return await db.Studies.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == parsedStudyId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (Guid.TryParse(trimmedStudy, out var rawStudyGuid))
        {
            var parsedStudyId = StudyId.FromGuid(rawStudyGuid);
            return await db.Studies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == parsedStudyId || s.StudyCode == trimmedStudy, cancellationToken)
                .ConfigureAwait(false);
        }

        return await db.Studies.AsNoTracking()
            .SingleOrDefaultAsync(s => s.StudyCode == trimmedStudy, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<List<ScopeWriteLockCandidate>> LoadActiveCoveringLocksAsync(
        KernelDbContext db,
        StudyId study,
        List<NodeRef> coveringScopes,
        CancellationToken cancellationToken)
    {
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
            return [];
        }

        var candidateIds = candidateLocks.Select(l => l.Id).ToList();
        var retracted = await db.Assertions.AsNoTracking()
            .Where(r => r.RetractsId != null && candidateIds.Contains(r.RetractsId.Value))
            .Select(r => r.RetractsId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var retractedSet = retracted.ToHashSet();
        return [.. candidateLocks
            .Where(l => !retractedSet.Contains(l.Id))
            .OrderByDescending(l => l.Kind == AssertionKind.Locked)
            .ThenBy(l => LockScopes.OrderOf(l.TargetKind))
            .Select(l => new ScopeWriteLockCandidate((LockKind)l.Kind, new NodeRef(l.TargetKind, l.TargetId)))];
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>The <see cref="KernelWorkItemSources.FormsAwaitingSignature"/> source: complete forms with recorded values that lack a current investigator signature.</summary>
internal sealed class FormsAwaitingSignatureSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.FormsAwaitingSignature;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var query = db.CollectionInstances.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.Members.Any());

        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                query = query.Where(c => db.Studies.Any(s => s.Id == c.StudyId && s.SponsorId == org));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                query = query.Where(c => c.StudyId == study);
                break;
            case { Kind: NodeKinds.StudySite }:
                var site = StudySiteId.FromGuid(within.Id);
                query = query.Where(c => c.StudySiteId == site);
                break;
            case { Kind: NodeKinds.Enrollment }:
                var enrollment = EnrollmentId.FromGuid(within.Id);
                query = query.Where(c => c.EnrollmentId == enrollment);
                break;
            case { Kind: NodeKinds.CollectionInstance }:
                var formId = CollectionInstanceId.FromGuid(within.Id);
                query = query.Where(c => c.Id == formId);
                break;
            default:
                return [];
        }

        var records = await KernelCollectionInstanceRecords.LoadRecordsAsync(db, query, cancellationToken).ConfigureAwait(false);
        var due = records
            .Where(r => r.Values.Count > 0 && r.Complete && !r.Attestations.Any(a => !a.Superseded))
            .ToList();
        if (due.Count == 0)
        {
            return [];
        }

        var enrollmentIds = due.Select(r => r.EnrollmentId).Distinct().ToList();
        var enrollmentRows = await db.Enrollments.AsNoTracking()
            .Where(e => enrollmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.SubjectCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var subjectsByEnrollment = enrollmentRows.ToDictionary(e => e.Id, e => e.SubjectCode.Value);

        return
        [
            .. due
                .Select(r =>
                {
                    var since = r.Values.Max(v => v.RecordedAt);
                    var formTitle = r.CollectionTitle ?? r.CollectionSpecId;
                    var title = subjectsByEnrollment.TryGetValue(r.EnrollmentId, out var code)
                        ? string.Create(CultureInfo.InvariantCulture, $"Sign {formTitle} for {code}")
                        : string.Create(CultureInfo.InvariantCulture, $"Sign {formTitle}");
                    return new WorkItemCandidate(NodeRef.For(r.CollectionInstanceId, r.Version), title, since);
                })
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

/// <summary>The <see cref="KernelWorkItemSources.ObservationsAwaitingVerification"/> source: recorded observations on unlocked scopes without active SDV at their current revision.</summary>
internal sealed class ObservationsAwaitingVerificationSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.ObservationsAwaitingVerification;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var query = db.Observations.AsNoTracking()
            .Where(o => o.TenantId == tenant);

        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                query = query.Where(o => db.Studies.Any(s => s.Id == o.StudyId && s.SponsorId == org));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                query = query.Where(o => o.StudyId == study);
                break;
            case { Kind: NodeKinds.StudySite }:
                var site = StudySiteId.FromGuid(within.Id);
                query = query.Where(o => db.Enrollments.Any(e => e.Id == o.EnrollmentId && e.StudySiteId == site));
                break;
            case { Kind: NodeKinds.Enrollment }:
                var enrollment = EnrollmentId.FromGuid(within.Id);
                query = query.Where(o => o.EnrollmentId == enrollment);
                break;
            case { Kind: NodeKinds.CollectionInstance }:
                var formId = CollectionInstanceId.FromGuid(within.Id);
                var form = await db.CollectionInstances.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == formId, cancellationToken)
                    .ConfigureAwait(false);
                if (form is null || form.Members.Count == 0)
                {
                    return [];
                }

                var memberIds = form.Members.Select(m => m.ObservationId).ToList();
                query = query.Where(o => memberIds.Contains(o.Id));
                break;
            case { Kind: NodeKinds.Observation }:
                var obsId = ObservationId.FromGuid(within.Id);
                query = query.Where(o => o.Id == obsId);
                break;
            default:
                return [];
        }

        var candidateObservations = await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var recordedCandidates = candidateObservations
            .Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .ToList();
        if (recordedCandidates.Count == 0)
        {
            return [];
        }

        var candidateObsGuids = recordedCandidates.Select(o => o.Id.Value).ToList();
        var verifiedTargets = await db.Assertions.AsNoTracking()
            .Where(v => v.TenantId == tenant
                && v.Kind == AssertionKind.Verified
                && v.RetractsId == null
                && v.TargetKind == NodeKinds.Observation
                && candidateObsGuids.Contains(v.TargetId)
                && !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.Verified && r.RetractsId == v.Id))
            .Select(v => new { v.TargetId, v.TargetRevision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var verifiedAtRevision = verifiedTargets
            .Select(v => (v.TargetId, v.TargetRevision))
            .ToHashSet();

        var recorded = recordedCandidates
            .Where(o => !verifiedAtRevision.Contains((o.Id.Value, o.Revision)))
            .ToList();
        if (recorded.Count == 0)
        {
            return [];
        }

        var enrollmentIds = recorded.Select(o => o.EnrollmentId).Distinct().ToList();
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => enrollmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken)
            .ConfigureAwait(false);

        var obsIds = recorded.Select(o => o.Id).ToList();
        var formsWithMembers = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.Members.Any(m => obsIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var formByObs = new Dictionary<ObservationId, CollectionInstanceId>();
        foreach (var inst in formsWithMembers)
        {
            foreach (var member in inst.Members)
            {
                formByObs[member.ObservationId] = inst.Id;
            }
        }

        var studyIds = recorded.Select(o => o.StudyId).Distinct().ToList();
        var activeHardLocks = await db.Assertions.AsNoTracking()
            .Where(a => a.TenantId == tenant
                && a.Kind == AssertionKind.Locked
                && a.RetractsId == null
                && studyIds.Contains(a.StudyId)
                && !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.Locked && r.RetractsId == a.Id))
            .Select(a => new { a.TargetKind, a.TargetId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var hardLockTargets = activeHardLocks.Select(l => (l.TargetKind, l.TargetId)).ToHashSet();

        var unlocked = recorded
            .Where(o =>
            {
                if (hardLockTargets.Contains((NodeKinds.Study, o.StudyId.Value))
                    || hardLockTargets.Contains((NodeKinds.Enrollment, o.EnrollmentId.Value))
                    || hardLockTargets.Contains((NodeKinds.Observation, o.Id.Value)))
                {
                    return false;
                }

                if (enrollments.TryGetValue(o.EnrollmentId, out var enr)
                    && hardLockTargets.Contains((NodeKinds.StudySite, enr.StudySiteId.Value)))
                {
                    return false;
                }

                if (formByObs.TryGetValue(o.Id, out var fid)
                    && hardLockTargets.Contains((NodeKinds.CollectionInstance, fid.Value)))
                {
                    return false;
                }

                return true;
            })
            .ToList();
        if (unlocked.Count == 0)
        {
            return [];
        }

        var conceptIds = unlocked.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => conceptIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.PreferredLabel, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. unlocked
                .Select(o =>
                {
                    var since = o.Revisions.FirstOrDefault(r => r.Number == o.Revision)?.RecordedAt
                        ?? new DateTimeOffset(DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc));
                    var label = concepts.GetValueOrDefault(o.ConceptId, o.ConceptId.Value.ToString());
                    var title = enrollments.TryGetValue(o.EnrollmentId, out var enr)
                        ? string.Create(CultureInfo.InvariantCulture, $"Verify {label} for {enr.SubjectCode.Value}")
                        : string.Create(CultureInfo.InvariantCulture, $"Verify {label}");
                    return new WorkItemCandidate(NodeRef.For(o.Id, o.VersionIndex), title, since);
                })
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

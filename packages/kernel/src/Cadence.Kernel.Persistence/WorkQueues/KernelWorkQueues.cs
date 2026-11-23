using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>The current work-queue definitions (ADR-2486), read without tracking under the tenant's Row-Level Security.</summary>
internal sealed class KernelWorkQueueDefinitions(KernelTenantDbContextFactory contexts) : IWorkQueueDefinitions
{
    public async Task<IReadOnlyList<WorkQueueDefinitionSnapshot>> CurrentAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var rows = await db.WorkQueueDefinitions.AsNoTracking()
            .Where(d => d.TenantId == tenant)
            .Where(d => !db.WorkQueueDefinitions.Any(later => later.TenantId == d.TenantId && later.Code == d.Code && later.Version > d.Version))
            .OrderBy(d => d.Code)
            .Select(d => new WorkQueueDefinitionSnapshot(d.Id, d.Code, d.Version, d.Title, d.Capability, d.VerbClass, d.Source, d.VersionIndex))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows;
    }
}

/// <summary>
/// The <see cref="KernelWorkItemSources.ProtocolVersionsInReview"/> source: protocol versions in review, due since
/// they were submitted, which is when their current row version was recorded (a version in review does not change
/// until it is approved, returned or withdrawn). <c>within</c> may name the tenant, a sponsor organization, a study or
/// one protocol version; any other object has no protocol versions under it.
/// </summary>
internal sealed class ProtocolVersionsInReviewSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.ProtocolVersionsInReview;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var versions = db.ProtocolVersions.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Status == ProtocolVersionStatus.InReview);
        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var sponsor = OrganizationId.FromGuid(within.Id);
                versions = versions.Where(p => db.Studies.Any(s => s.Id == p.StudyId && s.SponsorId == sponsor));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                versions = versions.Where(p => p.StudyId == study);
                break;
            case { Kind: NodeKinds.ProtocolVersion }:
                var version = ProtocolVersionId.FromGuid(within.Id);
                versions = versions.Where(p => p.Id == version);
                break;
            default:
                return [];
        }

        var inReview = await versions
            .Join(db.Studies, p => p.StudyId, s => s.Id, (p, s) => new { p.Id, p.Number, p.VersionIndex, p.CreatedAt, s.StudyCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inReview.Count == 0)
        {
            return [];
        }

        // The lineage node of each version's current row records when that row was written: the submission. A row
        // written before lineage was recorded falls back to when the version was created.
        var ids = inReview.Select(v => v.Id.Value).ToList();
        var recorded = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.ProtocolVersion && ids.Contains(n.NodeId))
            .Select(n => new { n.NodeId, n.Version, n.RecordedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var submittedAt = recorded.ToDictionary(n => (n.NodeId, n.Version), n => n.RecordedAt);

        return
        [
            .. inReview
                .Select(v => new WorkItemCandidate(
                    NodeRef.For(v.Id, v.VersionIndex),
                    string.Create(CultureInfo.InvariantCulture, $"Protocol version {v.Number} of {v.StudyCode}"),
                    submittedAt.TryGetValue((v.Id.Value, v.VersionIndex), out var at)
                        ? at
                        : new DateTimeOffset(DateTime.SpecifyKind(v.CreatedAt, DateTimeKind.Utc))))
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

/// <summary>The <see cref="KernelWorkItemSources.QueriesOpen"/> source: un-retracted queries awaiting an answer.</summary>
internal sealed class QueriesOpenSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.QueriesOpen;

    public Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken) =>
        QueryCandidatesReader.ReadAsync(contexts, tenant, within, answered: false, take, cancellationToken);
}

/// <summary>The <see cref="KernelWorkItemSources.QueriesAnswered"/> source: answered queries awaiting closure.</summary>
internal sealed class QueriesAnsweredSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.QueriesAnswered;

    public Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken) =>
        QueryCandidatesReader.ReadAsync(contexts, tenant, within, answered: true, take, cancellationToken);
}

internal static class QueryCandidatesReader
{
    public static async Task<IReadOnlyList<WorkItemCandidate>> ReadAsync(
        KernelTenantDbContextFactory contexts,
        TenantId tenant,
        NodeRef? within,
        bool answered,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var queries = db.Assertions.AsNoTracking()
            .Where(q => q.TenantId == tenant && q.Kind == AssertionKind.Queried && q.RetractsId == null)
            .Where(q => !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.Queried && r.RetractsId == q.Id));

        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                queries = queries.Where(q => db.Studies.Any(s => s.Id == q.StudyId && s.SponsorId == org));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                queries = queries.Where(q => q.StudyId == study);
                break;
            case { Kind: NodeKinds.StudySite }:
                var site = StudySiteId.FromGuid(within.Id);
                queries = queries.Where(q => q.EnrollmentId != null && db.Enrollments.Any(e => e.Id == q.EnrollmentId && e.StudySiteId == site));
                break;
            case { Kind: NodeKinds.Enrollment }:
                var enrollment = EnrollmentId.FromGuid(within.Id);
                queries = queries.Where(q => q.EnrollmentId == enrollment);
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

                var memberGuids = form.Members.Select(m => m.ObservationId.Value).ToList();
                queries = queries.Where(q => q.TargetKind == NodeKinds.Observation && memberGuids.Contains(q.TargetId));
                break;
            case { Kind: NodeKinds.Observation }:
                queries = queries.Where(q => q.TargetKind == NodeKinds.Observation && q.TargetId == within.Id);
                break;
            case { Kind: NodeKinds.Assertion }:
                var assertionId = AssertionId.FromGuid(within.Id);
                queries = queries.Where(q => q.Id == assertionId);
                break;
            default:
                return [];
        }

        var activeQueries = await queries
            .Select(q => new { q.Id, q.VersionIndex, q.EnrollmentId, q.Text, q.AssertedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (activeQueries.Count == 0)
        {
            return [];
        }

        var queryGuids = activeQueries.Select(q => q.Id.Value).ToList();
        var activeAnswers = await db.Assertions.AsNoTracking()
            .Where(a => a.TenantId == tenant
                && a.Kind == AssertionKind.QueryAnswered
                && a.RetractsId == null
                && a.TargetKind == NodeKinds.Assertion
                && queryGuids.Contains(a.TargetId)
                && !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.QueryAnswered && r.RetractsId == a.Id))
            .Select(a => new { a.TargetId, a.AssertedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latestAnswerByQuery = activeAnswers
            .GroupBy(a => a.TargetId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.AssertedAt));

        var matching = activeQueries
            .Where(q => latestAnswerByQuery.ContainsKey(q.Id.Value) == answered)
            .ToList();
        if (matching.Count == 0)
        {
            return [];
        }

        var enrollmentIds = matching.Where(q => q.EnrollmentId is not null).Select(q => q.EnrollmentId!.Value).Distinct().ToList();
        var enrollmentRows = enrollmentIds.Count == 0
            ? []
            : await db.Enrollments.AsNoTracking()
                .Where(e => enrollmentIds.Contains(e.Id))
                .Select(e => new { e.Id, e.SubjectCode })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var subjectsByEnrollment = enrollmentRows.ToDictionary(e => e.Id, e => e.SubjectCode.Value);

        return
        [
            .. matching
                .Select(q =>
                {
                    var since = answered ? latestAnswerByQuery[q.Id.Value] : q.AssertedAt;
                    var prefix = q.EnrollmentId is { } eid && subjectsByEnrollment.TryGetValue(eid, out var code)
                        ? string.Create(CultureInfo.InvariantCulture, $"Query on {code}: ")
                        : "Query: ";
                    return new WorkItemCandidate(
                        NodeRef.For(q.Id, q.VersionIndex),
                        prefix + WorkItemFormatting.Snippet(q.Text, 80),
                        since);
                })
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

/// <summary>The <see cref="KernelWorkItemSources.ValidationFindingsOpen"/> source: unresolved (<c>Open</c> or <c>Investigating</c>) validation findings.</summary>
internal sealed class ValidationFindingsOpenSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.ValidationFindingsOpen;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);

        List<StudyId> studyIds;
        HashSet<Guid>? allowedFormGuids = null;
        HashSet<Guid>? allowedObsGuids = null;
        ValidationFindingId? onlyFindingId = null;

        switch (within)
        {
            case null:
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                studyIds = await db.Set<ValidationRun>().AsNoTracking()
                    .Select(r => r.StudyId)
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                studyIds = await db.Set<ValidationRun>().AsNoTracking()
                    .Where(r => db.Studies.Any(s => s.Id == r.StudyId && s.SponsorId == org))
                    .Select(r => r.StudyId)
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
            case { Kind: NodeKinds.Study }:
                studyIds = [StudyId.FromGuid(within.Id)];
                break;
            case { Kind: NodeKinds.StudySite }:
                {
                    var siteId = StudySiteId.FromGuid(within.Id);
                    var studyId = await db.StudySites.AsNoTracking()
                        .Where(ss => ss.Id == siteId)
                        .Select(ss => (StudyId?)ss.StudyId)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (studyId is null)
                    {
                        return [];
                    }

                    studyIds = [studyId.Value];
                    var forms = await db.CollectionInstances.AsNoTracking()
                        .Where(c => c.StudySiteId == siteId)
                        .Select(c => c.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    allowedFormGuids = forms.Select(id => id.Value).ToHashSet();
                    var obsIds = await db.Observations.AsNoTracking()
                        .Where(o => db.Enrollments.Any(e => e.Id == o.EnrollmentId && e.StudySiteId == siteId))
                        .Select(o => o.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    allowedObsGuids = obsIds.Select(id => id.Value).ToHashSet();
                    break;
                }
            case { Kind: NodeKinds.Enrollment }:
                {
                    var enrollmentId = EnrollmentId.FromGuid(within.Id);
                    var studyId = await db.Enrollments.AsNoTracking()
                        .Where(e => e.Id == enrollmentId)
                        .Select(e => (StudyId?)e.StudyId)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (studyId is null)
                    {
                        return [];
                    }

                    studyIds = [studyId.Value];
                    var forms = await db.CollectionInstances.AsNoTracking()
                        .Where(c => c.EnrollmentId == enrollmentId)
                        .Select(c => c.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    allowedFormGuids = forms.Select(id => id.Value).ToHashSet();
                    var obsIds = await db.Observations.AsNoTracking()
                        .Where(o => o.EnrollmentId == enrollmentId)
                        .Select(o => o.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    allowedObsGuids = obsIds.Select(id => id.Value).ToHashSet();
                    break;
                }
            case { Kind: NodeKinds.CollectionInstance }:
                {
                    var formId = CollectionInstanceId.FromGuid(within.Id);
                    var form = await db.CollectionInstances.AsNoTracking()
                        .SingleOrDefaultAsync(c => c.Id == formId, cancellationToken)
                        .ConfigureAwait(false);
                    if (form is null)
                    {
                        return [];
                    }

                    studyIds = [form.StudyId];
                    allowedFormGuids = [form.Id.Value];
                    allowedObsGuids = form.Members.Select(m => m.ObservationId.Value).ToHashSet();
                    break;
                }
            case { Kind: NodeKinds.Observation }:
                {
                    var obsId = ObservationId.FromGuid(within.Id);
                    var studyId = await db.Observations.AsNoTracking()
                        .Where(o => o.Id == obsId)
                        .Select(o => (StudyId?)o.StudyId)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (studyId is null)
                    {
                        return [];
                    }

                    studyIds = [studyId.Value];
                    allowedFormGuids = [];
                    allowedObsGuids = [obsId.Value];
                    break;
                }
            case { Kind: NodeKinds.ValidationFinding }:
                {
                    var fid = ValidationFindingId.FromGuid(within.Id);
                    var studyId = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                        .Where(d => d.FindingId == fid)
                        .Select(d => (StudyId?)d.StudyId)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (studyId is null)
                    {
                        return [];
                    }

                    studyIds = [studyId.Value];
                    onlyFindingId = fid;
                    break;
                }
            default:
                return [];
        }

        if (studyIds.Count == 0)
        {
            return [];
        }

        var candidates = new List<WorkItemCandidate>();
        foreach (var studyId in studyIds)
        {
            var ledger = await KernelValidationStore.LoadStudyAsync(db, studyId, stage: null, cancellationToken).ConfigureAwait(false);
            var active = ledger.Derive()
                .Where(f => f.Status is FindingStatus.Open or FindingStatus.Investigating)
                .Where(f => onlyFindingId is null || f.Finding == onlyFindingId.Value)
                .ToList();
            if (active.Count == 0)
            {
                continue;
            }

            var runDates = ledger.Runs.ToDictionary(r => r.Id, r => r.RecordedAt);
            var findingIds = active.Select(a => a.Finding).ToList();
            var lastRunIds = active.Select(a => a.LastDetectedRun).Distinct().ToList();
            var detections = (await db.Set<ValidationDetectionRecord>().AsNoTracking()
                    .Where(d => findingIds.Contains(d.FindingId) && lastRunIds.Contains(d.RunId))
                    .Select(d => new { d.RunId, d.FindingId, d.RuleId, d.Message, d.Stage, d.TargetKind, d.TargetKey })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(d => (d.RunId, d.FindingId));
            var decisionVersions = await db.Set<ValidationFinding>().AsNoTracking()
                .Where(f => findingIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.VersionIndex, cancellationToken)
                .ConfigureAwait(false);

            foreach (var state in active)
            {
                if (!detections.TryGetValue((state.LastDetectedRun, state.Finding), out var det))
                {
                    continue;
                }

                if (allowedFormGuids is not null || allowedObsGuids is not null)
                {
                    var matchesForm = det.Stage == ValidationStage.Collection
                        && det.TargetKind == FindingTargetKind.DatasetRecord
                        && Guid.TryParse(det.TargetKey, out var fg)
                        && allowedFormGuids is not null
                        && allowedFormGuids.Contains(fg);
                    var matchesObs = det.TargetKind == FindingTargetKind.Observation
                        && NodeRef.TryParse(det.TargetKey, out var on)
                        && on.Kind == NodeKinds.Observation
                        && allowedObsGuids is not null
                        && allowedObsGuids.Contains(on.Id);
                    if (!matchesForm && !matchesObs)
                    {
                        continue;
                    }
                }

                var subject = decisionVersions.TryGetValue(state.Finding, out var ver)
                    ? NodeRef.For(state.Finding, ver)
                    : NodeRef.For(state.Finding);
                var since = runDates.GetValueOrDefault(state.FirstDetectedRun, DateTimeOffset.UnixEpoch);
                var title = string.Create(CultureInfo.InvariantCulture, $"[{state.Severity}] {det.RuleId}: {WorkItemFormatting.Snippet(det.Message, 80)}");
                candidates.Add(new WorkItemCandidate(subject, title, since));
            }
        }

        return
        [
            .. candidates
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

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

/// <summary>The <see cref="KernelWorkItemSources.ProposalsPending"/> source: open, unexpired proposals awaiting human review.</summary>
internal sealed class ProposalsPendingSource(KernelTenantDbContextFactory contexts, IClock clock) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.ProposalsPending;

    public async Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        var now = clock.UtcNow;
        await using var db = contexts.Create(tenant);
        var query = db.Proposals.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.Status == ProposalStatus.Open && p.ExpiresAt > now);

        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                query = query.Where(p => db.Studies.Any(s => s.Id == p.StudyId && s.SponsorId == org));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                query = query.Where(p => p.StudyId == study);
                break;
            case { Kind: NodeKinds.Proposal }:
                var proposalId = ProposalId.FromGuid(within.Id);
                query = query.Where(p => p.Id == proposalId);
                break;
            default:
                return [];
        }

        var openProposals = await query
            .Select(p => new { p.Id, p.VersionIndex, p.Intent, p.ProposedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (openProposals.Count == 0)
        {
            return [];
        }

        return
        [
            .. openProposals
                .Select(p => new WorkItemCandidate(
                    NodeRef.For(p.Id, p.VersionIndex),
                    "Proposal: " + WorkItemFormatting.Snippet(p.Intent, 80),
                    p.ProposedAt))
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}

/// <summary>
/// Masks work items whose subject or underlying observation records blinded treatment allocation unless the reader
/// holds <c>enrollment.unblind</c> on the covered enrollment or study (K-AUTH-4, ADR-2471).
/// </summary>
internal sealed class KernelWorkItemBlindingMask(KernelTenantDbContextFactory contexts) : IWorkItemBlindingMask
{
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IAuthorizationProbe probe,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        await using var db = contexts.Create(tenant);
        var obsByNode = new Dictionary<NodeRef, List<ObservationId>>();

        var directObsNodes = nodes.Where(n => n.Kind == NodeKinds.Observation).ToList();
        foreach (var node in directObsNodes)
        {
            obsByNode[node] = [ObservationId.FromGuid(node.Id)];
        }

        var formNodes = nodes.Where(n => n.Kind == NodeKinds.CollectionInstance).ToList();
        if (formNodes.Count > 0)
        {
            var formIds = formNodes.Select(n => CollectionInstanceId.FromGuid(n.Id)).Distinct().ToList();
            var formRows = await db.CollectionInstances.AsNoTracking()
                .Where(c => formIds.Contains(c.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var forms = formRows.ToDictionary(c => c.Id.Value, c => c.Members.Select(m => m.ObservationId).ToList());
            foreach (var node in formNodes)
            {
                if (forms.TryGetValue(node.Id, out var memberObs) && memberObs.Count > 0)
                {
                    obsByNode[node] = memberObs;
                }
            }
        }

        var assertionNodes = nodes.Where(n => n.Kind == NodeKinds.Assertion).ToList();
        if (assertionNodes.Count > 0)
        {
            var assertionIds = assertionNodes.Select(n => AssertionId.FromGuid(n.Id)).Distinct().ToList();
            var assertionRows = await db.Assertions.AsNoTracking()
                .Where(a => assertionIds.Contains(a.Id) && a.TargetKind == NodeKinds.Observation)
                .Select(a => new { a.Id, a.TargetId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var assertions = assertionRows.ToDictionary(a => a.Id.Value, a => ObservationId.FromGuid(a.TargetId));
            foreach (var node in assertionNodes)
            {
                if (assertions.TryGetValue(node.Id, out var obsId))
                {
                    obsByNode[node] = [obsId];
                }
            }
        }

        var findingNodes = nodes.Where(n => n.Kind == NodeKinds.ValidationFinding).ToList();
        if (findingNodes.Count > 0)
        {
            var findingIds = findingNodes.Select(n => ValidationFindingId.FromGuid(n.Id)).Distinct().ToList();
            var dets = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => findingIds.Contains(d.FindingId))
                .Select(d => new { d.FindingId, d.Stage, d.TargetKind, d.TargetKey })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var targetForms = new Dictionary<Guid, CollectionInstanceId>();
            foreach (var det in dets.GroupBy(d => d.FindingId).Select(g => g.First()))
            {
                if (det.TargetKind == FindingTargetKind.Observation
                    && NodeRef.TryParse(det.TargetKey, out var obsNode)
                    && obsNode.Kind == NodeKinds.Observation)
                {
                    foreach (var node in findingNodes.Where(n => n.Id == det.FindingId.Value))
                    {
                        obsByNode[node] = [ObservationId.FromGuid(obsNode.Id)];
                    }
                }
                else if (det.Stage == ValidationStage.Collection
                    && det.TargetKind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(det.TargetKey, out var fg))
                {
                    targetForms[det.FindingId.Value] = CollectionInstanceId.FromGuid(fg);
                }
            }

            if (targetForms.Count > 0)
            {
                var fIds = targetForms.Values.Distinct().ToList();
                var formRows = await db.CollectionInstances.AsNoTracking()
                    .Where(c => fIds.Contains(c.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var forms = formRows.ToDictionary(c => c.Id, c => c.Members.Select(m => m.ObservationId).ToList());
                foreach (var (findingGuid, fId) in targetForms)
                {
                    if (forms.TryGetValue(fId, out var memberObs) && memberObs.Count > 0)
                    {
                        foreach (var node in findingNodes.Where(n => n.Id == findingGuid))
                        {
                            obsByNode[node] = memberObs;
                        }
                    }
                }
            }
        }

        var allObsIds = obsByNode.Values.SelectMany(x => x).Distinct().ToList();
        if (allObsIds.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var observations = await db.Observations.AsNoTracking()
            .Where(o => allObsIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (observations.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var obsLookup = observations.ToDictionary(o => o.Id);
        var blindedIds = new HashSet<ObservationId>();
        foreach (var byStudy in observations.GroupBy(o => o.StudyId))
        {
            var studyBlinded = await KernelLockRecords.ResolveBlindedObservationsAsync(db, byStudy.Key, byStudy, cancellationToken)
                .ConfigureAwait(false);
            blindedIds.UnionWith(studyBlinded);
        }

        if (blindedIds.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var unblindCache = new Dictionary<NodeRef, bool>();
        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var (node, mappedObsIds) in obsByNode)
        {
            var blindedObs = mappedObsIds.Where(blindedIds.Contains).Select(id => obsLookup[id]).ToList();
            if (blindedObs.Count == 0)
            {
                continue;
            }

            var blocked = false;
            foreach (var obs in blindedObs)
            {
                var unblindTarget = NodeRef.For(obs.EnrollmentId);
                if (!unblindCache.TryGetValue(unblindTarget, out var mayUnblind))
                {
                    var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindTarget, cancellationToken)
                        .ConfigureAwait(false);
                    mayUnblind = decision.Permitted;
                    unblindCache[unblindTarget] = mayUnblind;
                }

                if (!mayUnblind)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                result[node] = MaskReason.Blinded;
            }
        }

        return result;
    }
}

/// <summary>Resolves study, enrollment, form, observation, finding, and protocol navigation context for unmasked work items.</summary>
internal sealed class KernelWorkItemTargetResolver(KernelTenantDbContextFactory contexts) : IWorkItemTargetResolver
{
    public async Task<IReadOnlyDictionary<NodeRef, WorkItemTargetContext>> ResolveAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> subjects,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return new Dictionary<NodeRef, WorkItemTargetContext>();
        }

        await using var db = contexts.Create(tenant);
        var result = new Dictionary<NodeRef, WorkItemTargetContext>();

        var protocolNodes = subjects.Where(s => s.Kind == NodeKinds.ProtocolVersion).ToList();
        if (protocolNodes.Count > 0)
        {
            var pvIds = protocolNodes.Select(s => ProtocolVersionId.FromGuid(s.Id)).Distinct().ToList();
            var pvRows = await db.ProtocolVersions.AsNoTracking()
                .Where(p => pvIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var pvs = pvRows.ToDictionary(p => p.Id.Value, p => p.StudyId);
            foreach (var node in protocolNodes)
            {
                if (pvs.TryGetValue(node.Id, out var studyId))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(studyId).ToString(),
                        StudyId: studyId.Value,
                        ProtocolVersionRef: node.ToString());
                }
            }
        }

        var proposalNodes = subjects.Where(s => s.Kind == NodeKinds.Proposal).ToList();
        if (proposalNodes.Count > 0)
        {
            var propIds = proposalNodes.Select(s => ProposalId.FromGuid(s.Id)).Distinct().ToList();
            var proposalRows = await db.Proposals.AsNoTracking()
                .Where(p => propIds.Contains(p.Id))
                .Select(p => new { p.Id, p.StudyId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var proposals = proposalRows.ToDictionary(p => p.Id.Value, p => p.StudyId);
            foreach (var node in proposalNodes)
            {
                if (proposals.TryGetValue(node.Id, out var studyId))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(studyId).ToString(),
                        StudyId: studyId.Value,
                        ProposalRef: node.ToString());
                }
            }
        }

        var formNodes = subjects.Where(s => s.Kind == NodeKinds.CollectionInstance).ToList();
        if (formNodes.Count > 0)
        {
            var formIds = formNodes.Select(s => CollectionInstanceId.FromGuid(s.Id)).Distinct().ToList();
            var formRows = await db.CollectionInstances.AsNoTracking()
                .Where(c => formIds.Contains(c.Id))
                .Select(c => new { c.Id, c.StudyId, c.EnrollmentId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var forms = formRows.ToDictionary(c => c.Id.Value, c => (c.StudyId, c.EnrollmentId));
            foreach (var node in formNodes)
            {
                if (forms.TryGetValue(node.Id, out var f))
                {
                    result[node] = new WorkItemTargetContext(
                        StudyRef: NodeRef.For(f.StudyId).ToString(),
                        StudyId: f.StudyId.Value,
                        EnrollmentId: f.EnrollmentId.Value,
                        FormId: node.Id);
                }
            }
        }

        var obsNodes = subjects.Where(s => s.Kind == NodeKinds.Observation).ToList();
        var assertionNodes = subjects.Where(s => s.Kind == NodeKinds.Assertion).ToList();
        var findingNodes = subjects.Where(s => s.Kind == NodeKinds.ValidationFinding).ToList();

        var assertionTargets = new Dictionary<Guid, (StudyId StudyId, EnrollmentId? EnrollmentId, ObservationId? ObservationId)>();
        if (assertionNodes.Count > 0)
        {
            var aIds = assertionNodes.Select(s => AssertionId.FromGuid(s.Id)).Distinct().ToList();
            var rows = await db.Assertions.AsNoTracking()
                .Where(a => aIds.Contains(a.Id))
                .Select(a => new { a.Id, a.StudyId, a.EnrollmentId, a.TargetKind, a.TargetId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                var obsId = row.TargetKind == NodeKinds.Observation ? ObservationId.FromGuid(row.TargetId) : (ObservationId?)null;
                assertionTargets[row.Id.Value] = (row.StudyId, row.EnrollmentId, obsId);
            }
        }

        var findingTargets = new Dictionary<Guid, (StudyId StudyId, CollectionInstanceId? FormId, ObservationId? ObservationId)>();
        if (findingNodes.Count > 0)
        {
            var fIds = findingNodes.Select(s => ValidationFindingId.FromGuid(s.Id)).Distinct().ToList();
            var dets = await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => fIds.Contains(d.FindingId))
                .Select(d => new { d.FindingId, d.StudyId, d.Stage, d.TargetKind, d.TargetKey })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var det in dets.GroupBy(d => d.FindingId).Select(g => g.First()))
            {
                CollectionInstanceId? fId = det.Stage == ValidationStage.Collection
                    && det.TargetKind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(det.TargetKey, out var fg)
                    ? CollectionInstanceId.FromGuid(fg)
                    : null;
                ObservationId? oId = det.TargetKind == FindingTargetKind.Observation
                    && NodeRef.TryParse(det.TargetKey, out var on)
                    && on.Kind == NodeKinds.Observation
                    ? ObservationId.FromGuid(on.Id)
                    : null;
                findingTargets[det.FindingId.Value] = (det.StudyId, fId, oId);
            }
        }

        var referencedObsIds = obsNodes.Select(s => ObservationId.FromGuid(s.Id))
            .Concat(assertionTargets.Values.Where(v => v.ObservationId is not null).Select(v => v.ObservationId!.Value))
            .Concat(findingTargets.Values.Where(v => v.ObservationId is not null).Select(v => v.ObservationId!.Value))
            .Distinct()
            .ToList();

        Dictionary<ObservationId, (StudyId StudyId, EnrollmentId EnrollmentId, long VersionIndex)> obsDetails;
        if (referencedObsIds.Count == 0)
        {
            obsDetails = new Dictionary<ObservationId, (StudyId StudyId, EnrollmentId EnrollmentId, long VersionIndex)>();
        }
        else
        {
            var obsRows = await db.Observations.AsNoTracking()
                .Where(o => referencedObsIds.Contains(o.Id))
                .Select(o => new { o.Id, o.StudyId, o.EnrollmentId, o.VersionIndex })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            obsDetails = obsRows.ToDictionary(o => o.Id, o => (o.StudyId, o.EnrollmentId, o.VersionIndex));
        }

        var referencedFormIds = findingTargets.Values
            .Where(v => v.FormId is not null)
            .Select(v => v.FormId!.Value)
            .Distinct()
            .ToList();

        var memberForms = (referencedObsIds.Count == 0 && referencedFormIds.Count == 0)
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Where(c => referencedFormIds.Contains(c.Id) || c.Members.Any(m => referencedObsIds.Contains(m.ObservationId)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var formByObs = new Dictionary<ObservationId, CollectionInstance>();
        var formById = new Dictionary<CollectionInstanceId, CollectionInstance>();
        foreach (var inst in memberForms)
        {
            formById[inst.Id] = inst;
            foreach (var member in inst.Members)
            {
                formByObs[member.ObservationId] = inst;
            }
        }

        foreach (var node in obsNodes)
        {
            var obsId = ObservationId.FromGuid(node.Id);
            if (obsDetails.TryGetValue(obsId, out var detail))
            {
                formByObs.TryGetValue(obsId, out var inst);
                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(detail.StudyId).ToString(),
                    StudyId: detail.StudyId.Value,
                    EnrollmentId: detail.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: node.ToString());
            }
        }

        foreach (var node in assertionNodes)
        {
            if (assertionTargets.TryGetValue(node.Id, out var target))
            {
                CollectionInstance? inst = null;
                string? obsRef = null;
                var enrollmentId = target.EnrollmentId;
                if (target.ObservationId is { } oId)
                {
                    formByObs.TryGetValue(oId, out inst);
                    if (obsDetails.TryGetValue(oId, out var od))
                    {
                        enrollmentId ??= od.EnrollmentId;
                        obsRef = NodeRef.For(oId, od.VersionIndex).ToString();
                    }
                    else
                    {
                        obsRef = NodeRef.For(oId).ToString();
                    }
                }

                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(target.StudyId).ToString(),
                    StudyId: target.StudyId.Value,
                    EnrollmentId: enrollmentId?.Value ?? inst?.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: obsRef);
            }
        }

        foreach (var node in findingNodes)
        {
            if (findingTargets.TryGetValue(node.Id, out var target))
            {
                CollectionInstance? inst = null;
                Guid? enrollmentGuid = null;
                string? obsRef = null;
                if (target.FormId is { } fId && formById.TryGetValue(fId, out inst))
                {
                    enrollmentGuid = inst.EnrollmentId.Value;
                }
                else if (target.ObservationId is { } oId)
                {
                    formByObs.TryGetValue(oId, out inst);
                    if (obsDetails.TryGetValue(oId, out var od))
                    {
                        enrollmentGuid = od.EnrollmentId.Value;
                        obsRef = NodeRef.For(oId, od.VersionIndex).ToString();
                    }
                }

                result[node] = new WorkItemTargetContext(
                    StudyRef: NodeRef.For(target.StudyId).ToString(),
                    StudyId: target.StudyId.Value,
                    EnrollmentId: enrollmentGuid ?? inst?.EnrollmentId.Value,
                    FormId: inst?.Id.Value,
                    ObservationRef: obsRef,
                    FindingRef: node.Unversioned().ToString());
            }
        }

        return result;
    }
}

internal static class WorkItemFormatting
{
    public static string Snippet(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Review item";
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength - 1), "…");
    }
}

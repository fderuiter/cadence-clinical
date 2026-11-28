using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

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

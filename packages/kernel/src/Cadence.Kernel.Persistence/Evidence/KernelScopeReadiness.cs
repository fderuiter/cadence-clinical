using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Read-only projection of freeze/lock readiness for a scope within a study (spec §10.5).
/// </summary>
internal sealed class KernelScopeReadiness(KernelTenantDbContextFactory contexts) : IScopeReadiness
{
    public async Task<ScopeReadinessSummary?> EvaluateAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken)
    {
        if (!LockScopes.IsScope(scope))
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        List<CollectionInstance> forms;
        HashSet<Guid> coveredObservationGuids;
        HashSet<Guid> coveredFormGuids;
        string? singleObservationItemId = null;
        Guid? singleObservationParentFormGuid = null;
        var placeholderObservationCount = 0;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                {
                    if (StudyId.FromGuid(scope.Id) != study
                        || !await db.Studies.AsNoTracking().AnyAsync(s => s.Id == study, cancellationToken).ConfigureAwait(false))
                    {
                        return null;
                    }

                    forms = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .Where(c => c.StudyId == study)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var obsIds = await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study)
                        .Select(o => o.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    coveredObservationGuids = obsIds.Select(o => o.Value).ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.StudySite:
                {
                    var siteId = StudySiteId.FromGuid(scope.Id);
                    var site = await db.StudySites.AsNoTracking()
                        .SingleOrDefaultAsync(s => s.Id == siteId && s.StudyId == study, cancellationToken)
                        .ConfigureAwait(false);
                    if (site is null)
                    {
                        return null;
                    }

                    var enrollmentIds = await db.Enrollments.AsNoTracking()
                        .Where(e => e.StudySiteId == siteId)
                        .Select(e => e.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    forms = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .Where(c => c.StudySiteId == siteId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var obsIds = enrollmentIds.Count == 0
                        ? []
                        : await db.Observations.AsNoTracking()
                            .Where(o => enrollmentIds.Contains(o.EnrollmentId))
                            .Select(o => o.Id)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                    coveredObservationGuids = obsIds.Select(o => o.Value).ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.Enrollment:
                {
                    var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                    var enrollment = await db.Enrollments.AsNoTracking()
                        .SingleOrDefaultAsync(e => e.Id == enrollmentId && e.StudyId == study, cancellationToken)
                        .ConfigureAwait(false);
                    if (enrollment is null)
                    {
                        return null;
                    }

                    forms = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .Where(c => c.EnrollmentId == enrollmentId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var obsIds = await db.Observations.AsNoTracking()
                        .Where(o => o.EnrollmentId == enrollmentId)
                        .Select(o => o.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    coveredObservationGuids = obsIds.Select(o => o.Value).ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.CollectionInstance:
                {
                    var formId = CollectionInstanceId.FromGuid(scope.Id);
                    var instance = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .SingleOrDefaultAsync(c => c.Id == formId && c.StudyId == study, cancellationToken)
                        .ConfigureAwait(false);
                    if (instance is null)
                    {
                        return null;
                    }

                    forms = [instance];
                    coveredObservationGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
                    coveredFormGuids = [instance.Id.Value];
                    break;
                }
            default:
                {
                    var obsId = ObservationId.FromGuid(scope.Id);
                    var obs = await db.Observations.AsNoTracking()
                        .SingleOrDefaultAsync(o => o.Id == obsId && o.StudyId == study, cancellationToken)
                        .ConfigureAwait(false);
                    if (obs is null)
                    {
                        return null;
                    }

                    forms = [];
                    coveredFormGuids = [];
                    coveredObservationGuids = [obs.Id.Value];
                    if (obs.Value is MissingValue { Reason: NullFlavor.NotAsked })
                    {
                        placeholderObservationCount = 1;
                    }

                    var parentForm = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .FirstOrDefaultAsync(c => c.Members.Any(m => m.ObservationId == obsId), cancellationToken)
                        .ConfigureAwait(false);
                    if (parentForm is not null)
                    {
                        singleObservationParentFormGuid = parentForm.Id.Value;
                        singleObservationItemId = parentForm.Members.FirstOrDefault(m => m.ObservationId == obsId)?.ItemId;
                    }

                    break;
                }
        }

        var incompleteFormCount = placeholderObservationCount + await CountFormsMissingRequiredAsync(db, forms, cancellationToken).ConfigureAwait(false);
        var (openQueryCount, answeredQueryCount) = await CountActiveQueriesAsync(db, study, scope.Kind, coveredObservationGuids, cancellationToken)
            .ConfigureAwait(false);

        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, study, cancellationToken)
            .ConfigureAwait(false);
        var errorFindingCount = scope.Kind switch
        {
            NodeKinds.Study => activeErrorTargets.Count,
            NodeKinds.Observation => activeErrorTargets.Count(entry =>
                (entry.Target.Kind == FindingTargetKind.Observation && entry.Target.Node is { } obsNode && coveredObservationGuids.Contains(obsNode.Id))
                || (singleObservationParentFormGuid is { } pGuid
                    && entry.Target.Kind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(entry.Target.Key, out var rGuid)
                    && rGuid == pGuid
                    && (string.IsNullOrWhiteSpace(entry.Target.Variable)
                        || string.Equals(entry.Target.Variable, singleObservationItemId, StringComparison.Ordinal)))),
            _ => activeErrorTargets.Count(entry =>
                (entry.Target.Kind == FindingTargetKind.DatasetRecord && Guid.TryParse(entry.Target.Key, out var rGuid) && coveredFormGuids.Contains(rGuid))
                || (entry.Target.Kind == FindingTargetKind.Observation && entry.Target.Node is { } obsNode && coveredObservationGuids.Contains(obsNode.Id))),
        };

        return CollectionReadiness.BuildScopeSummary(
            scope,
            incompleteFormCount,
            openQueryCount,
            answeredQueryCount,
            errorFindingCount);
    }

    private static async Task<int> CountFormsMissingRequiredAsync(
        KernelDbContext db,
        IReadOnlyList<CollectionInstance> forms,
        CancellationToken cancellationToken)
    {
        if (forms.Count == 0)
        {
            return 0;
        }

        var versionIds = forms.Select(f => f.ProtocolVersionId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var specsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design?.CollectionSpecs ?? []);

        var memberIds = forms.SelectMany(f => f.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = memberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => memberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);

        var incomplete = 0;
        foreach (var form in forms)
        {
            var specs = specsByVersion.GetValueOrDefault(form.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, form.CollectionSpecId, StringComparison.Ordinal));
            if (spec is null || !spec.Items.Any(i => i.Required))
            {
                continue;
            }

            var recordedItems = form.Members
                .Where(m => observations.TryGetValue(m.ObservationId, out var obs)
                    && obs.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                .Select(m => m.ItemId)
                .ToHashSet(StringComparer.Ordinal);
            if (spec.Items.Any(i => i.Required && !recordedItems.Contains(i.Id)))
            {
                incomplete++;
            }
        }

        return incomplete;
    }

    private static async Task<(int OpenCount, int AnsweredCount)> CountActiveQueriesAsync(
        KernelDbContext db,
        StudyId study,
        string scopeKind,
        IReadOnlySet<Guid> coveredObservationGuids,
        CancellationToken cancellationToken)
    {
        if (scopeKind != NodeKinds.Study && coveredObservationGuids.Count == 0)
        {
            return (0, 0);
        }

        var studyAssertions = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study && (a.Kind == AssertionKind.Queried || a.Kind == AssertionKind.QueryAnswered))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var retractedIds = studyAssertions
            .Where(a => a.RetractsId is not null)
            .Select(a => a.RetractsId!.Value)
            .ToHashSet();

        var activeQueryGuids = studyAssertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.RetractsId is null
                && !retractedIds.Contains(a.Id)
                && (scopeKind == NodeKinds.Study || (a.TargetKind == NodeKinds.Observation && coveredObservationGuids.Contains(a.TargetId))))
            .Select(a => a.Id.Value)
            .ToHashSet();
        if (activeQueryGuids.Count == 0)
        {
            return (0, 0);
        }

        var answeredQueryGuids = studyAssertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractsId is null
                && !retractedIds.Contains(a.Id)
                && a.TargetKind == NodeKinds.Assertion
                && activeQueryGuids.Contains(a.TargetId))
            .Select(a => a.TargetId)
            .ToHashSet();
        var answeredCount = answeredQueryGuids.Count;
        return (activeQueryGuids.Count - answeredCount, answeredCount);
    }
}

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class CollectionQueryTargetReader
{
    internal static async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> distinctIds,
        IReadOnlySet<EnrollmentId> unblindedEnrollments,
        IBlindedTreatmentClassifier blinding,
        CancellationToken cancellationToken)
    {
        var forms = await db.CollectionInstances.AsNoTracking()
            .Where(c => distinctIds.Contains(c.Id))
            .Select(c => new QueryTargetFormRow(
                c.Id,
                c.EnrollmentId,
                c.StudyId,
                c.StudySiteId,
                c.ProtocolVersionId,
                c.CollectionSpecId,
                c.PlannedActivityId,
                c.Members.Select(m => new MemberObservationRow(m.ItemId, m.ObservationId)).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (forms.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>();
        }

        var allMemberIds = forms.SelectMany(f => f.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var blindedForms = forms.Where(f => !unblindedEnrollments.Contains(f.EnrollmentId)).ToList();

        if (blindedForms.Count == 0)
        {
            var revisionsByObs = allMemberIds.Count == 0
                ? new Dictionary<ObservationId, int>()
                : await db.Observations.AsNoTracking()
                    .Where(o => allMemberIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.Revision })
                    .ToDictionaryAsync(o => o.Id, o => o.Revision, cancellationToken)
                    .ConfigureAwait(false);

            var unblindedTargets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
            foreach (var form in forms)
            {
                var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
                var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
                foreach (var member in form.Members)
                {
                    if (!revisionsByObs.TryGetValue(member.ObservationId, out var revision))
                    {
                        continue;
                    }

                    itemRevisions[member.ItemId] = revision;
                    observationRevisions[member.ObservationId] = revision;
                }

                unblindedTargets[form.Id] = new CollectionInstanceQueryTarget(
                    form.Id,
                    form.EnrollmentId,
                    form.StudyId,
                    form.StudySiteId,
                    form.ProtocolVersionId,
                    form.CollectionSpecId,
                    form.PlannedActivityId,
                    observationRevisions,
                    itemRevisions,
                    FrozenSet<string>.Empty,
                    FrozenSet<ObservationId>.Empty);
            }

            return unblindedTargets;
        }

        if (db.Database.IsRelational() && ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance))
        {
            var projections = allMemberIds.Count == 0
                ? new Dictionary<ObservationId, CollectionObservationProjection>()
                : (await db.Database
                        .SqlQueryRaw<ObservationProjectionRow>(
                            CollectionMemberBlindingClassifier.ObservationProjectionSql,
                            new NpgsqlParameter<Guid>("tenant", tenant.Value),
                            new NpgsqlParameter<Guid[]>("ids", [.. allMemberIds.Select(id => id.Value)]))
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(
                        r => ObservationId.FromGuid(r.Id),
                        r => new CollectionObservationProjection(r.Revision, r.IsBlinded));

            var sqlTargets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
            foreach (var form in forms)
            {
                var isUnblinded = unblindedEnrollments.Contains(form.EnrollmentId);
                HashSet<string>? blindedItemsBuilder = null;
                if (!isUnblinded && blinding.IsBlindedCollectionSpec(form.CollectionSpecId))
                {
                    blindedItemsBuilder = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var member in form.Members)
                    {
                        blindedItemsBuilder.Add(member.ItemId);
                    }
                }

                var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
                var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
                foreach (var member in form.Members)
                {
                    if (!projections.TryGetValue(member.ObservationId, out var proj))
                    {
                        continue;
                    }

                    itemRevisions[member.ItemId] = proj.Revision;
                    var memberBlinded = !isUnblinded
                        && (proj.IsBlinded
                            || (blindedItemsBuilder?.Contains(member.ItemId) ?? false)
                            || BlindedTreatmentClassifier.MatchesMarkers(member.ItemId));
                    if (memberBlinded)
                    {
                        blindedItemsBuilder ??= new HashSet<string>(StringComparer.Ordinal);
                        blindedItemsBuilder.Add(member.ItemId);
                    }
                    else
                    {
                        observationRevisions[member.ObservationId] = proj.Revision;
                    }
                }

                var blindedItems = blindedItemsBuilder is { Count: > 0 }
                    ? blindedItemsBuilder.ToFrozenSet(StringComparer.Ordinal)
                    : FrozenSet<string>.Empty;
                sqlTargets[form.Id] = new CollectionInstanceQueryTarget(
                    form.Id,
                    form.EnrollmentId,
                    form.StudyId,
                    form.StudySiteId,
                    form.ProtocolVersionId,
                    form.CollectionSpecId,
                    form.PlannedActivityId,
                    observationRevisions,
                    itemRevisions,
                    blindedItems,
                    FrozenSet<ObservationId>.Empty);
            }

            return sqlTargets;
        }

        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, QueryTargetObservationRow>()
            : await db.Observations.AsNoTracking()
                .Where(o => allMemberIds.Contains(o.Id))
                .Select(o => new QueryTargetObservationRow(o.Id, o.ConceptId, o.Value, o.Revision))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var blindedStudyIds = blindedForms.Select(f => f.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => blindedStudyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var specsByVersion = CollectionRecordHydrator.IndexSpecsByVersion(designsByVersion);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var blindedMemberIds = blindedForms.SelectMany(f => f.Members.Select(m => m.ObservationId)).ToHashSet();
        var conceptIds = rawVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Where(o => blindedMemberIds.Contains(o.Id)).Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        Dictionary<ConceptId, BlindedConceptMetadata> concepts;
        if (conceptIds.Count == 0)
        {
            concepts = [];
        }
        else
        {
            concepts = await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ExternalCode, c.PreferredLabel, c.Definition })
                .ToDictionaryAsync(
                    c => c.Id,
                    c => new BlindedConceptMetadata(c.ExternalCode, c.PreferredLabel, c.Definition),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var blindingByStudy = new Dictionary<StudyId, BatchFormBlindingLookup>(blindedStudyIds.Count);
        foreach (var studyId in blindedStudyIds)
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(studyId) ?? [];
            blindingByStudy[studyId] = new BatchFormBlindingLookup(blinding, concepts, studyDesigns);
        }

        var targets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
        foreach (var form in forms)
        {
            var isUnblinded = unblindedEnrollments.Contains(form.EnrollmentId);
            IReadOnlySet<string> blindedItems = FrozenSet<string>.Empty;
            IReadOnlySet<ObservationId> blindedObservations = FrozenSet<ObservationId>.Empty;
            if (!isUnblinded)
            {
                specsByVersion.TryGetValue(form.ProtocolVersionId, out var specsForVersion);
                CollectionSpec? spec = null;
                specsForVersion?.TryGetValue(form.CollectionSpecId, out spec);
                var items = spec?.Items ?? [];
                var title = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
                if (blindingByStudy.TryGetValue(form.StudyId, out var studyBlinding))
                {
                    (blindedItems, blindedObservations) = studyBlinding.ClassifyQueryTarget(form, title, items, observations);
                }
            }

            var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
            var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
            foreach (var member in form.Members)
            {
                if (!observations.TryGetValue(member.ObservationId, out var obs))
                {
                    continue;
                }

                itemRevisions[member.ItemId] = obs.Revision;
                if (isUnblinded || (!blindedItems.Contains(member.ItemId) && !blindedObservations.Contains(obs.Id)))
                {
                    observationRevisions[obs.Id] = obs.Revision;
                }
            }

            targets[form.Id] = new CollectionInstanceQueryTarget(
                form.Id,
                form.EnrollmentId,
                form.StudyId,
                form.StudySiteId,
                form.ProtocolVersionId,
                form.CollectionSpecId,
                form.PlannedActivityId,
                observationRevisions,
                itemRevisions,
                blindedItems,
                FrozenSet<ObservationId>.Empty);
        }

        return targets;
    }

    internal static async Task<CollectionQueryTarget?> FindQueryTargetAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        ObservationId? observation,
        string? itemId,
        bool checkLocks,
        CancellationToken cancellationToken)
    {
        var hasObservation = observation is { IsEmpty: false };
        var targetObservation = observation ?? default;
        var trimmedItemId = itemId?.Trim();
        var hasItem = !hasObservation && !string.IsNullOrWhiteSpace(trimmedItemId);

        var openedExplicitly = false;
        if (checkLocks && db.Database.IsRelational())
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            openedExplicitly = true;
        }

        try
        {
            QueryTargetRow? row;
            if (hasObservation || hasItem)
            {
                var matchedMembers =
                    from m in db.Set<CollectionInstanceMember>().AsNoTracking()
                    join o in db.Observations.AsNoTracking()
                        on new { m.TenantId, Id = m.ObservationId } equals new { o.TenantId, o.Id }
                    where hasObservation ? m.ObservationId == targetObservation : m.ItemId == trimmedItemId
                    select new
                    {
                        m.TenantId,
                        m.CollectionInstanceId,
                        m.ItemId,
                        ObservationId = (ObservationId?)o.Id,
                        Revision = (int?)o.Revision,
                        o.Value,
                    };

                row = await (
                        from c in db.CollectionInstances.AsNoTracking()
                        join e in db.Enrollments.AsNoTracking()
                            on new { c.TenantId, Id = c.EnrollmentId } equals new { e.TenantId, e.Id }
                        join mm in matchedMembers
                            on new { c.TenantId, CollectionInstanceId = c.Id } equals new { mm.TenantId, mm.CollectionInstanceId }
                            into mmGroup
                        from mm in mmGroup.DefaultIfEmpty()
                        where c.Id == instance
                        select new QueryTargetRow(
                            c.Id,
                            c.StudyId,
                            c.StudySiteId,
                            c.EnrollmentId,
                            c.CollectionSpecId,
                            c.PlannedActivityId,
                            c.VersionIndex,
                            e.VersionIndex,
                            mm != null ? mm.ItemId : null,
                            mm != null ? mm.ObservationId : null,
                            mm != null ? mm.Revision : null,
                            mm != null ? mm.Value : null))
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                row = await (
                        from c in db.CollectionInstances.AsNoTracking()
                        join e in db.Enrollments.AsNoTracking()
                            on new { c.TenantId, Id = c.EnrollmentId } equals new { e.TenantId, e.Id }
                        where c.Id == instance
                        select new QueryTargetRow(
                            c.Id,
                            c.StudyId,
                            c.StudySiteId,
                            c.EnrollmentId,
                            c.CollectionSpecId,
                            c.PlannedActivityId,
                            c.VersionIndex,
                            e.VersionIndex,
                            null,
                            null,
                            null,
                            null))
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (row is null)
            {
                return null;
            }

            var isLocked = false;
            if (checkLocks)
            {
                var studyGuid = row.StudyId.Value;
                var siteGuid = row.StudySiteId.Value;
                var enrollmentGuid = row.EnrollmentId.Value;
                var formGuid = row.Id.Value;
                var lockObsGuid = hasObservation ? targetObservation.Value : row.ObservationId?.Value;
                Guid[] targetGuids = lockObsGuid is { } og
                    ? [studyGuid, siteGuid, enrollmentGuid, formGuid, og]
                    : [studyGuid, siteGuid, enrollmentGuid, formGuid];

                isLocked = await db.Assertions.AsNoTracking()
                    .AnyAsync(
                        a => a.Kind == AssertionKind.Locked
                            && targetGuids.Contains(a.TargetId)
                            && ((a.TargetKind == NodeKinds.Study && a.TargetId == studyGuid)
                                || (a.TargetKind == NodeKinds.StudySite && a.TargetId == siteGuid)
                                || (a.TargetKind == NodeKinds.Enrollment && a.TargetId == enrollmentGuid)
                                || (a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == formGuid)
                                || (lockObsGuid != null && a.TargetKind == NodeKinds.Observation && a.TargetId == lockObsGuid.Value))
                            && !db.Assertions.Any(r => r.TenantId == a.TenantId && r.RetractsId == a.Id),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new CollectionQueryTarget(
                row.Id,
                row.StudyId,
                row.StudySiteId,
                row.EnrollmentId,
                row.CollectionSpecId,
                row.PlannedActivityId,
                row.VersionIndex,
                row.EnrollmentVersion,
                isLocked,
                row.ItemId,
                row.ObservationId,
                row.Revision,
                row.Value is MissingValue { Reason: NullFlavor.NotAsked });
        }
        finally
        {
            if (openedExplicitly)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}

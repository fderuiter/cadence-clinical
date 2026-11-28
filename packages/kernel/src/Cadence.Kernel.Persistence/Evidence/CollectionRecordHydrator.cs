using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class CollectionRecordHydrator
{
    internal static async Task<CollectionInstanceRecord?> FindSingleAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        ProtocolDesign? governingDesign,
        IBlindedTreatmentClassifier blinding,
        CancellationToken cancellationToken)
    {
        var found = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == instance, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = (await db.Observations.AsNoTracking()
                .Where(o => memberIds.Contains(o.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(o => o.Id);
        var values = found.Members
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
            .Select(x => (x.ItemId, x.Observation, Current: x.Observation.Revisions.Single(r => r.Number == x.Observation.Revision)))
            .Select(x => new CollectedValueView(
                x.ItemId, x.Observation.Id, x.Observation.Concept,
                x.Observation.Value, x.Observation.ObservedAt, x.Observation.Revision, x.Current.RecordedBy, x.Current.RecordedAt))
            .ToList();

        // The locks at every scope covering the form or a value, and the assertions about its values.
        var scopes = LockScopes.Covering(found.StudyId, found.StudySiteId, found.EnrollmentId, found.Id, memberIds);
        var scoped = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var memberGuids = memberIds.Select(m => m.Value).ToHashSet();
        var assertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && memberGuids.Contains(a.TargetId));

        // The form's layout and study protocol designs (for blinding classification, K-AUTH-4).
        List<ProtocolDesign> designs;
        ProtocolDesign? resolvedGoverningDesign;
        if (governingDesign is not null)
        {
            var otherVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == found.StudyId && v.Id != found.ProtocolVersionId)
                .Select(v => v.Content)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            designs = [governingDesign];
            foreach (var content in otherVersions)
            {
                if (ProtocolDesignDocument.Read(content).Design is { } otherDesign)
                {
                    designs.Add(otherDesign);
                }
            }

            resolvedGoverningDesign = governingDesign;
        }
        else
        {
            var studyVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == found.StudyId)
                .Select(v => new { v.Id, v.Content })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var designsById = studyVersions.ToDictionary(
                v => v.Id,
                v => ProtocolDesignDocument.Read(v.Content).Design);
            designs = designsById.Values
                .Where(d => d is not null)
                .Select(d => d!)
                .ToList();
            resolvedGoverningDesign = designsById.GetValueOrDefault(found.ProtocolVersionId);
        }

        var spec = resolvedGoverningDesign?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, found.CollectionSpecId, StringComparison.Ordinal));
        var items = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
        var codelistGroups = await LoadCodelistGroupsAsync(db, items, cancellationToken).ConfigureAwait(false);
        var itemCodelists = MapItemCodelists(items, codelistGroups);

        var conceptIds = items.Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        // A signature names the revisions current when it was recorded; any later revision or value supersedes it (#5463).
        string? contentHash = values.Count == 0
            ? null
            : CollectionInstance.ContentHashOf(values.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
        var instanceGuid = found.Id.Value;
        var signatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == instanceGuid)
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var attestations = signatures
            .Select(a => new FormAttestationView(
                a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason,
                !string.Equals(a.TargetContentHash, contentHash, StringComparison.Ordinal),
                CollectionAttestationAndSummaryReader.SignedValues(found, observations, a.RecordedAt, a.TargetContentHash)))
            .ToList();
        var (blindedItemIds, blindedObservationIds) = CollectionMemberBlindingClassifier.ClassifyBlindedMembers(
            blinding, found, collectionTitle, items, observations, concepts, designs, attestations);

        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, found.StudyId, cancellationToken)
            .ConfigureAwait(false);
        var (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers) =
            CollectionReadinessAndTimingHelper.EvaluateFormReadiness(items, values, assertions, found.Id.Value, memberGuids, activeErrorTargets);
        var enrollmentEntity = await db.Enrollments.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == found.EnrollmentId, cancellationToken)
            .ConfigureAwait(false);
        var enrollmentVersion = enrollmentEntity?.VersionIndex ?? 1L;
        var enrollmentReferenceAt = CollectionReadinessAndTimingHelper.ResolveEnrollmentReferenceAt(enrollmentEntity);
        var visitTimingAssessment = CollectionReadinessAndTimingHelper.AssessVisitTiming(
            resolvedGoverningDesign, found.PlannedActivityId, found.CollectedAt, enrollmentReferenceAt);

        return new CollectionInstanceRecord(
            found.Id,
            found.EnrollmentId,
            found.ParticipantId,
            found.StudyId,
            found.StudySiteId,
            found.ProtocolVersionId,
            found.CollectionSpecId,
            found.PlannedActivityId,
            found.CollectedAt,
            found.OpenedBy,
            found.OpenedAt,
            found.VersionIndex,
            items,
            values,
            scoped.Locks(),
            assertions,
            attestations,
            contentHash,
            collectionTitle,
            enrollmentVersion,
            complete,
            missingRequired,
            openQueries,
            answeredQueries,
            errorFindings,
            blockers,
            blindedItemIds,
            blindedObservationIds,
            itemCodelists,
            enrollmentReferenceAt,
            visitTimingAssessment,
            VisitTimingEvaluated: true);
    }

    internal static async Task<IReadOnlyList<CollectionInstanceRecord>> LoadRecordsAsync(
        KernelDbContext db,
        IQueryable<CollectionInstance> query,
        CancellationToken cancellationToken,
        IBlindedTreatmentClassifier? blindingClassifier = null)
    {
        var blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;
        var instances = await query
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var studyIds = instances.Select(c => c.StudyId).Distinct().ToList();
        var rawStudyVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawStudyVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var specsByVersion = IndexSpecsByVersion(designsByVersion);
        var visitsByVersion = IndexPlannedVisitsByVersion(designsByVersion);
        var designsByStudy = rawStudyVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var allMemberIds = instances.SelectMany(c => c.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => allMemberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);

        var conceptIds = rawStudyVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var scopes = instances.Select(i => NodeRef.For(i.StudyId))
            .Concat(instances.Select(i => NodeRef.For(i.StudySiteId)))
            .Concat(instances.Select(i => NodeRef.For(i.EnrollmentId)))
            .Concat(instances.Select(i => NodeRef.For(i.Id)))
            .Concat(allMemberIds.Select(o => NodeRef.For(o)))
            .Distinct()
            .ToList();
        var scoped = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var lockIndex = new BatchLockIndex(scoped.Locks());
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));
        var assertionIndex = new BatchAssertionIndex(allMemberAssertions);

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.TargetId, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId);
        var errorIndexesByStudy = new Dictionary<StudyId, BatchErrorTargetIndex>(studyIds.Count);
        var blindingByStudy = new Dictionary<StudyId, BatchFormBlindingLookup>(studyIds.Count);
        foreach (var studyId in studyIds)
        {
            var studyErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, studyId, cancellationToken)
                .ConfigureAwait(false);
            errorIndexesByStudy[studyId] = new BatchErrorTargetIndex(studyErrorTargets);
            var studyDesigns = designsByStudy.GetValueOrDefault(studyId) ?? [];
            blindingByStudy[studyId] = BatchFormBlindingLookup.FromConcepts(blinding, concepts, studyDesigns);
        }

        var distinctReferencedSpecs = new Dictionary<(ProtocolVersionId VersionId, string SpecId), CollectionSpec>();
        foreach (var instance in instances)
        {
            var specKey = (instance.ProtocolVersionId, instance.CollectionSpecId);
            if (!distinctReferencedSpecs.ContainsKey(specKey)
                && specsByVersion.TryGetValue(instance.ProtocolVersionId, out var specsForVersion)
                && specsForVersion.TryGetValue(instance.CollectionSpecId, out var matchedSpec))
            {
                distinctReferencedSpecs.Add(specKey, matchedSpec);
            }
        }

        var codelistGroups = await LoadCodelistGroupsAsync(
            db,
            distinctReferencedSpecs.Values.SelectMany(s => s.Items),
            cancellationToken).ConfigureAwait(false);
        var itemCodelistsBySpec = new Dictionary<(ProtocolVersionId VersionId, string SpecId), IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>?>(
            distinctReferencedSpecs.Count);
        foreach (var (specKey, matchedSpec) in distinctReferencedSpecs)
        {
            itemCodelistsBySpec[specKey] = MapItemCodelists(matchedSpec.Items, codelistGroups);
        }

        var enrollmentIds = instances.Select(i => i.EnrollmentId).Distinct().ToList();
        var enrollmentEntities = await db.Enrollments.AsNoTracking()
            .Where(e => enrollmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken)
            .ConfigureAwait(false);

        var records = new List<CollectionInstanceRecord>(instances.Count);
        foreach (var instance in instances)
        {
            specsByVersion.TryGetValue(instance.ProtocolVersionId, out var specsForVersion);
            CollectionSpec? spec = null;
            specsForVersion?.TryGetValue(instance.CollectionSpecId, out spec);
            var items = spec?.Items ?? [];
            var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
            itemCodelistsBySpec.TryGetValue((instance.ProtocolVersionId, instance.CollectionSpecId), out var itemCodelists);

            var values = instance.Members
                .Where(m => observations.ContainsKey(m.ObservationId))
                .OrderBy(m => m.ItemId, StringComparer.Ordinal)
                .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
                .Select(x => (x.ItemId, x.Observation, Current: x.Observation.Revisions.Single(r => r.Number == x.Observation.Revision)))
                .Select(x => new CollectedValueView(
                    x.ItemId, x.Observation.Id, x.Observation.Concept,
                    x.Observation.Value, x.Observation.ObservedAt, x.Observation.Revision, x.Current.RecordedBy, x.Current.RecordedAt))
                .ToList();

            var memberGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
            var coveringLocks = lockIndex.ForForm(
                instance.StudyId.Value,
                instance.StudySiteId.Value,
                instance.EnrollmentId.Value,
                instance.Id.Value,
                memberGuids);
            var assertions = assertionIndex.ForForm(memberGuids);

            string? contentHash = values.Count == 0
                ? null
                : CollectionInstance.ContentHashOf(values.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
            var attestations = signaturesByInstance[instance.Id.Value]
                .Select(a => new FormAttestationView(
                    a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason,
                    !string.Equals(a.TargetContentHash, contentHash, StringComparison.Ordinal),
                    CollectionAttestationAndSummaryReader.SignedValues(instance, observations, a.RecordedAt, a.TargetContentHash)))
                .ToList();

            var (blindedItemIds, blindedObservationIds) = blindingByStudy[instance.StudyId].ClassifyForm(
                instance,
                collectionTitle,
                items,
                observations,
                attestations);
            var errorFindings = errorIndexesByStudy[instance.StudyId].CountForForm(instance.Id.Value, memberGuids);
            var (complete, missingRequired, openQueries, answeredQueries, _, blockers) =
                CollectionReadinessAndTimingHelper.EvaluateFormReadinessWithErrorCount(items, values, assertions, errorFindings);

            var matchedEnrollment = enrollmentEntities.GetValueOrDefault(instance.EnrollmentId);
            var enrollmentReferenceAt = CollectionReadinessAndTimingHelper.ResolveEnrollmentReferenceAt(matchedEnrollment);
            visitsByVersion.TryGetValue(instance.ProtocolVersionId, out var visitsForVersion);
            records.Add(new CollectionInstanceRecord(
                instance.Id,
                instance.EnrollmentId,
                instance.ParticipantId,
                instance.StudyId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                instance.PlannedActivityId,
                instance.CollectedAt,
                instance.OpenedBy,
                instance.OpenedAt,
                instance.VersionIndex,
                items,
                values,
                coveringLocks,
                assertions,
                attestations,
                contentHash,
                collectionTitle,
                matchedEnrollment?.VersionIndex ?? 1L,
                complete,
                missingRequired,
                openQueries,
                answeredQueries,
                errorFindings,
                blockers,
                blindedItemIds,
                blindedObservationIds,
                itemCodelists,
                enrollmentReferenceAt,
                CollectionReadinessAndTimingHelper.AssessVisitTiming(visitsForVersion, instance.PlannedActivityId, instance.CollectedAt, enrollmentReferenceAt),
                VisitTimingEvaluated: true));
        }

        return records;
    }

    internal static async Task<Dictionary<(CodeSystemVersionId Version, string GroupCode), IReadOnlyList<CodelistOptionView>>> LoadCodelistGroupsAsync(
        KernelDbContext db,
        IEnumerable<CollectionItem> items,
        CancellationToken cancellationToken)
    {
        var keys = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Terminology?.Codelist))
            .Select(i => (i.Terminology!.CodeSystemVersionId, GroupCode: i.Terminology.Codelist!))
            .Distinct()
            .ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var versionIds = keys.Select(k => k.CodeSystemVersionId).Distinct().ToList();
        var groupCodes = keys.Select(k => k.GroupCode).Distinct(StringComparer.Ordinal).ToList();
        var rawEntries = await db.CodeSystemEntries.AsNoTracking()
            .Where(e => versionIds.Contains(e.CodeSystemVersionId) && e.GroupCode != null && groupCodes.Contains(e.GroupCode))
            .OrderBy(e => e.Code)
            .ThenBy(e => e.Ordinal)
            .Select(e => new { e.CodeSystemVersionId, e.GroupCode, e.Code, e.Display, e.SubmissionValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rawEntries
            .GroupBy(e => (e.CodeSystemVersionId, GroupCode: e.GroupCode!))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CodelistOptionView>)g
                    .DistinctBy(e => e.Code, StringComparer.Ordinal)
                    .Select(e => new CodelistOptionView(e.Code, e.Display, e.SubmissionValue))
                    .ToList());
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? MapItemCodelists(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<(CodeSystemVersionId Version, string GroupCode), IReadOnlyList<CodelistOptionView>> groups)
    {
        Dictionary<string, IReadOnlyList<CodelistOptionView>>? mapped = null;
        foreach (var item in items)
        {
            if (item.Terminology is not { } terminology || string.IsNullOrWhiteSpace(terminology.Codelist))
            {
                continue;
            }

            mapped ??= new Dictionary<string, IReadOnlyList<CodelistOptionView>>(StringComparer.Ordinal);
            mapped[item.Id] = groups.GetValueOrDefault((terminology.CodeSystemVersionId, terminology.Codelist)) ?? [];
        }

        return mapped;
    }

    internal static Dictionary<ProtocolVersionId, IReadOnlyDictionary<string, CollectionSpec>> IndexSpecsByVersion(
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign?> designsByVersion)
    {
        var result = new Dictionary<ProtocolVersionId, IReadOnlyDictionary<string, CollectionSpec>>(designsByVersion.Count);
        foreach (var (versionId, design) in designsByVersion)
        {
            if (design is null || design.CollectionSpecs.Count == 0)
            {
                result[versionId] = FrozenDictionary<string, CollectionSpec>.Empty;
                continue;
            }

            var byId = new Dictionary<string, CollectionSpec>(design.CollectionSpecs.Count, StringComparer.Ordinal);
            foreach (var spec in design.CollectionSpecs)
            {
                byId.TryAdd(spec.Id, spec);
            }

            result[versionId] = byId;
        }

        return result;
    }

    internal static Dictionary<ProtocolVersionId, IReadOnlyDictionary<string, PlannedVisit>> IndexPlannedVisitsByVersion(
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign?> designsByVersion)
    {
        var result = new Dictionary<ProtocolVersionId, IReadOnlyDictionary<string, PlannedVisit>>(designsByVersion.Count);
        foreach (var (versionId, design) in designsByVersion)
        {
            result[versionId] = design is null
                ? FrozenDictionary<string, PlannedVisit>.Empty
                : ExpectedSchedule.IndexByActivity(design);
        }

        return result;
    }
}

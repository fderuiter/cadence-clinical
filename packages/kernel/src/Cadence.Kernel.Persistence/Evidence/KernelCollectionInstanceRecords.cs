using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads a form instance, its member observations, the assertions and locks that concern them and the signatures over
/// the form through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelCollectionInstanceRecords(KernelTenantDbContextFactory contexts) : ICollectionInstanceRecords
{
    public async Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
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
        var studyVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == found.StudyId)
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designs = studyVersions
            .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();
        var governingContent = studyVersions.Single(v => v.Id == found.ProtocolVersionId).Content;
        var spec = ProtocolDesignDocument.Read(governingContent).Design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, found.CollectionSpecId, StringComparison.Ordinal));
        var items = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;

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
        var (blindedItemIds, blindedObservationIds) = ClassifyBlindedMembers(found, items, observations, concepts, designs);

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
                SignedValues(found, observations, a.RecordedAt, a.TargetContentHash)))
            .ToList();

        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, found.StudyId, cancellationToken)
            .ConfigureAwait(false);
        var (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers) =
            EvaluateFormReadiness(items, values, assertions, found.Id.Value, memberGuids, activeErrorTargets);
        var enrollmentVersion = await db.Enrollments.AsNoTracking()
            .Where(e => e.Id == found.EnrollmentId)
            .Select(e => (long?)e.VersionIndex)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 1L;

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
            blindedObservationIds);
    }

    public async Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var instances = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.EnrollmentId == enrollment)
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var versionIds = instances.Select(c => c.ProtocolVersionId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var specsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design?.CollectionSpecs ?? []);

        var allMemberIds = instances.SelectMany(c => c.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => allMemberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);

        var first = instances[0];
        List<NodeRef> allScopes =
        [
            NodeRef.For(first.StudyId),
            NodeRef.For(first.StudySiteId),
            NodeRef.For(enrollment),
            .. instances.Select(i => NodeRef.For(i.Id)),
            .. allMemberIds.Select(o => NodeRef.For(o)),
        ];
        var scoped = await ScopedAssertions.ReadAsync(db, allScopes, cancellationToken).ConfigureAwait(false);
        var allLocks = scoped.Locks();
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped
            .About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));
        var verifiedRevisions = allMemberAssertions
            .Where(a => a.Kind == AssertionKind.Verified && a.RetractedAt is null && a.Revision is not null)
            .Select(a => (a.Target.Id, Revision: a.Revision!.Value))
            .ToHashSet();

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .Select(a => new { a.TargetId, a.TargetContentHash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId, a => a.TargetContentHash);
        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, first.StudyId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. instances.Select(instance => ToSummary(
                instance,
                specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [],
                observations,
                allLocks,
                allMemberAssertions,
                verifiedRevisions,
                signaturesByInstance[instance.Id.Value],
                activeErrorTargets)),
        ];
    }

    public async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var distinctIds = instances.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
        }

        await using var db = contexts.Create(tenant);
        var records = await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => distinctIds.Contains(c.Id)),
            cancellationToken).ConfigureAwait(false);
        return records.ToDictionary(r => r.CollectionInstanceId);
    }

    public async Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var distinctIds = observations.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<ObservationId, int>();
        }

        await using var db = contexts.Create(tenant);
        return await db.Observations.AsNoTracking()
            .Where(o => distinctIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Revision })
            .ToDictionaryAsync(x => x.Id, x => x.Revision, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => c.EnrollmentId == enrollment),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => c.StudyId == study),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<CollectionInstanceRecord>> LoadRecordsAsync(
        KernelDbContext db,
        IQueryable<CollectionInstance> query,
        CancellationToken cancellationToken)
    {
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
        var specsByVersion = designsByVersion.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.CollectionSpecs ?? []);
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
        var allLocks = scoped.Locks();
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.TargetId, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId);
        var errorTargetsByStudy = new Dictionary<StudyId, IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)>>();
        foreach (var studyId in instances.Select(i => i.StudyId).Distinct())
        {
            errorTargetsByStudy[studyId] = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, studyId, cancellationToken)
                .ConfigureAwait(false);
        }

        var enrollmentIds = instances.Select(i => i.EnrollmentId).Distinct().ToList();
        var enrollmentVersions = await db.Enrollments.AsNoTracking()
            .Where(e => enrollmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.VersionIndex })
            .ToDictionaryAsync(e => e.Id, e => e.VersionIndex, cancellationToken)
            .ConfigureAwait(false);

        var records = new List<CollectionInstanceRecord>(instances.Count);
        foreach (var instance in instances)
        {
            var specs = specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            var items = spec?.Items ?? [];
            var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;

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
            var coveringLocks = allLocks
                .Where(l =>
                    (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == instance.StudyId.Value)
                    || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == instance.StudySiteId.Value)
                    || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == instance.EnrollmentId.Value)
                    || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == instance.Id.Value)
                    || (l.Scope.Kind == NodeKinds.Observation && memberGuids.Contains(l.Scope.Id)))
                .ToList();
            var assertions = FilterFormAssertions(allMemberAssertions, memberGuids);
            var studyDesigns = designsByStudy.GetValueOrDefault(instance.StudyId) ?? [];
            var (blindedItemIds, blindedObservationIds) = ClassifyBlindedMembers(instance, items, observations, concepts, studyDesigns);

            string? contentHash = values.Count == 0
                ? null
                : CollectionInstance.ContentHashOf(values.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
            var attestations = signaturesByInstance[instance.Id.Value]
                .Select(a => new FormAttestationView(
                    a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason,
                    !string.Equals(a.TargetContentHash, contentHash, StringComparison.Ordinal),
                    SignedValues(instance, observations, a.RecordedAt, a.TargetContentHash)))
                .ToList();

            var activeErrorTargets = errorTargetsByStudy[instance.StudyId];
            var (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers) =
                EvaluateFormReadiness(items, values, assertions, instance.Id.Value, memberGuids, activeErrorTargets);

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
                enrollmentVersions.GetValueOrDefault(instance.EnrollmentId, 1L),
                complete,
                missingRequired,
                openQueries,
                answeredQueries,
                errorFindings,
                blockers,
                blindedItemIds,
                blindedObservationIds));
        }

        return records;
    }

    private static CollectionInstanceSummary ToSummary(
        CollectionInstance instance,
        IReadOnlyList<CollectionSpec> specs,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyList<LockView> allLocks,
        IReadOnlyList<AssertionView> allMemberAssertions,
        IReadOnlySet<(Guid ObservationId, int Revision)> verifiedRevisions,
        IEnumerable<string> signatureHashes,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets)
    {
        var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
        var specItems = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;

        var memberObservations = instance.Members
            .Where(m => observations.ContainsKey(m.ObservationId))
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
            .ToList();
        var recordedMembers = memberObservations
            .Where(m => m.Observation.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .ToList();
        var recordedItems = recordedMembers.Select(m => m.ItemId).ToHashSet(StringComparer.Ordinal);

        var itemCount = specItems.Select(i => i.Id)
            .Concat(instance.Members.Select(m => m.ItemId))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var missingRequired = specItems
            .Where(i => i.Required && !recordedItems.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        var missingRequiredCount = missingRequired.Count;
        var recordedCount = recordedMembers.Count;

        var verifiedCount = recordedMembers.Count(m => verifiedRevisions.Contains((m.Observation.Id.Value, m.Observation.Revision)));
        var unverifiedCount = recordedCount - verifiedCount;

        var memberGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
        var assertions = FilterFormAssertions(allMemberAssertions, memberGuids);
        var (openQueryCount, answeredQueryCount) = CountActiveQueries(assertions);
        var openErrorFindingCount = CountFormErrorFindings(instance.Id.Value, memberGuids, activeErrorTargets);
        var complete = CollectionReadiness.IsComplete(missingRequiredCount, openQueryCount, answeredQueryCount, openErrorFindingCount);
        var blockers = CollectionReadiness.BuildBlockers(
            missingRequired,
            missingRequiredCount,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount);

        var coveringLocks = allLocks
            .Where(l =>
                (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == instance.StudyId.Value)
                || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == instance.StudySiteId.Value)
                || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == instance.EnrollmentId.Value)
                || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == instance.Id.Value)
                || (l.Scope.Kind == NodeKinds.Observation && memberGuids.Contains(l.Scope.Id)))
            .ToList();
        var locked = coveringLocks.Any(l => l.Kind == LockKind.Locked);
        var frozen = coveringLocks.Any(l => l.Kind == LockKind.Frozen);

        string? contentHash = memberObservations.Count == 0
            ? null
            : CollectionInstance.ContentHashOf(
                memberObservations.Select(m => new CollectionContentEntry(m.Observation.Id, m.Observation.Revision, m.Observation.Value)));
        var hashes = signatureHashes.ToList();
        var signed = contentHash is not null && hashes.Any(h => string.Equals(h, contentHash, StringComparison.Ordinal));
        var signatureSuperseded = !signed && hashes.Count > 0;

        return new CollectionInstanceSummary(
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
            complete,
            itemCount,
            recordedCount,
            collectionTitle,
            missingRequiredCount,
            verifiedCount,
            unverifiedCount,
            locked,
            frozen,
            coveringLocks.Count,
            signed,
            signatureSuperseded,
            hashes.Count,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount,
            blockers);
    }

    internal static List<AssertionView> FilterFormAssertions(
        IReadOnlyList<AssertionView> allAssertions,
        IReadOnlySet<Guid> memberObservationGuids)
    {
        var formQueryGuids = allAssertions
            .Where(a => a.Kind == AssertionKind.Queried && a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        return [.. allAssertions
            .Where(a => (a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
                || (a.Target.Kind == NodeKinds.Assertion && formQueryGuids.Contains(a.Target.Id)))];
    }

    internal static (int OpenCount, int AnsweredCount) CountActiveQueries(
        IReadOnlyList<AssertionView> assertions,
        IReadOnlySet<Guid>? excludeQueryIds = null)
    {
        var activeQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.RetractedAt is null
                && (excludeQueryIds is null || !excludeQueryIds.Contains(a.AssertionId.Value)))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        if (activeQueryIds.Count == 0)
        {
            return (0, 0);
        }

        var answeredQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractedAt is null
                && a.Target.Kind == NodeKinds.Assertion
                && activeQueryIds.Contains(a.Target.Id))
            .Select(a => a.Target.Id)
            .ToHashSet();
        var answeredCount = answeredQueryIds.Count;
        return (activeQueryIds.Count - answeredCount, answeredCount);
    }

    internal static int CountFormErrorFindings(
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets) =>
        activeErrorTargets.Count(entry =>
            (entry.Target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(entry.Target.Key, out var recordGuid)
                && recordGuid == instanceGuid)
            || (entry.Target.Kind == FindingTargetKind.Observation
                && entry.Target.Node is { Kind: NodeKinds.Observation } obsNode
                && memberObservationGuids.Contains(obsNode.Id)));

    private static (
        bool Complete,
        IReadOnlyList<string> MissingRequired,
        int OpenQueries,
        int AnsweredQueries,
        int ErrorFindings,
        IReadOnlyList<string> Blockers) EvaluateFormReadiness(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyList<CollectedValueView> values,
        IReadOnlyList<AssertionView> assertions,
        Guid instanceGuid,
        IReadOnlySet<Guid> memberGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets)
    {
        var recordedItems = values
            .Where(v => v.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .Select(v => v.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        var missingRequired = items
            .Where(i => i.Required && !recordedItems.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        var (openQueries, answeredQueries) = CountActiveQueries(assertions);
        var errorFindings = CountFormErrorFindings(instanceGuid, memberGuids, activeErrorTargets);
        var complete = CollectionReadiness.IsComplete(missingRequired.Count, openQueries, answeredQueries, errorFindings);
        var blockers = CollectionReadiness.BuildBlockers(missingRequired, missingRequired.Count, openQueries, answeredQueries, errorFindings);
        return (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers);
    }

    /// <summary>
    /// The revisions a signature recorded at <paramref name="signedAt"/> signed: each member that had joined by then, at
    /// its latest revision recorded by then. Returned only when they hash to <paramref name="signedHash"/>, so what the
    /// form shows as signed is exactly what was signed.
    /// </summary>
    private static List<SignedValueView>? SignedValues(
        CollectionInstance instance, IReadOnlyDictionary<ObservationId, Observation> observations, DateTimeOffset signedAt, string signedHash)
    {
        var signed = instance.Members
            .Where(m => m.JoinedAt <= signedAt)
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Revision: observations[m.ObservationId].Revisions.Where(r => r.RecordedAt <= signedAt).MaxBy(r => r.Number)))
            .Where(x => x.Revision is not null)
            .Select(x => new SignedValueView(x.ItemId, x.Revision!.ObservationId, x.Revision.Number, x.Revision.Value))
            .ToList();
        var hash = CollectionInstance.ContentHashOf(signed.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
        return string.Equals(hash, signedHash, StringComparison.Ordinal) ? signed : null;
    }

    private static (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyBlindedMembers(
        CollectionInstance instance,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyDictionary<ConceptId, Concept> concepts,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var blindedItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            concepts.TryGetValue(item.Concept.ConceptId, out var concept);
            if (BlindedObservationClassifier.IsBlindedTreatment(
                    item.Concept.ConceptId,
                    new TextValue(string.Empty),
                    concept,
                    instance.CollectionSpecId,
                    item.Id,
                    designs))
            {
                blindedItems.Add(item.Id);
            }
        }

        var blindedObservations = new HashSet<ObservationId>();
        foreach (var member in instance.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            concepts.TryGetValue(obs.ConceptId, out var concept);
            if (blindedItems.Contains(member.ItemId)
                || BlindedObservationClassifier.IsBlindedTreatment(
                    obs.ConceptId,
                    obs.Value,
                    concept,
                    instance.CollectionSpecId,
                    member.ItemId,
                    designs))
            {
                blindedItems.Add(member.ItemId);
                blindedObservations.Add(obs.Id);
            }
        }

        return (
            blindedItems.Count == 0 ? FrozenSet<string>.Empty : blindedItems,
            blindedObservations.Count == 0 ? FrozenSet<ObservationId>.Empty : blindedObservations);
    }
}

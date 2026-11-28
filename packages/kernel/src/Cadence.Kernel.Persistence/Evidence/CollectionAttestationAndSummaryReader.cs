using System;
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

internal static class CollectionAttestationAndSummaryReader
{
    internal static async Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        KernelDbContext db,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
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
        var designsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var specsByVersion = CollectionRecordHydrator.IndexSpecsByVersion(designsByVersion);
        var visitsByVersion = CollectionRecordHydrator.IndexPlannedVisitsByVersion(designsByVersion);

        var allMemberIds = instances.SelectMany(c => c.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, ObservationSummaryRow>()
            : await db.Observations.AsNoTracking()
                .Where(o => allMemberIds.Contains(o.Id))
                .Select(o => new ObservationSummaryRow(o.Id, o.Value, o.Revision))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

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
        var lockIndex = new BatchLockIndex(scoped.Locks());
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped
            .About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));
        var assertionIndex = new BatchAssertionIndex(allMemberAssertions);

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .Select(a => new { a.TargetId, a.TargetContentHash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId, a => a.TargetContentHash);
        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, first.StudyId, cancellationToken)
            .ConfigureAwait(false);
        var errorIndex = new BatchErrorTargetIndex(activeErrorTargets);
        var enrollmentEntity = await db.Enrollments.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == enrollment, cancellationToken)
            .ConfigureAwait(false);
        var enrollmentReferenceAt = CollectionReadinessAndTimingHelper.ResolveEnrollmentReferenceAt(enrollmentEntity);

        var summaries = new List<CollectionInstanceSummary>(instances.Count);
        foreach (var instance in instances)
        {
            specsByVersion.TryGetValue(instance.ProtocolVersionId, out var specsForVersion);
            CollectionSpec? spec = null;
            specsForVersion?.TryGetValue(instance.CollectionSpecId, out spec);
            visitsByVersion.TryGetValue(instance.ProtocolVersionId, out var visitsForVersion);
            summaries.Add(ToSummary(
                instance,
                spec,
                visitsForVersion,
                observations,
                lockIndex,
                assertionIndex,
                signaturesByInstance[instance.Id.Value],
                errorIndex,
                enrollmentReferenceAt));
        }

        return summaries;
    }

    internal static async Task<FormAttestationView?> FindAttestationAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        AttestationId attestation,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAttestationCoreAsync(db, instance, attestation, cancellationToken).ConfigureAwait(false);
        return loaded?.View;
    }

    internal static async Task<FormAttestationView?> FindAttestationForReaderAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        AttestationId attestation,
        bool unblinded,
        ProtocolDesign? governingDesign,
        IBlindedTreatmentClassifier blinding,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAttestationCoreAsync(db, instance, attestation, cancellationToken).ConfigureAwait(false);
        if (loaded is not { } core)
        {
            return null;
        }

        if (unblinded)
        {
            return core.View;
        }

        List<ProtocolDesign> designs;
        ProtocolDesign? resolvedGoverningDesign;
        if (governingDesign is not null)
        {
            designs = [governingDesign];
            resolvedGoverningDesign = governingDesign;
        }
        else
        {
            var studyVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == core.Found.StudyId)
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
            resolvedGoverningDesign = designsById.GetValueOrDefault(core.Found.ProtocolVersionId);
        }

        var spec = resolvedGoverningDesign?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, core.Found.CollectionSpecId, StringComparison.Ordinal));
        var items = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
        var conceptIds = items.Select(i => i.Concept.ConceptId)
            .Concat(core.Observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var (blindedItemIds, blindedObservationIds) = CollectionMemberBlindingClassifier.ClassifyBlindedMembers(
            blinding,
            core.Found,
            collectionTitle,
            items,
            core.Observations,
            concepts,
            designs,
            [core.View]);
        if (blindedItemIds.Count == 0 && blindedObservationIds.Count == 0)
        {
            return core.View;
        }

        return core.View with
        {
            ContentHash = "masked",
            Reason = "masked",
            SignedValues = core.View.SignedValues?
                .Select(sv =>
                {
                    if (!blindedItemIds.Contains(sv.ItemId) && !blindedObservationIds.Contains(sv.ObservationId))
                    {
                        return sv;
                    }

                    var maskedValue = sv.Value is MissingValue { Reason: NullFlavor.NotAsked }
                        ? sv.Value
                        : new MissingValue(NullFlavor.Masked);
                    return sv with
                    {
                        ObservationId = ObservationId.Empty,
                        Value = maskedValue,
                    };
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The revisions a signature recorded at <paramref name="signedAt"/> signed: each member that had joined by then, at
    /// its latest revision recorded by then. Returned only when they hash to <paramref name="signedHash"/>, so what the
    /// form shows as signed is exactly what was signed.
    /// </summary>
    internal static List<SignedValueView>? SignedValues(
        CollectionInstance instance,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        DateTimeOffset signedAt,
        string signedHash)
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

    private static async Task<(CollectionInstance Found, Dictionary<ObservationId, Observation> Observations, FormAttestationView View)?> LoadAttestationCoreAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        AttestationId attestation,
        CancellationToken cancellationToken)
    {
        var found = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == instance, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var instanceGuid = found.Id.Value;
        var signature = await db.Attestations.AsNoTracking()
            .Where(a => a.Id == attestation && a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == instanceGuid)
            .Select(a => new { a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (signature is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = memberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => memberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);
        var currentEntries = found.Members
            .Where(m => observations.ContainsKey(m.ObservationId))
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => observations[m.ObservationId])
            .Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value))
            .ToList();
        var contentHash = currentEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(currentEntries);
        var view = new FormAttestationView(
            signature.Id,
            signature.Meaning,
            signature.ActorId,
            signature.RecordedAt,
            signature.TargetContentHash,
            signature.Reason,
            !string.Equals(signature.TargetContentHash, contentHash, StringComparison.Ordinal),
            SignedValues(found, observations, signature.RecordedAt, signature.TargetContentHash));
        return (found, observations, view);
    }

    private static CollectionInstanceSummary ToSummary(
        CollectionInstance instance,
        CollectionSpec? spec,
        IReadOnlyDictionary<string, PlannedVisit>? visitsByActivity,
        IReadOnlyDictionary<ObservationId, ObservationSummaryRow> observations,
        BatchLockIndex lockIndex,
        BatchAssertionIndex assertionIndex,
        IEnumerable<string> signatureHashes,
        BatchErrorTargetIndex errorIndex,
        DateTimeOffset? enrollmentReferenceAt = null)
    {
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

        var verifiedCount = recordedMembers.Count(m => assertionIndex.IsVerified(m.Observation.Id.Value, m.Observation.Revision));
        var unverifiedCount = recordedCount - verifiedCount;

        var memberGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
        var assertions = assertionIndex.ForForm(memberGuids);
        var (activeQueryAssertionIds, answeredQueryAssertionIds) = CollectionReadinessAndTimingHelper.ExtractActiveQueryAssertionIds(assertions);
        var openQueryCount = activeQueryAssertionIds.Count - answeredQueryAssertionIds.Count;
        var answeredQueryCount = answeredQueryAssertionIds.Count;
        var openErrorFindingCount = errorIndex.CountForForm(instance.Id.Value, memberGuids);
        var complete = (itemCount == 0 || recordedCount > 0)
            && CollectionReadiness.IsComplete(missingRequiredCount, openQueryCount, answeredQueryCount, openErrorFindingCount);
        var blockers = CollectionReadiness.BuildBlockers(
            missingRequired,
            missingRequiredCount,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount);

        var (locked, frozen, coveringLockCount) = lockIndex.SummarizeForForm(
            instance.StudyId.Value,
            instance.StudySiteId.Value,
            instance.EnrollmentId.Value,
            instance.Id.Value,
            memberGuids);

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
            coveringLockCount,
            signed,
            signatureSuperseded,
            hashes.Count,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount,
            blockers,
            enrollmentReferenceAt,
            activeQueryAssertionIds,
            answeredQueryAssertionIds,
            CollectionReadinessAndTimingHelper.AssessVisitTiming(visitsByActivity, instance.PlannedActivityId, instance.CollectedAt, enrollmentReferenceAt),
            VisitTimingEvaluated: true);
    }
}

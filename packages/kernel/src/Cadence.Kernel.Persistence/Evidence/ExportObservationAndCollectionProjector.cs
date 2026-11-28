using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class ExportObservationAndCollectionProjector
{
    internal static async Task<(IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations)> BuildObservationsAndCollectionsAsync(
        KernelDbContext db,
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var conceptIds = observationsRaw.Select(o => o.ConceptId).Distinct().ToList();
        var conceptsById = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Include(c => c.Revisions)
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        return AssembleObservationsAndCollections(
            protocols,
            designs,
            conceptsById,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt);
    }

    internal static (IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations) AssembleObservationsAndCollections(
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt)
    {
        var specNamesByVersion = new Dictionary<VersionedElementKey, string>(VersionedElementKeyComparer.Instance);
        var activityNamesByVersion = new Dictionary<VersionedElementKey, string>(VersionedElementKeyComparer.Instance);
        foreach (var protocol in protocols)
        {
            if (protocol.Design is not { } design)
            {
                continue;
            }

            foreach (var spec in design.CollectionSpecs)
            {
                specNamesByVersion.TryAdd(new VersionedElementKey(protocol.ProtocolVersionId, spec.Id), spec.Name);
            }

            foreach (var activity in design.PlannedActivities)
            {
                activityNamesByVersion.TryAdd(new VersionedElementKey(protocol.ProtocolVersionId, activity.Id), activity.Name);
            }
        }

        var collectionByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in collectionsRaw)
        {
            foreach (var member in instance.Members)
            {
                if (member.JoinedAt <= knownAt)
                {
                    collectionByObservation[member.ObservationId] = (instance, member.ItemId);
                }
            }
        }

        var activeLockByScope = new Dictionary<ScopeKey, LockKind>(ScopeKeyComparer.Instance);
        foreach (var lockEntry in lockEntries)
        {
            if (lockEntry.Released)
            {
                continue;
            }

            var scopeKey = new ScopeKey(lockEntry.Scope.Kind, lockEntry.Scope.Id);
            if (lockEntry.Kind == LockKind.Locked)
            {
                activeLockByScope[scopeKey] = LockKind.Locked;
            }
            else
            {
                activeLockByScope.TryAdd(scopeKey, LockKind.Frozen);
            }
        }

        var directAssertionsByObservation = new Dictionary<Guid, List<Assertion>>();
        var answersByQueryId = new Dictionary<Guid, List<Assertion>>();
        var nonLockRetractedBy = new Dictionary<AssertionId, Assertion>();
        foreach (var assertion in assertionsRaw)
        {
            if (assertion.IsLock)
            {
                continue;
            }

            if (assertion.RetractsId is { } retractedId)
            {
                if (!nonLockRetractedBy.TryGetValue(retractedId, out var existingRetraction)
                    || IsEarlierAssertion(assertion, existingRetraction))
                {
                    nonLockRetractedBy[retractedId] = assertion;
                }

                continue;
            }

            if (string.Equals(assertion.TargetKind, NodeKinds.Observation, StringComparison.Ordinal))
            {
                if (!directAssertionsByObservation.TryGetValue(assertion.TargetId, out var obsList))
                {
                    obsList = [];
                    directAssertionsByObservation[assertion.TargetId] = obsList;
                }

                obsList.Add(assertion);
            }
            else if (assertion.Kind == AssertionKind.QueryAnswered
                && string.Equals(assertion.TargetKind, NodeKinds.Assertion, StringComparison.Ordinal))
            {
                if (!answersByQueryId.TryGetValue(assertion.TargetId, out var answerList))
                {
                    answerList = [];
                    answersByQueryId[assertion.TargetId] = answerList;
                }

                answerList.Add(assertion);
            }
        }

        var blindingLookup = BlindedObservationClassifier.CreateLookup(conceptsById, designs);
        var conceptDisplayByRef = new Dictionary<ConceptRef, (string Code, string Label)>();
        var currentAtCutByObservation = new Dictionary<ObservationId, (int Revision, ObservationValue Value)>(observationsRaw.Count);
        var observationSnapshots = new List<ExportObservationSnapshot>(observationsRaw.Count);

        foreach (var obs in observationsRaw)
        {
            var revsAtCut = SelectRevisionsAtCut(obs.Revisions, knownAt);
            if (revsAtCut.Count == 0)
            {
                continue;
            }

            var views = new List<ObservationRevisionView>(revsAtCut.Count);
            for (var idx = 0; idx < revsAtCut.Count; idx++)
            {
                var r = revsAtCut[idx];
                views.Add(new ObservationRevisionView(
                    r.Number,
                    r.Value,
                    r.ReasonForChange,
                    r.RecordedBy,
                    r.RecordedAt,
                    idx + 1 < revsAtCut.Count ? revsAtCut[idx + 1].RecordedAt : null));
            }

            var currentRev = views[^1];
            currentAtCutByObservation[obs.Id] = (currentRev.Number, currentRev.Value);

            collectionByObservation.TryGetValue(obs.Id, out var membership);
            var studySiteId = studySiteByEnrollment[obs.EnrollmentId];

            if (!conceptDisplayByRef.TryGetValue(obs.Concept, out var conceptDisplay))
            {
                conceptsById.TryGetValue(obs.ConceptId, out var concept);
                var fallbackId = concept is null ? obs.ConceptId.ToString() : null;
                var revLabel = concept?.Revisions.FirstOrDefault(r => r.Version == obs.ConceptVersion)?.PreferredLabel
                    ?? concept?.PreferredLabel
                    ?? fallbackId!;
                var conceptCode = concept?.ExternalCode ?? fallbackId ?? obs.ConceptId.ToString();
                conceptDisplay = (conceptCode, revLabel);
                conceptDisplayByRef[obs.Concept] = conceptDisplay;
            }

            bool isBlinded = blindingLookup.IsBlindedTreatment(
                obs.ConceptId,
                currentRev.Value,
                membership.Instance?.CollectionSpecId,
                membership.ItemId);

            LockKind? effectiveLock = activeLockByScope.Count == 0
                ? null
                : ResolveEffectiveLock(
                    activeLockByScope,
                    obs.StudyId,
                    studySiteId,
                    obs.EnrollmentId,
                    membership.Instance?.Id,
                    obs.Id);

            IReadOnlyList<AssertionView> aboutViews = directAssertionsByObservation.TryGetValue(obs.Id.Value, out var directAssertions)
                ? BuildObservationAssertionViews(directAssertions, answersByQueryId, nonLockRetractedBy)
                : [];

            observationSnapshots.Add(new ExportObservationSnapshot(
                obs.Id,
                obs.EnrollmentId,
                obs.ParticipantId,
                studySiteId,
                membership.Instance?.Id,
                membership.Instance?.CollectionSpecId,
                membership.Instance?.PlannedActivityId,
                membership.ItemId,
                obs.Concept,
                conceptDisplay.Code,
                conceptDisplay.Label,
                isBlinded,
                obs.ObservedAt,
                currentRev,
                views,
                effectiveLock,
                aboutViews));
        }

        var collectionSnapshots = new List<ExportCollectionSnapshot>(collectionsRaw.Count);
        foreach (var instance in collectionsRaw)
        {
            var specName = specNamesByVersion.GetValueOrDefault(
                new VersionedElementKey(instance.ProtocolVersionId, instance.CollectionSpecId),
                instance.CollectionSpecId);
            var activityName = instance.PlannedActivityId is { } plannedActivityId
                ? activityNamesByVersion.GetValueOrDefault(
                    new VersionedElementKey(instance.ProtocolVersionId, plannedActivityId),
                    plannedActivityId)
                : null;

            var itemsMap = new Dictionary<ObservationId, string>();
            var hashEntries = new List<CollectionContentEntry>();
            foreach (var member in instance.Members)
            {
                if (member.JoinedAt <= knownAt && currentAtCutByObservation.TryGetValue(member.ObservationId, out var current))
                {
                    itemsMap[member.ObservationId] = member.ItemId;
                    hashEntries.Add(new CollectionContentEntry(member.ObservationId, current.Revision, current.Value));
                }
            }

            var contentHash = hashEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(hashEntries);
            collectionSnapshots.Add(new ExportCollectionSnapshot(
                instance.Id,
                instance.EnrollmentId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                specName,
                instance.PlannedActivityId,
                activityName,
                instance.CollectedAt,
                instance.OpenedAt,
                instance.OpenedBy,
                contentHash,
                itemsMap));
        }

        return (collectionSnapshots, observationSnapshots);
    }

    internal static bool IsEarlierAssertion(Assertion candidate, Assertion existing)
    {
        var cmp = candidate.AssertedAt.CompareTo(existing.AssertedAt);
        return cmp != 0 ? cmp < 0 : candidate.Id.CompareTo(existing.Id) < 0;
    }

    private static List<ObservationRevision> SelectRevisionsAtCut(
        IReadOnlyCollection<ObservationRevision> revisions,
        DateTimeOffset knownAt)
    {
        var revsAtCut = new List<ObservationRevision>(revisions.Count);
        var alreadySorted = true;
        var previousNumber = int.MinValue;
        foreach (var rev in revisions)
        {
            if (rev.RecordedAt <= knownAt)
            {
                if (rev.Number < previousNumber)
                {
                    alreadySorted = false;
                }

                previousNumber = rev.Number;
                revsAtCut.Add(rev);
            }
        }

        if (!alreadySorted)
        {
            revsAtCut.Sort(static (x, y) => x.Number.CompareTo(y.Number));
        }

        return revsAtCut;
    }

    private static LockKind? ResolveEffectiveLock(
        Dictionary<ScopeKey, LockKind> activeLockByScope,
        StudyId studyId,
        StudySiteId studySiteId,
        EnrollmentId enrollmentId,
        CollectionInstanceId? collectionInstanceId,
        ObservationId observationId)
    {
        var hasFrozen = false;
        if (TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Study, studyId.Value), ref hasFrozen)
            || TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.StudySite, studySiteId.Value), ref hasFrozen)
            || TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Enrollment, enrollmentId.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        if (collectionInstanceId is { } cid
            && TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.CollectionInstance, cid.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        if (TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Observation, observationId.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        return hasFrozen ? LockKind.Frozen : null;
    }

    private static bool TryProbeLock(
        Dictionary<ScopeKey, LockKind> activeLockByScope,
        ScopeKey scope,
        ref bool hasFrozen)
    {
        if (!activeLockByScope.TryGetValue(scope, out var kind))
        {
            return false;
        }

        if (kind == LockKind.Locked)
        {
            return true;
        }

        hasFrozen = true;
        return false;
    }

    private static List<AssertionView> BuildObservationAssertionViews(
        List<Assertion> directAssertions,
        Dictionary<Guid, List<Assertion>> answersByQueryId,
        Dictionary<AssertionId, Assertion> nonLockRetractedBy)
    {
        var combined = new List<Assertion>(directAssertions.Count);
        combined.AddRange(directAssertions);
        foreach (var assertion in directAssertions)
        {
            if (assertion.Kind == AssertionKind.Queried
                && answersByQueryId.TryGetValue(assertion.Id.Value, out var answers))
            {
                combined.AddRange(answers);
            }
        }

        combined.Sort(AssertionOrderComparer.Instance);

        var views = new List<AssertionView>(combined.Count);
        foreach (var a in combined)
        {
            views.Add(nonLockRetractedBy.TryGetValue(a.Id, out var ret)
                ? new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, ret.Id, ret.AssertedAt)
                : new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, null, null));
        }

        return views;
    }

    private readonly record struct ScopeKey(string Kind, Guid Id);

    private sealed class ScopeKeyComparer : IEqualityComparer<ScopeKey>
    {
        public static readonly ScopeKeyComparer Instance = new();

        public bool Equals(ScopeKey x, ScopeKey y) =>
            x.Id == y.Id && string.Equals(x.Kind, y.Kind, StringComparison.Ordinal);

        public int GetHashCode(ScopeKey obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Kind), obj.Id);
    }

    private readonly record struct VersionedElementKey(ProtocolVersionId ProtocolVersionId, string ElementId);

    private sealed class VersionedElementKeyComparer : IEqualityComparer<VersionedElementKey>
    {
        public static readonly VersionedElementKeyComparer Instance = new();

        public bool Equals(VersionedElementKey x, VersionedElementKey y) =>
            x.ProtocolVersionId == y.ProtocolVersionId
            && string.Equals(x.ElementId, y.ElementId, StringComparison.Ordinal);

        public int GetHashCode(VersionedElementKey obj) =>
            HashCode.Combine(obj.ProtocolVersionId, StringComparer.Ordinal.GetHashCode(obj.ElementId));
    }
}

internal sealed class AssertionOrderComparer : IComparer<Assertion>
{
    public static readonly AssertionOrderComparer Instance = new();

    public int Compare(Assertion? x, Assertion? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var cmp = x.AssertedAt.CompareTo(y.AssertedAt);
        return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
    }
}

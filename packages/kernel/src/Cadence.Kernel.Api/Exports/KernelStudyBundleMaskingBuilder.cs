using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

internal static class KernelStudyBundleMaskingBuilder
{
    public static KernelStudyLocksResponse BuildMaskedStudyLocks(ProjectedStudyExport export)
    {
        var enrollmentSiteMap = export.Snapshot.Enrollments.ToDictionary(e => e.EnrollmentId.Value, e => e.StudySiteId);
        var collectionEnrollmentMap = export.Snapshot.Collections.ToDictionary(c => c.CollectionInstanceId.Value, c => (c.EnrollmentId, c.StudySiteId));
        var obsEnrollmentMap = export.Snapshot.Observations.ToDictionary(o => o.ObservationId.Value, o => (o.EnrollmentId, o.StudySiteId, o.IsBlindedTreatment));

        var hiddenReasons = new HashSet<MaskReason>();
        var responses = new List<KernelLockResponse>(export.Snapshot.Locks.Count);
        foreach (var entry in export.Snapshot.Locks)
        {
            var reason = ResolveLockEntryMask(entry, export, enrollmentSiteMap, collectionEnrollmentMap, obsEnrollmentMap);
            if (reason is { } hiddenScopeReason && hiddenScopeReason != MaskReason.Blinded)
            {
                hiddenReasons.Add(hiddenScopeReason);
                continue;
            }

            responses.Add(reason is { } mask
                ? KernelLockResponse.FromMasked(entry, mask)
                : KernelLockResponse.FromVisible(entry));
        }

        return new KernelStudyLocksResponse(
            NodeRef.For(export.Snapshot.StudyId).ToString(),
            export.Snapshot.StudyCode,
            export.Snapshot.ValidAt,
            export.Snapshot.KnownAt,
            responses,
            KernelHiddenContentResponse.FromReasons(hiddenReasons));
    }

    public static (IReadOnlyList<BundleAuditEntryView> Entries, HashSet<MaskReason> HiddenReasons) BuildMaskedAuditTrail(ProjectedStudyExport export)
    {
        var hiddenReasons = new HashSet<MaskReason>();
        var snapshot = export.Snapshot;
        var enrollmentSiteMap = snapshot.Enrollments.ToDictionary(e => e.EnrollmentId.Value, e => e.StudySiteId);
        var collectionEnrollmentMap = snapshot.Collections.ToDictionary(c => c.CollectionInstanceId.Value, c => (c.EnrollmentId, c.StudySiteId));
        var obsEnrollmentMap = snapshot.Observations.ToDictionary(o => o.ObservationId.Value, o => (o.EnrollmentId, o.StudySiteId, o.IsBlindedTreatment));

        var maskByAggregateId = new Dictionary<string, MaskReason?>(StringComparer.Ordinal)
        {
            [snapshot.StudyId.ToString()] = null,
        };
        var maskByTargetGuid = new Dictionary<Guid, MaskReason?>
        {
            [snapshot.StudyId.Value] = null,
        };
        foreach (var protocol in snapshot.ProtocolVersions)
        {
            maskByTargetGuid[protocol.ProtocolVersionId.Value] = null;
            maskByAggregateId[protocol.ProtocolVersionId.ToString()] = null;
        }

        foreach (var site in snapshot.StudySites)
        {
            var siteMask = export.Visibility.Masks.For(NodeRef.For(site.StudySiteId));
            maskByTargetGuid[site.StudySiteId.Value] = siteMask;
            maskByAggregateId[site.StudySiteId.ToString()] = siteMask;
        }

        foreach (var enrollment in snapshot.Enrollments)
        {
            var eMask = export.Visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId))
                ?? export.Visibility.Masks.For(NodeRef.For(enrollment.StudySiteId));
            maskByTargetGuid[enrollment.EnrollmentId.Value] = eMask;
            maskByAggregateId[enrollment.EnrollmentId.ToString()] = eMask;
            maskByAggregateId[enrollment.ParticipantId.ToString()] = eMask;
        }

        var designs = snapshot.ProtocolVersions.Where(p => p.Design is not null).Select(p => p.Design!).ToList();
        var blindedObsIds = snapshot.Observations
            .Where(o => o.IsBlindedTreatment && !export.Visibility.UnblindedEnrollments.Contains(o.EnrollmentId))
            .Select(o => o.ObservationId)
            .ToHashSet();

        foreach (var coll in snapshot.Collections)
        {
            var cMask = export.Visibility.Masks.For(NodeRef.For(coll.EnrollmentId))
                ?? export.Visibility.Masks.For(NodeRef.For(coll.StudySiteId));
            if (cMask is null
                && !export.Visibility.UnblindedEnrollments.Contains(coll.EnrollmentId)
                && (BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(coll.CollectionSpecId, coll.CollectionSpecName, designs: designs)
                    || coll.ItemIdsByObservation.Keys.Any(blindedObsIds.Contains)))
            {
                cMask = MaskReason.Blinded;
            }

            maskByTargetGuid[coll.CollectionInstanceId.Value] = cMask;
            maskByAggregateId[coll.CollectionInstanceId.ToString()] = cMask;
        }

        foreach (var obs in snapshot.Observations)
        {
            var oMask = export.Visibility.Masks.For(NodeRef.For(obs.EnrollmentId))
                ?? export.Visibility.Masks.For(NodeRef.For(obs.StudySiteId))
                ?? export.Visibility.Masks.For(NodeRef.For(obs.ObservationId));
            if (oMask is null && obs.IsBlindedTreatment && !export.Visibility.UnblindedEnrollments.Contains(obs.EnrollmentId))
            {
                oMask = MaskReason.Blinded;
            }

            maskByTargetGuid[obs.ObservationId.Value] = oMask;
            maskByAggregateId[obs.ObservationId.ToString()] = oMask;
            foreach (var assertion in obs.Assertions)
            {
                maskByTargetGuid[assertion.AssertionId.Value] = oMask;
                maskByAggregateId[assertion.AssertionId.ToString()] = oMask;
            }
        }

        foreach (var lk in snapshot.Locks)
        {
            var lMask = ResolveLockEntryMask(lk, export, enrollmentSiteMap, collectionEnrollmentMap, obsEnrollmentMap);
            maskByTargetGuid[lk.LockId.Value] = lMask;
            maskByAggregateId[lk.LockId.ToString()] = lMask;
            if (lk.ReleaseAssertionId is { } relId)
            {
                maskByTargetGuid[relId.Value] = lMask;
                maskByAggregateId[relId.ToString()] = lMask;
            }
        }

        foreach (var att in snapshot.Attestations)
        {
            maskByTargetGuid.TryGetValue(att.TargetId, out var aMask);
            maskByAggregateId[att.AttestationId.ToString()] = aMask;
        }

        MaskReason? fallbackHiddenReason = null;
        foreach (var site in snapshot.StudySites)
        {
            var sm = export.Visibility.Masks.For(NodeRef.For(site.StudySiteId));
            if (sm is not null && sm != MaskReason.Blinded)
            {
                fallbackHiddenReason ??= sm;
            }
        }

        foreach (var enrollment in snapshot.Enrollments)
        {
            var em = export.Visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId));
            if (em is not null && em != MaskReason.Blinded)
            {
                fallbackHiddenReason ??= em;
            }
        }

        var hasUnrevealedBlindedObs = snapshot.Observations.Any(o =>
                export.VisibleEnrollmentIds.Contains(o.EnrollmentId)
                && o.IsBlindedTreatment
                && !export.Visibility.UnblindedEnrollments.Contains(o.EnrollmentId))
            || snapshot.Locks.Any(l =>
                l.BlindedScope
                && (l.EnrollmentId is null
                    || (export.VisibleEnrollmentIds.Contains(l.EnrollmentId.Value)
                        && !export.Visibility.UnblindedEnrollments.Contains(l.EnrollmentId.Value))));
        var blindedSequences = snapshot.BlindedAuditSequences;
        var aggregateIdsBySequence = snapshot.AuditAggregateIdsBySequence;
        var entries = new List<BundleAuditEntryView>(snapshot.AuditEntries.Count);

        foreach (var audit in snapshot.AuditEntries)
        {
            var isStudyLevel = string.Equals(audit.Subject, "Study", StringComparison.OrdinalIgnoreCase);
            MaskReason? entryMask = null;

            if (!isStudyLevel)
            {
                if (aggregateIdsBySequence is not null && aggregateIdsBySequence.TryGetValue(audit.Sequence, out var aggId))
                {
                    if (!maskByAggregateId.TryGetValue(aggId, out entryMask))
                    {
                        if (fallbackHiddenReason is { } unmappedHidden)
                        {
                            hiddenReasons.Add(unmappedHidden);
                        }

                        continue;
                    }

                    if (entryMask is null && hasUnrevealedBlindedObs && blindedSequences?.Contains(audit.Sequence) is true)
                    {
                        entryMask = MaskReason.Blinded;
                    }
                }
                else
                {
                    if (fallbackHiddenReason is { } hiddenFallback)
                    {
                        entryMask = hiddenFallback;
                    }
                    else if (hasUnrevealedBlindedObs && (blindedSequences is null || blindedSequences.Contains(audit.Sequence)))
                    {
                        entryMask = MaskReason.Blinded;
                    }
                }
            }

            if (entryMask is { } hiddenScopeReason && hiddenScopeReason != MaskReason.Blinded)
            {
                hiddenReasons.Add(hiddenScopeReason);
                continue;
            }

            if (entryMask == MaskReason.Blinded)
            {
                entries.Add(new BundleAuditEntryView(
                    Sequence: audit.Sequence,
                    EventKind: "masked",
                    ObservationRef: "masked",
                    Revision: null,
                    RecordedAt: audit.RecordedAt,
                    ActorId: null,
                    Summary: "masked",
                    Masked: true,
                    MaskReason: MaskReason.Blinded.ToString()));
                continue;
            }

            entries.Add(new BundleAuditEntryView(
                Sequence: audit.Sequence,
                EventKind: audit.Action,
                ObservationRef: audit.Subject,
                Revision: audit.Version is { } ver ? (int)ver : null,
                RecordedAt: audit.RecordedAt,
                ActorId: audit.ActorId,
                Summary: audit.ReasonForChange,
                Masked: false,
                MaskReason: null));
        }

        return (entries, hiddenReasons);
    }

    private static MaskReason? ResolveLockEntryMask(
        StudyLockEntry entry,
        ProjectedStudyExport export,
        IReadOnlyDictionary<Guid, StudySiteId> enrollmentSiteMap,
        IReadOnlyDictionary<Guid, (EnrollmentId EnrollmentId, StudySiteId StudySiteId)> collectionEnrollmentMap,
        IReadOnlyDictionary<Guid, (EnrollmentId EnrollmentId, StudySiteId StudySiteId, bool IsBlindedTreatment)> obsEnrollmentMap)
    {
        MaskReason? reason = entry.Scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
            ? export.Visibility.Masks.For(entry.Scope)
            : null;
        if (reason is null)
        {
            if (entry.Scope.Kind == NodeKinds.Enrollment && enrollmentSiteMap.TryGetValue(entry.Scope.Id, out var siteId))
            {
                reason = export.Visibility.Masks.For(NodeRef.For(siteId));
            }
            else if (entry.Scope.Kind == NodeKinds.CollectionInstance
                && collectionEnrollmentMap.TryGetValue(entry.Scope.Id, out var colScope))
            {
                reason = export.Visibility.Masks.For(NodeRef.For(colScope.EnrollmentId))
                    ?? export.Visibility.Masks.For(NodeRef.For(colScope.StudySiteId));
            }
            else if (entry.Scope.Kind == NodeKinds.Observation
                && obsEnrollmentMap.TryGetValue(entry.Scope.Id, out var obsScope))
            {
                reason = export.Visibility.Masks.For(NodeRef.For(obsScope.EnrollmentId))
                    ?? export.Visibility.Masks.For(NodeRef.For(obsScope.StudySiteId));
                if (reason is null && (entry.BlindedScope || obsScope.IsBlindedTreatment)
                    && !export.Visibility.UnblindedEnrollments.Contains(obsScope.EnrollmentId))
                {
                    reason = MaskReason.Blinded;
                }
            }
        }

        reason ??= (entry.EnrollmentId is { } eid ? export.Visibility.Masks.For(NodeRef.For(eid)) : null)
            ?? (entry.StudySiteId is { } sid ? export.Visibility.Masks.For(NodeRef.For(sid)) : null);

        if (reason is null
            && entry.BlindedScope
            && (entry.EnrollmentId is null || !export.Visibility.UnblindedEnrollments.Contains(entry.EnrollmentId.Value)))
        {
            reason = MaskReason.Blinded;
        }

        return reason;
    }
}

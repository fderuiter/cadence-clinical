using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Evidence;

internal static class LockReadinessFormAndScheduleBlockers
{
    internal static void CollectOpenQueryBlockers(
        List<LockReadinessBlocker> blockers,
        IReadOnlyList<Assertion> openQueries,
        IReadOnlySet<AssertionId> answeredQueryIds,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ObservationId, (CollectionInstance Instance, CollectionSpec? Spec, CollectionItem? Item)> membershipByObservation,
        IReadOnlySet<ObservationId> blindedObservationIds)
    {
        foreach (var query in openQueries.OrderBy(q => q.AssertedAt).ThenBy(q => q.Id))
        {
            var obsId = ObservationId.FromGuid(query.TargetId);
            observationsById.TryGetValue(obsId, out var obs);
            var enrollmentId = query.EnrollmentId ?? obs?.EnrollmentId;
            Enrollment? enrollment = enrollmentId is { } eid && enrollmentsById.TryGetValue(eid, out var foundEnrollment)
                ? foundEnrollment
                : null;
            membershipByObservation.TryGetValue(obsId, out var memberInfo);

            var answered = answeredQueryIds.Contains(query.Id);
            var owner = answered ? LockReadinessEvaluator.MonitorOwner : LockReadinessEvaluator.SiteOwner;
            var targetRef = NodeRef.For(obsId);
            var queryRef = NodeRef.For(query.Id);
            var collectionTitle = string.IsNullOrWhiteSpace(memberInfo.Spec?.Name) ? memberInfo.Instance?.CollectionSpecId : memberInfo.Spec!.Name;
            var itemLabel = !string.IsNullOrWhiteSpace(memberInfo.Item?.Prompt)
                ? memberInfo.Item!.Prompt
                : memberInfo.Item?.Id ?? obs?.Concept.ToString() ?? targetRef.ToString();
            var capability = answered ? "assertion.retract" : "observation.query.answer";
            var nextAction = answered
                ? $"{owner}: review the site response and close query {queryRef} on {itemLabel} ({targetRef})."
                : $"{owner}: answer open query {queryRef} on {itemLabel} ({targetRef}) or close it if resolved.";

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.OpenQuery,
                owner,
                targetRef,
                enrollment?.StudySiteId ?? memberInfo.Instance?.StudySiteId,
                enrollmentId,
                memberInfo.Instance?.Id,
                obsId,
                query.Id,
                enrollment?.SubjectCode.Value,
                memberInfo.Instance?.CollectionSpecId,
                collectionTitle,
                memberInfo.Item?.Id,
                itemLabel,
                query.Text,
                answered,
                blindedObservationIds.Contains(obsId),
                nextAction,
                capability,
                targetRef));
        }
    }

    internal static void CollectMissingRequiredItemBlockers(
        List<LockReadinessBlocker> blockers,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyDictionary<ProtocolVersionId, IReadOnlyList<CollectionSpec>> specsByVersion,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById)
    {
        foreach (var instance in instances.OrderBy(i => i.OpenedAt).ThenBy(i => i.Id))
        {
            var specs = specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            if (spec is null)
            {
                continue;
            }

            var recordedItems = instance.Members
                .Where(m => observationsById.TryGetValue(m.ObservationId, out var obs)
                    && obs.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                .Select(m => m.ItemId)
                .ToHashSet(StringComparer.Ordinal);

            enrollmentsById.TryGetValue(instance.EnrollmentId, out var enrollment);
            var targetRef = NodeRef.For(instance.Id);
            var collectionTitle = string.IsNullOrWhiteSpace(spec.Name) ? instance.CollectionSpecId : spec.Name;

            foreach (var item in spec.Items.Where(i => i.Required && !recordedItems.Contains(i.Id)))
            {
                var itemLabel = !string.IsNullOrWhiteSpace(item.Prompt) ? item.Prompt! : item.Id;
                var nextAction = $"{LockReadinessEvaluator.SiteOwner}: record a value or reasoned missing value for required item '{itemLabel}' ({item.Id}) on form '{collectionTitle}' ({targetRef}).";

                blockers.Add(new LockReadinessBlocker(
                    LockBlockerKind.MissingRequiredItem,
                    LockReadinessEvaluator.SiteOwner,
                    targetRef,
                    instance.StudySiteId,
                    instance.EnrollmentId,
                    instance.Id,
                    ObservationId: null,
                    QueryAssertionId: null,
                    enrollment?.SubjectCode.Value,
                    instance.CollectionSpecId,
                    collectionTitle,
                    item.Id,
                    itemLabel,
                    QueryText: null,
                    Answered: false,
                    Blinded: false,
                    nextAction,
                    NextCapability: "collection.record",
                    NextTarget: targetRef));
            }
        }
    }

    internal static void CollectUnverifiedValueBlockers(
        List<LockReadinessBlocker> blockers,
        NodeRef scope,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ObservationId, (CollectionInstance Instance, CollectionSpec? Spec, CollectionItem? Item)> membershipByObservation,
        IReadOnlySet<ObservationId> blindedObservationIds,
        IReadOnlySet<(ObservationId ObservationId, int Revision)>? verifiedRevisions)
    {
        var verifiedSet = verifiedRevisions ?? new HashSet<(ObservationId, int)>();
        var inScopeObservationIds = scope.Kind switch
        {
            NodeKinds.CollectionInstance => instances.SelectMany(i => i.Members.Select(m => m.ObservationId)).ToHashSet(),
            NodeKinds.Observation => new HashSet<ObservationId> { ObservationId.FromGuid(scope.Id) },
            _ => observationsById.Keys.ToHashSet(),
        };

        foreach (var obs in observationsById.Values
            .Where(o => inScopeObservationIds.Contains(o.Id) && o.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .OrderBy(o => o.ObservedAt)
            .ThenBy(o => o.Id))
        {
            if (verifiedSet.Contains((obs.Id, obs.Revision)))
            {
                continue;
            }

            enrollmentsById.TryGetValue(obs.EnrollmentId, out var enrollment);
            membershipByObservation.TryGetValue(obs.Id, out var memberInfo);
            var targetRef = NodeRef.For(obs.Id);
            var collectionTitle = string.IsNullOrWhiteSpace(memberInfo.Spec?.Name) ? memberInfo.Instance?.CollectionSpecId : memberInfo.Spec!.Name;
            var itemLabel = !string.IsNullOrWhiteSpace(memberInfo.Item?.Prompt)
                ? memberInfo.Item!.Prompt
                : memberInfo.Item?.Id ?? obs.Concept.ToString();
            var nextAction = $"{LockReadinessEvaluator.MonitorOwner}: complete source data verification for revision {obs.Revision} of '{itemLabel}' ({targetRef}).";

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.UnverifiedValue,
                LockReadinessEvaluator.MonitorOwner,
                targetRef,
                enrollment?.StudySiteId ?? memberInfo.Instance?.StudySiteId,
                obs.EnrollmentId,
                memberInfo.Instance?.Id,
                obs.Id,
                QueryAssertionId: null,
                enrollment?.SubjectCode.Value,
                memberInfo.Instance?.CollectionSpecId,
                collectionTitle,
                memberInfo.Item?.Id,
                itemLabel,
                QueryText: null,
                Answered: false,
                Blinded: blindedObservationIds.Contains(obs.Id),
                nextAction,
                NextCapability: "observation.verify",
                NextTarget: targetRef,
                CurrentRevision: obs.Revision));
        }
    }

    internal static void CollectUnsignedFormBlockers(
        List<LockReadinessBlocker> blockers,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyDictionary<ProtocolVersionId, IReadOnlyList<CollectionSpec>> specsByVersion,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<CollectionInstanceId, IReadOnlyList<Attestation>>? formSignaturesByInstance)
    {
        var signaturesMap = formSignaturesByInstance ?? new Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>>();
        foreach (var instance in instances.OrderBy(i => i.OpenedAt).ThenBy(i => i.Id))
        {
            var memberObservations = instance.Members
                .Select(m => observationsById.GetValueOrDefault(m.ObservationId))
                .Where(o => o is not null)
                .Select(o => o!)
                .ToList();
            if (!memberObservations.Any(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked }))
            {
                continue;
            }

            var currentHash = CollectionInstance.ContentHashOf(
                memberObservations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)));
            var signatures = signaturesMap.GetValueOrDefault(instance.Id) ?? [];
            var hasCurrentSignature = signatures.Any(a =>
                a.Meaning is AttestationMeaning.Approved or AttestationMeaning.Responsibility
                && string.Equals(a.TargetContentHash, currentHash, StringComparison.Ordinal));
            if (hasCurrentSignature)
            {
                continue;
            }

            var superseded = signatures.Count > 0;
            var specs = specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? instance.CollectionSpecId : spec!.Name;
            enrollmentsById.TryGetValue(instance.EnrollmentId, out var enrollment);
            var targetRef = NodeRef.For(instance.Id);
            var nextAction = superseded
                ? $"{LockReadinessEvaluator.InvestigatorOwner}: re-sign form '{collectionTitle}' ({targetRef}) because a subsequent data change superseded its prior signature."
                : $"{LockReadinessEvaluator.InvestigatorOwner}: sign populated form '{collectionTitle}' ({targetRef}) before locking.";

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.UnsignedForm,
                LockReadinessEvaluator.InvestigatorOwner,
                targetRef,
                instance.StudySiteId,
                instance.EnrollmentId,
                instance.Id,
                ObservationId: null,
                QueryAssertionId: null,
                enrollment?.SubjectCode.Value,
                instance.CollectionSpecId,
                collectionTitle,
                ItemId: null,
                ItemLabel: superseded ? "Signature superseded" : "Awaiting investigator signature",
                QueryText: null,
                Answered: false,
                Blinded: false,
                nextAction,
                NextCapability: "collection.sign",
                NextTarget: targetRef,
                SignatureSuperseded: superseded));
        }
    }

    internal static void CollectUnopenedVisitBlockers(
        List<LockReadinessBlocker> blockers,
        NodeRef scope,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign?> designsByVersion,
        DateTimeOffset? evaluatedAt,
        IReadOnlyDictionary<StudySiteId, StudySite>? studySitesById,
        IReadOnlyDictionary<SiteId, Site>? sitesById,
        IReadOnlyList<Effectivity>? studyEffectivities)
    {
        if (evaluatedAt is not { } evalInstant
            || scope.Kind is not (NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment)
            || studySitesById is null
            || sitesById is null
            || studyEffectivities is null)
        {
            return;
        }

        var instancesByEnrollment = instances.ToLookup(i => i.EnrollmentId);

        foreach (var enrollment in enrollmentsById.Values
            .OrderBy(e => e.SubjectCode.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Id))
        {
            if (enrollment.Status == EnrollmentStatus.ScreenFailed)
            {
                continue;
            }

            var enrolledPeriod = enrollment.Periods
                .Where(p => p.Status == EnrollmentStatus.Enrolled && p.Period.Recorded.Contains(evalInstant))
                .OrderBy(p => p.Period.Valid.Start)
                .FirstOrDefault();
            if (enrolledPeriod is null)
            {
                continue;
            }

            var referenceStart = DateOnly.FromDateTime(enrolledPeriod.Period.Valid.Start.UtcDateTime);
            DateTimeOffset scheduleInstant;
            DateOnly? participationEndedDate = null;
            if (enrollment.Status == EnrollmentStatus.Withdrawn)
            {
                var endedAt = enrollment.StatusSince;
                participationEndedDate = DateOnly.FromDateTime(endedAt.UtcDateTime);
                scheduleInstant = endedAt < evalInstant ? endedAt : evalInstant;
            }
            else
            {
                scheduleInstant = evalInstant;
            }

            if (!studySitesById.TryGetValue(enrollment.StudySiteId, out var studySite)
                || !sitesById.TryGetValue(studySite.SiteId, out var site))
            {
                continue;
            }

            var effectiveVersionIds = EffectivityResolution.EffectiveAt(
                    studyEffectivities,
                    studySite.Id,
                    site.Country,
                    scheduleInstant,
                    evalInstant)
                .Select(e => e.ProtocolVersionId)
                .ToHashSet();
            var governingVersion = versionsById.Values
                .Where(v => effectiveVersionIds.Contains(v.Id))
                .MaxBy(v => v.Number);
            if (governingVersion is null
                || !designsByVersion.TryGetValue(governingVersion.Id, out var design)
                || design is null)
            {
                continue;
            }

            var asOfDate = DateOnly.FromDateTime(scheduleInstant.UtcDateTime);
            var expectedVisits = ExpectedSchedule.Compute(design, referenceStart, asOfDate);
            var activitiesById = design.PlannedActivities.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var specsById = design.CollectionSpecs.ToDictionary(s => s.Id, StringComparer.Ordinal);
            var openedVisitSpecs = instancesByEnrollment[enrollment.Id]
                .Where(i => i.PlannedActivityId is not null)
                .Select(i => (ActivityId: i.PlannedActivityId!, SpecId: i.CollectionSpecId))
                .ToHashSet();

            foreach (var expected in expectedVisits)
            {
                if (expected.Status is not (VisitWindowStatus.Open or VisitWindowStatus.Closed))
                {
                    continue;
                }

                if (participationEndedDate is { } endedDate && expected.Target > endedDate)
                {
                    continue;
                }

                if (!activitiesById.TryGetValue(expected.Visit.ActivityId, out var activity))
                {
                    continue;
                }

                var targetRef = NodeRef.For(enrollment.Id);
                foreach (var specId in activity.CollectionSpecIds)
                {
                    if (openedVisitSpecs.Contains((activity.Id, specId)))
                    {
                        continue;
                    }

                    var specTitle = specsById.TryGetValue(specId, out var spec) && !string.IsNullOrWhiteSpace(spec.Name)
                        ? spec.Name
                        : specId;
                    var windowStateLabel = expected.Status == VisitWindowStatus.Closed ? "overdue" : "due";
                    var nextAction = $"{LockReadinessEvaluator.SiteOwner}: open scheduled form '{specTitle}' ({specId}) for {windowStateLabel} visit '{activity.Name}' (target {expected.Target:yyyy-MM-dd}) on {targetRef}.";

                    blockers.Add(new LockReadinessBlocker(
                        LockBlockerKind.UnopenedVisit,
                        LockReadinessEvaluator.SiteOwner,
                        targetRef,
                        enrollment.StudySiteId,
                        enrollment.Id,
                        CollectionInstanceId: null,
                        ObservationId: null,
                        QueryAssertionId: null,
                        enrollment.SubjectCode.Value,
                        specId,
                        specTitle,
                        ItemId: activity.Id,
                        ItemLabel: $"{activity.Name} — {specTitle}",
                        QueryText: null,
                        Answered: false,
                        Blinded: false,
                        nextAction,
                        NextCapability: "collection.open",
                        NextTarget: targetRef,
                        PlannedActivityId: activity.Id,
                        PlannedActivityName: activity.Name,
                        PlannedDate: expected.Target,
                        WindowEnd: expected.WindowEnd));
                }
            }
        }
    }
}

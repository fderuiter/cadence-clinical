using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Evidence;

internal static class LockReadinessFindingAndUnitReaders
{
    internal static void CollectErrorFindingBlockers(
        List<LockReadinessBlocker> blockers,
        NodeRef scope,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyList<CollectionInstance>? membershipInstances,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ObservationId, (CollectionInstance Instance, CollectionSpec? Spec, CollectionItem? Item)> membershipByObservation,
        IReadOnlyDictionary<ProtocolVersionId, IReadOnlyList<CollectionSpec>> specsByVersion,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlySet<ObservationId> blindedObservationIds,
        IReadOnlyList<ValidationFindingView>? activeErrorFindings)
    {
        if (activeErrorFindings is not { Count: > 0 })
        {
            return;
        }

        var instancesById = instances.ToDictionary(i => i.Id);
        var instancesByEnrollment = (membershipInstances ?? instances)
            .Concat(instances)
            .DistinctBy(i => i.Id)
            .GroupBy(i => i.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstance>)[.. g]);
        foreach (var finding in activeErrorFindings
            .OrderBy(f => f.Stage)
            .ThenBy(f => f.PackKey, StringComparer.Ordinal)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Id))
        {
            if (!TryMatchFindingToScope(
                    scope,
                    finding,
                    instancesById,
                    observationsById,
                    enrollmentsById,
                    membershipByObservation,
                    specsByVersion,
                    out var targetRef,
                    out var siteId,
                    out var enrollmentId,
                    out var collectionInstanceId,
                    out var obsId,
                    out var subjectCode,
                    out var collectionSpecId,
                    out var collectionTitle,
                    out var itemId,
                    out var itemLabel))
            {
                continue;
            }

            var findingRef = NodeRef.For(finding.Id);
            var ruleLabel = $"{finding.PackKey}/{finding.RuleId}";
            var nextAction = $"{LockReadinessEvaluator.MonitorOwner}: resolve, accept, or explain Error validation finding {findingRef} ({ruleLabel}: {finding.Message}) on {targetRef}.";
            var isBlindedFinding = (obsId is { } matchedObsId && blindedObservationIds.Contains(matchedObsId))
                || BlindedTreatmentClassifier.Instance.IsBlindedFinding(finding.Target, finding.Observed, designs)
                || ReferencesBlindedObservation(finding, collectionInstanceId, enrollmentId, instancesById, instancesByEnrollment, blindedObservationIds);

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.ErrorFinding,
                LockReadinessEvaluator.MonitorOwner,
                targetRef,
                siteId,
                enrollmentId,
                collectionInstanceId,
                obsId,
                QueryAssertionId: null,
                subjectCode,
                collectionSpecId,
                collectionTitle,
                itemId,
                itemLabel,
                QueryText: null,
                Answered: false,
                Blinded: isBlindedFinding,
                nextAction,
                NextCapability: "validation_finding.investigate",
                NextTarget: findingRef,
                FindingId: finding.Id,
                RuleId: ruleLabel,
                FindingMessage: finding.Message));
        }
    }

    internal static async Task<IReadOnlyList<ValidationFindingView>> ReadActiveErrorFindingsAsync(
        TenantId tenant,
        StudyId study,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        if (findings is null || tenant.IsEmpty)
        {
            return [];
        }

        var page = await findings.ListAsync(
            tenant,
            new FindingQuery(study)
            {
                Severity = ValidationSeverity.Error,
                Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                Take = FindingQuery.MaxTake,
            },
            cancellationToken).ConfigureAwait(false);
        return page.Findings;
    }

    internal static async Task<(IReadOnlyList<Assertion> Queries, IReadOnlySet<AssertionId> AnsweredQueryIds)> ReadOpenQueriesAsync(
        IKernelUnitOfWork work,
        StudyId study,
        IReadOnlyList<Observation> coveredObservations,
        CancellationToken cancellationToken)
    {
        if (coveredObservations.Count == 0)
        {
            return ([], new HashSet<AssertionId>());
        }

        var targetGuids = coveredObservations.Select(o => o.Id.Value).ToList();
        var candidateQueries = await work.ReadAllAsync<Assertion>(
            a => a.StudyId == study
                && a.Kind == AssertionKind.Queried
                && a.RetractsId == null
                && a.TargetKind == NodeKinds.Observation
                && targetGuids.Contains(a.TargetId),
            cancellationToken).ConfigureAwait(false);
        if (candidateQueries.Count == 0)
        {
            return ([], new HashSet<AssertionId>());
        }

        var queryIds = candidateQueries.Select(q => q.Id).ToList();
        var retractedQueryIds = await LocksInForce.RetractedAsync(work, queryIds, cancellationToken).ConfigureAwait(false);
        var openQueries = candidateQueries.Where(q => !retractedQueryIds.Contains(q.Id)).ToList();
        if (openQueries.Count == 0)
        {
            return ([], new HashSet<AssertionId>());
        }

        var openQueryGuids = openQueries.Select(q => q.Id.Value).ToList();
        var answers = await work.ReadAllAsync<Assertion>(
            a => a.StudyId == study
                && a.Kind == AssertionKind.QueryAnswered
                && a.RetractsId == null
                && a.TargetKind == NodeKinds.Assertion
                && openQueryGuids.Contains(a.TargetId),
            cancellationToken).ConfigureAwait(false);
        if (answers.Count == 0)
        {
            return (openQueries, new HashSet<AssertionId>());
        }

        var retractedAnswerIds = await LocksInForce.RetractedAsync(work, answers.Select(a => a.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var answeredIds = answers
            .Where(a => !retractedAnswerIds.Contains(a.Id))
            .Select(a => AssertionId.FromGuid(a.TargetId))
            .ToHashSet();
        return (openQueries, answeredIds);
    }

    internal static async Task<IReadOnlySet<(ObservationId ObservationId, int Revision)>> ReadVerifiedRevisionsAsync(
        IKernelUnitOfWork work,
        StudyId study,
        IReadOnlyList<Observation> coveredObservations,
        CancellationToken cancellationToken)
    {
        var recordedObservations = coveredObservations
            .Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .ToList();
        if (recordedObservations.Count == 0)
        {
            return new HashSet<(ObservationId, int)>();
        }

        var targetGuids = recordedObservations.Select(o => o.Id.Value).ToList();
        var candidateVerifications = await work.ReadAllAsync<Assertion>(
            a => a.StudyId == study
                && a.Kind == AssertionKind.Verified
                && a.RetractsId == null
                && a.TargetKind == NodeKinds.Observation
                && targetGuids.Contains(a.TargetId),
            cancellationToken).ConfigureAwait(false);
        if (candidateVerifications.Count == 0)
        {
            return new HashSet<(ObservationId, int)>();
        }

        var verificationIds = candidateVerifications.Select(v => v.Id).ToList();
        var retractedIds = await LocksInForce.RetractedAsync(work, verificationIds, cancellationToken).ConfigureAwait(false);
        return candidateVerifications
            .Where(v => !retractedIds.Contains(v.Id) && v.TargetRevision is not null)
            .Select(v => (ObservationId.FromGuid(v.TargetId), v.TargetRevision!.Value))
            .ToHashSet();
    }

    internal static async Task<IReadOnlyDictionary<CollectionInstanceId, IReadOnlyList<Attestation>>> ReadFormSignaturesAsync(
        IKernelUnitOfWork work,
        IReadOnlyList<CollectionInstance> instances,
        CancellationToken cancellationToken)
    {
        if (instances.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>>();
        }

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var attestations = await work.ReadAllAsync<Attestation>(
            a => a.TargetKind == NodeKinds.CollectionInstance
                && instanceGuids.Contains(a.TargetId)
                && (a.Meaning == AttestationMeaning.Approved || a.Meaning == AttestationMeaning.Responsibility),
            cancellationToken).ConfigureAwait(false);
        return attestations
            .GroupBy(a => CollectionInstanceId.FromGuid(a.TargetId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Attestation>)g.ToList());
    }

    private static bool TryMatchFindingToScope(
        NodeRef scope,
        ValidationFindingView finding,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstance> instancesById,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ObservationId, (CollectionInstance Instance, CollectionSpec? Spec, CollectionItem? Item)> membershipByObservation,
        IReadOnlyDictionary<ProtocolVersionId, IReadOnlyList<CollectionSpec>> specsByVersion,
        out NodeRef targetRef,
        out StudySiteId? siteId,
        out EnrollmentId? enrollmentId,
        out CollectionInstanceId? collectionInstanceId,
        out ObservationId? obsId,
        out string? subjectCode,
        out string? collectionSpecId,
        out string? collectionTitle,
        out string? itemId,
        out string? itemLabel)
    {
        targetRef = scope;
        siteId = null;
        enrollmentId = null;
        collectionInstanceId = null;
        obsId = null;
        subjectCode = null;
        collectionSpecId = null;
        collectionTitle = null;
        itemId = null;
        itemLabel = null;

        if (finding.Target.Kind == FindingTargetKind.Observation
            && finding.Target.Node is { Kind: NodeKinds.Observation } obsNode
            && observationsById.TryGetValue(ObservationId.FromGuid(obsNode.Id), out var matchedObs))
        {
            obsId = matchedObs.Id;
            targetRef = NodeRef.For(matchedObs.Id);
            enrollmentId = matchedObs.EnrollmentId;
            enrollmentsById.TryGetValue(matchedObs.EnrollmentId, out var enrollment);
            membershipByObservation.TryGetValue(matchedObs.Id, out var memberInfo);
            siteId = enrollment?.StudySiteId ?? memberInfo.Instance?.StudySiteId;
            collectionInstanceId = memberInfo.Instance?.Id;
            subjectCode = enrollment?.SubjectCode.Value;
            collectionSpecId = memberInfo.Instance?.CollectionSpecId;
            collectionTitle = string.IsNullOrWhiteSpace(memberInfo.Spec?.Name) ? memberInfo.Instance?.CollectionSpecId : memberInfo.Spec!.Name;
            itemId = memberInfo.Item?.Id ?? finding.Target.Variable;
            itemLabel = !string.IsNullOrWhiteSpace(memberInfo.Item?.Prompt)
                ? memberInfo.Item!.Prompt
                : itemId ?? matchedObs.Concept.ToString();
            return true;
        }

        if (finding.Target.Kind == FindingTargetKind.DatasetRecord
            && Guid.TryParse(finding.Target.Key, out var recordGuid))
        {
            if (instancesById.TryGetValue(CollectionInstanceId.FromGuid(recordGuid), out var matchedInstance))
            {
                collectionInstanceId = matchedInstance.Id;
                targetRef = NodeRef.For(matchedInstance.Id);
                siteId = matchedInstance.StudySiteId;
                enrollmentId = matchedInstance.EnrollmentId;
                enrollmentsById.TryGetValue(matchedInstance.EnrollmentId, out var enrollment);
                subjectCode = enrollment?.SubjectCode.Value;
                collectionSpecId = matchedInstance.CollectionSpecId;
                var specs = specsByVersion.GetValueOrDefault(matchedInstance.ProtocolVersionId) ?? [];
                var spec = specs.FirstOrDefault(s => string.Equals(s.Id, matchedInstance.CollectionSpecId, StringComparison.Ordinal));
                collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? matchedInstance.CollectionSpecId : spec!.Name;
                var item = finding.Target.Variable is null
                    ? null
                    : spec?.Items.FirstOrDefault(i => string.Equals(i.Id, finding.Target.Variable, StringComparison.Ordinal));
                itemId = item?.Id ?? finding.Target.Variable;
                itemLabel = !string.IsNullOrWhiteSpace(item?.Prompt) ? item!.Prompt : itemId;
                if (finding.Target.Variable is not null)
                {
                    var member = matchedInstance.Members.FirstOrDefault(m => string.Equals(m.ItemId, finding.Target.Variable, StringComparison.Ordinal));
                    if (member is not null)
                    {
                        obsId = member.ObservationId;
                    }
                }

                return true;
            }

            if (scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
                && enrollmentsById.TryGetValue(EnrollmentId.FromGuid(recordGuid), out var matchedEnrollment))
            {
                enrollmentId = matchedEnrollment.Id;
                siteId = matchedEnrollment.StudySiteId;
                subjectCode = matchedEnrollment.SubjectCode.Value;
                targetRef = NodeRef.For(matchedEnrollment.Id);
                itemId = finding.Target.Variable;
                itemLabel = finding.Target.Variable ?? finding.Target.Dataset;
                return true;
            }

            if (scope.Kind == NodeKinds.Observation
                && membershipByObservation.TryGetValue(ObservationId.FromGuid(scope.Id), out var singleMember)
                && singleMember.Instance is not null
                && singleMember.Instance.Id.Value == recordGuid
                && (finding.Target.Variable is null || string.Equals(finding.Target.Variable, singleMember.Item?.Id, StringComparison.Ordinal)))
            {
                obsId = ObservationId.FromGuid(scope.Id);
                targetRef = scope;
                collectionInstanceId = singleMember.Instance.Id;
                siteId = singleMember.Instance.StudySiteId;
                enrollmentId = singleMember.Instance.EnrollmentId;
                enrollmentsById.TryGetValue(singleMember.Instance.EnrollmentId, out var enrollment);
                subjectCode = enrollment?.SubjectCode.Value;
                collectionSpecId = singleMember.Instance.CollectionSpecId;
                collectionTitle = string.IsNullOrWhiteSpace(singleMember.Spec?.Name) ? singleMember.Instance.CollectionSpecId : singleMember.Spec!.Name;
                itemId = singleMember.Item?.Id ?? finding.Target.Variable;
                itemLabel = !string.IsNullOrWhiteSpace(singleMember.Item?.Prompt) ? singleMember.Item!.Prompt : itemId;
                return true;
            }
        }

        if (scope.Kind == NodeKinds.Study)
        {
            targetRef = scope;
            itemId = finding.Target.Variable;
            itemLabel = finding.Target.Variable is not null
                ? $"{finding.Target.Dataset}.{finding.Target.Variable}"
                : $"{finding.Target.Dataset} ({finding.Target.Key})";
            return true;
        }

        return false;
    }

    private static bool ReferencesBlindedObservation(
        ValidationFindingView finding,
        CollectionInstanceId? collectionInstanceId,
        EnrollmentId? enrollmentId,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstance> instancesById,
        IReadOnlyDictionary<EnrollmentId, IReadOnlyList<CollectionInstance>> instancesByEnrollment,
        IReadOnlySet<ObservationId> blindedObservationIds)
    {
        if (blindedObservationIds.Count == 0)
        {
            return false;
        }

        CollectionInstance? targetInstance = null;
        if (collectionInstanceId is { } cid)
        {
            instancesById.TryGetValue(cid, out targetInstance);
        }

        IReadOnlyList<CollectionInstance> siblingForms = [];
        if (enrollmentId is { } eid && instancesByEnrollment.TryGetValue(eid, out var forms))
        {
            siblingForms = forms;
        }
        else if (targetInstance is not null)
        {
            siblingForms = [targetInstance];
        }

        if (siblingForms.Count == 0)
        {
            return false;
        }

        var hasVariableReference = false;
        if (!string.IsNullOrWhiteSpace(finding.Target.Variable))
        {
            hasVariableReference = true;
            if (VariableMatchesBlindedObservation(finding.Target.Variable, targetInstance, siblingForms, blindedObservationIds))
            {
                return true;
            }
        }

        foreach (var observed in finding.Observed)
        {
            if (string.IsNullOrWhiteSpace(observed.Variable))
            {
                continue;
            }

            hasVariableReference = true;
            if (VariableMatchesBlindedObservation(observed.Variable, targetInstance, siblingForms, blindedObservationIds))
            {
                return true;
            }
        }

        return !hasVariableReference
            && targetInstance is not null
            && targetInstance.Members.Any(m => blindedObservationIds.Contains(m.ObservationId));
    }

    private static bool VariableMatchesBlindedObservation(
        string variable,
        CollectionInstance? targetInstance,
        IReadOnlyList<CollectionInstance> siblingForms,
        IReadOnlySet<ObservationId> blindedObservationIds)
    {
        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            return siblingForms.Any(form =>
                string.Equals(form.CollectionSpecId, specId, StringComparison.Ordinal)
                && form.Members.Any(m =>
                    string.Equals(m.ItemId, itemId, StringComparison.Ordinal)
                    && blindedObservationIds.Contains(m.ObservationId)));
        }

        if (targetInstance is not null)
        {
            return targetInstance.Members.Any(m =>
                string.Equals(m.ItemId, variable, StringComparison.Ordinal)
                && blindedObservationIds.Contains(m.ObservationId));
        }

        return siblingForms.Any(form =>
            form.Members.Any(m =>
                string.Equals(m.ItemId, variable, StringComparison.Ordinal)
                && blindedObservationIds.Contains(m.ObservationId)));
    }
}

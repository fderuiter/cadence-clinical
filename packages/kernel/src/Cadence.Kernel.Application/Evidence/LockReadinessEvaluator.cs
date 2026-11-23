using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Evaluates whether a target lock scope is ready for a <see cref="LockKind.Locked"/> assertion (#5466, spec §10.5):
/// every <see cref="AssertionKind.Queried"/> assertion in the scope must be retracted (closed), every required
/// <see cref="CollectionItem"/> on opened <see cref="CollectionInstance"/>s in the scope must hold a recorded value
/// (a reasoned <see cref="MissingValue"/> other than <see cref="NullFlavor.NotAsked"/> included), and every active
/// <see cref="ValidationSeverity.Error"/> validation finding in the scope must be resolved, accepted, or explained.
/// <see cref="LockKind.Frozen"/> commits skip this gate so sites can still answer and close queries before hard lock.
/// </summary>
public static class LockReadinessEvaluator
{
    public const string Producer = "kernel.lock_readiness";
    public const string QueriesClosedCondition = "lock.queries_closed";
    public const string RequiredItemsCompleteCondition = "lock.required_items_complete";
    public const string ErrorFindingsResolvedCondition = "lock.error_findings_resolved";
    public const string OpenQueryBlockerCondition = "lock.open_query";
    public const string MissingRequiredItemBlockerCondition = "lock.missing_required_item";
    public const string ErrorFindingBlockerCondition = "lock.error_finding";

    public const string SiteOwner = "Site coordinator / investigator";
    public const string MonitorOwner = "Clinical research associate / data manager";

    /// <summary>
    /// Evaluates lock readiness of <paramref name="scope"/> inside <paramref name="study"/> over <paramref name="work"/>
    /// and returns <see cref="AssertionErrors.ScopeNotReady"/> with its structured <see cref="Explanation"/> when any
    /// open query, missing required item, or unresolved Error validation finding blocks <see cref="LockKind.Locked"/>;
    /// returns null when ready.
    /// </summary>
    internal static Task<KernelError?> CheckAsync(
        IKernelUnitOfWork work,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        CheckAsync(TenantId.Empty, work, study, scope, findings: null, cancellationToken);

    internal static async Task<KernelError?> CheckAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        var snapshot = await EvaluateAsync(tenant, work, study, studyCode: string.Empty, scope, findings, cancellationToken).ConfigureAwait(false);
        return snapshot.ReadyForLock
            ? null
            : AssertionErrors.ScopeNotReady.WithMessage(snapshot.Explanation.Claim) with { Explanation = snapshot.Explanation };
    }

    internal static Task<LockReadinessSnapshot> EvaluateAsync(
        IKernelUnitOfWork work,
        StudyId study,
        string studyCode,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        EvaluateAsync(TenantId.Empty, work, study, studyCode, scope, findings: null, cancellationToken);

    internal static async Task<LockReadinessSnapshot> EvaluateAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        string studyCode,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CollectionInstance> instances;
        IReadOnlyList<Observation> coveredObservations;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                instances = await work.ReadAllAsync<CollectionInstance>(c => c.StudyId == study, cancellationToken).ConfigureAwait(false);
                coveredObservations = await work.ReadAllAsync<Observation>(o => o.StudyId == study, cancellationToken).ConfigureAwait(false);
                break;

            case NodeKinds.StudySite:
                var siteId = StudySiteId.FromGuid(scope.Id);
                var siteEnrollments = await work.ReadAllAsync<Enrollment>(
                    e => e.StudyId == study && e.StudySiteId == siteId,
                    cancellationToken).ConfigureAwait(false);
                var siteEnrollmentIds = siteEnrollments.Select(e => e.Id).ToList();
                instances = await work.ReadAllAsync<CollectionInstance>(
                    c => c.StudyId == study && c.StudySiteId == siteId,
                    cancellationToken).ConfigureAwait(false);
                coveredObservations = siteEnrollmentIds.Count == 0
                    ? []
                    : await work.ReadAllAsync<Observation>(
                        o => o.StudyId == study && siteEnrollmentIds.Contains(o.EnrollmentId),
                        cancellationToken).ConfigureAwait(false);
                break;

            case NodeKinds.Enrollment:
                var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                instances = await work.ReadAllAsync<CollectionInstance>(
                    c => c.StudyId == study && c.EnrollmentId == enrollmentId,
                    cancellationToken).ConfigureAwait(false);
                coveredObservations = await work.ReadAllAsync<Observation>(
                    o => o.StudyId == study && o.EnrollmentId == enrollmentId,
                    cancellationToken).ConfigureAwait(false);
                break;

            case NodeKinds.CollectionInstance:
                var instanceId = CollectionInstanceId.FromGuid(scope.Id);
                var singleInstance = await work.ReadAsync<CollectionInstance>(instanceId, cancellationToken).ConfigureAwait(false);
                instances = singleInstance is not null && singleInstance.StudyId == study ? [singleInstance] : [];
                var memberObsIds = instances.SelectMany(i => i.Members.Select(m => m.ObservationId)).Distinct().ToList();
                coveredObservations = memberObsIds.Count == 0
                    ? []
                    : await work.ReadAllAsync<Observation>(
                        o => o.StudyId == study && memberObsIds.Contains(o.Id),
                        cancellationToken).ConfigureAwait(false);
                break;

            default:
                instances = [];
                var observationId = ObservationId.FromGuid(scope.Id);
                var singleObs = await work.ReadAsync<Observation>(observationId, cancellationToken).ConfigureAwait(false);
                coveredObservations = singleObs is not null && singleObs.StudyId == study ? [singleObs] : [];
                break;
        }

        var enrollmentIds = instances.Select(i => i.EnrollmentId)
            .Concat(coveredObservations.Select(o => o.EnrollmentId))
            .Distinct()
            .ToList();
        var enrollmentsById = enrollmentIds.Count == 0
            ? new Dictionary<EnrollmentId, Enrollment>()
            : (await work.ReadAllAsync<Enrollment>(e => enrollmentIds.Contains(e.Id), cancellationToken).ConfigureAwait(false))
                .ToDictionary(e => e.Id);

        var versionIds = instances.Select(i => i.ProtocolVersionId).Distinct().ToList();
        var versionsById = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, ProtocolVersion>()
            : (await work.ReadAllAsync<ProtocolVersion>(v => versionIds.Contains(v.Id), cancellationToken).ConfigureAwait(false))
                .ToDictionary(v => v.Id);

        var obsById = coveredObservations.ToDictionary(o => o.Id);
        var missingMemberObsIds = instances
            .SelectMany(i => i.Members.Select(m => m.ObservationId))
            .Where(id => !obsById.ContainsKey(id))
            .Distinct()
            .ToList();
        if (missingMemberObsIds.Count > 0)
        {
            var extraObs = await work.ReadAllAsync<Observation>(o => missingMemberObsIds.Contains(o.Id), cancellationToken).ConfigureAwait(false);
            foreach (var extra in extraObs)
            {
                obsById[extra.Id] = extra;
            }
        }

        var openQueries = await ReadOpenQueriesAsync(work, study, coveredObservations, cancellationToken).ConfigureAwait(false);
        var activeErrorFindings = await ReadActiveErrorFindingsAsync(tenant, study, findings, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CollectionInstance>? membershipInstances = null;
        if (scope.Kind == NodeKinds.Observation && (openQueries.Queries.Count > 0 || activeErrorFindings.Count > 0))
        {
            var targetObsId = ObservationId.FromGuid(scope.Id);
            var containing = await work.ReadAllAsync<CollectionInstance>(
                c => c.StudyId == study && c.Members.Any(m => m.ObservationId == targetObsId),
                cancellationToken).ConfigureAwait(false);
            if (containing.Count > 0)
            {
                membershipInstances = containing;
                var extraVersionIds = containing.Select(c => c.ProtocolVersionId).Where(id => !versionsById.ContainsKey(id)).Distinct().ToList();
                if (extraVersionIds.Count > 0)
                {
                    var extraVersions = await work.ReadAllAsync<ProtocolVersion>(v => extraVersionIds.Contains(v.Id), cancellationToken).ConfigureAwait(false);
                    foreach (var version in extraVersions)
                    {
                        versionsById[version.Id] = version;
                    }
                }
            }
        }

        return BuildSnapshot(
            study,
            studyCode,
            scope,
            instances,
            obsById,
            enrollmentsById,
            versionsById,
            openQueries.Queries,
            openQueries.AnsweredQueryIds,
            blindedObservationIds: new HashSet<ObservationId>(),
            membershipInstances,
            activeErrorFindings);
    }

    /// <summary>
    /// Builds a deterministic <see cref="LockReadinessSnapshot"/> and <see cref="Explanation"/> from loaded domain objects.
    /// Shared by command handling (<see cref="IKernelUnitOfWork"/>) and read endpoints (<c>KernelLockRecords</c>).
    /// </summary>
    public static LockReadinessSnapshot BuildSnapshot(
        StudyId study,
        string studyCode,
        NodeRef scope,
        IReadOnlyList<CollectionInstance> instances,
        IReadOnlyDictionary<ObservationId, Observation> observationsById,
        IReadOnlyDictionary<EnrollmentId, Enrollment> enrollmentsById,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById,
        IReadOnlyList<Assertion> openQueries,
        IReadOnlySet<AssertionId> answeredQueryIds,
        IReadOnlySet<ObservationId> blindedObservationIds,
        IReadOnlyList<CollectionInstance>? membershipInstances = null,
        IReadOnlyList<ValidationFindingView>? activeErrorFindings = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(observationsById);
        ArgumentNullException.ThrowIfNull(enrollmentsById);
        ArgumentNullException.ThrowIfNull(versionsById);
        ArgumentNullException.ThrowIfNull(openQueries);
        ArgumentNullException.ThrowIfNull(answeredQueryIds);
        ArgumentNullException.ThrowIfNull(blindedObservationIds);

        var specsByVersion = versionsById.ToDictionary(
            kvp => kvp.Key,
            kvp => ProtocolDesignDocument.Read(kvp.Value.Content).Design?.CollectionSpecs ?? []);

        var membershipByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, CollectionSpec? Spec, CollectionItem? Item)>();
        foreach (var instance in membershipInstances ?? instances)
        {
            var specs = specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            foreach (var member in instance.Members)
            {
                var item = spec?.Items.FirstOrDefault(i => string.Equals(i.Id, member.ItemId, StringComparison.Ordinal));
                membershipByObservation[member.ObservationId] = (instance, spec, item);
            }
        }

        var blockers = new List<LockReadinessBlocker>();

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
            var owner = answered ? MonitorOwner : SiteOwner;
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
                var nextAction = $"{SiteOwner}: record a value or reasoned missing value for required item '{itemLabel}' ({item.Id}) on form '{collectionTitle}' ({targetRef}).";

                blockers.Add(new LockReadinessBlocker(
                    LockBlockerKind.MissingRequiredItem,
                    SiteOwner,
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

        if (activeErrorFindings is { Count: > 0 })
        {
            var instancesById = instances.ToDictionary(i => i.Id);
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
                var nextAction = $"{MonitorOwner}: resolve, accept, or explain Error validation finding {findingRef} ({ruleLabel}: {finding.Message}) on {targetRef}.";

                blockers.Add(new LockReadinessBlocker(
                    LockBlockerKind.ErrorFinding,
                    MonitorOwner,
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
                    Blinded: obsId is { } matchedObsId && blindedObservationIds.Contains(matchedObsId),
                    nextAction,
                    NextCapability: "validation_finding.investigate",
                    NextTarget: findingRef,
                    FindingId: finding.Id,
                    RuleId: ruleLabel,
                    FindingMessage: finding.Message));
            }
        }

        var openQueryCount = blockers.Count(b => b.Kind == LockBlockerKind.OpenQuery);
        var missingRequiredCount = blockers.Count(b => b.Kind == LockBlockerKind.MissingRequiredItem);
        var openErrorFindingCount = blockers.Count(b => b.Kind == LockBlockerKind.ErrorFinding);
        var ready = blockers.Count == 0;
        var explanation = BuildExplanation(scope, openQueryCount, missingRequiredCount, openErrorFindingCount, blockers);

        return new LockReadinessSnapshot(
            study,
            studyCode,
            scope,
            ReadyForLock: ready,
            CanFreeze: true,
            openQueryCount,
            missingRequiredCount,
            blockers,
            explanation,
            openErrorFindingCount);
    }

    public static Explanation BuildExplanation(
        NodeRef scope,
        int openQueryCount,
        int missingRequiredCount,
        IReadOnlyList<LockReadinessBlocker> blockers) =>
        BuildExplanation(
            scope,
            openQueryCount,
            missingRequiredCount,
            blockers?.Count(b => b.Kind == LockBlockerKind.ErrorFinding) ?? 0,
            blockers!);

    public static Explanation BuildExplanation(
        NodeRef scope,
        int openQueryCount,
        int missingRequiredCount,
        int openErrorFindingCount,
        IReadOnlyList<LockReadinessBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(blockers);

        var queryEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.OpenQuery)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var missingEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.MissingRequiredItem)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var errorFindingEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.ErrorFinding)
            .Select(b => b.FindingId is { } fid ? NodeRef.For(fid) : b.Target)
            .Distinct()
            .ToArray();

        var queryNoun = openQueryCount == 1 ? "query" : "queries";
        var itemNoun = missingRequiredCount == 1 ? "item" : "items";
        var findingNoun = openErrorFindingCount == 1 ? "finding" : "findings";

        var queriesCondition = openQueryCount == 0
            ? EvaluatedCondition.Satisfied(QueriesClosedCondition, "All queries in the lock scope are closed.")
            : EvaluatedCondition.Unsatisfied(
                QueriesClosedCondition,
                $"Close {openQueryCount} open {queryNoun} in {scope} before applying a Locked assertion.",
                queryEvidence);

        var itemsCondition = missingRequiredCount == 0
            ? EvaluatedCondition.Satisfied(RequiredItemsCompleteCondition, "All required items on opened forms in the lock scope are recorded.")
            : EvaluatedCondition.Unsatisfied(
                RequiredItemsCompleteCondition,
                $"Complete {missingRequiredCount} missing required {itemNoun} in {scope} before applying a Locked assertion.",
                missingEvidence);

        var findingsCondition = openErrorFindingCount == 0
            ? EvaluatedCondition.Satisfied(ErrorFindingsResolvedCondition, "All Error validation findings in the lock scope are resolved, accepted, or explained.")
            : EvaluatedCondition.Unsatisfied(
                ErrorFindingsResolvedCondition,
                $"Resolve, accept, or explain {openErrorFindingCount} Error validation {findingNoun} in {scope} before applying a Locked assertion.",
                errorFindingEvidence);

        var conditions = new List<EvaluatedCondition> { queriesCondition, itemsCondition, findingsCondition };

        var steps = new List<ExplanationStep>();
        var nextActions = new List<NextAction>();

        foreach (var blocker in blockers)
        {
            NodeRef[] evidence;
            if (blocker.QueryAssertionId is { } qid)
            {
                evidence = [NodeRef.For(qid), blocker.Target];
            }
            else if (blocker.FindingId is { } fid)
            {
                evidence = [NodeRef.For(fid), blocker.Target];
            }
            else
            {
                evidence = [blocker.Target];
            }
            var conditionCode = blocker.Kind switch
            {
                LockBlockerKind.OpenQuery => OpenQueryBlockerCondition,
                LockBlockerKind.ErrorFinding => ErrorFindingBlockerCondition,
                _ => MissingRequiredItemBlockerCondition,
            };
            var queryLabel = blocker.QueryAssertionId is { } q ? NodeRef.For(q).ToString() : "query";
            var findingLabel = blocker.FindingId is { } f ? NodeRef.For(f).ToString() : "finding";
            var claim = blocker.Kind switch
            {
                LockBlockerKind.OpenQuery =>
                    $"{blocker.Owner} owns open query {queryLabel} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.ErrorFinding =>
                    $"{blocker.Owner} owns Error validation finding {findingLabel} ({blocker.RuleId ?? "rule"}) on {blocker.Target}.",
                _ =>
                    $"{blocker.Owner} owns missing required item '{blocker.ItemLabel ?? blocker.ItemId}' on {blocker.Target} ({blocker.CollectionTitle ?? blocker.CollectionSpecId}).",
            };

            conditions.Add(EvaluatedCondition.Unsatisfied(conditionCode, blocker.NextAction, evidence));
            steps.Add(new ExplanationStep(claim, evidence));
            nextActions.Add(new NextAction(blocker.NextAction, blocker.NextCapability, blocker.NextTarget));
        }

        if (steps.Count == 0)
        {
            steps.Add(new ExplanationStep($"Checked queries, opened form instances, and validation findings under {scope}; no blockers remain.", [scope]));
        }

        string headline;
        if (blockers.Count == 0)
        {
            headline = $"Scope {scope} is ready for a Locked assertion: 0 open queries, 0 missing required items, and 0 open Error validation findings.";
        }
        else if (openErrorFindingCount > 0)
        {
            headline = $"Locking {scope} is blocked by {openQueryCount} open {queryNoun}, {missingRequiredCount} missing required {itemNoun}, and {openErrorFindingCount} open Error validation {findingNoun}; Frozen remains available while blockers are resolved.";
        }
        else
        {
            headline = $"Locking {scope} is blocked by {openQueryCount} open {queryNoun} and {missingRequiredCount} missing required {itemNoun}; Frozen remains available while blockers are resolved.";
        }

        return new Explanation(scope, headline, Producer, steps, conditions, nextActions);
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

    private static async Task<IReadOnlyList<ValidationFindingView>> ReadActiveErrorFindingsAsync(
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

    private static async Task<(IReadOnlyList<Assertion> Queries, IReadOnlySet<AssertionId> AnsweredQueryIds)> ReadOpenQueriesAsync(
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
}

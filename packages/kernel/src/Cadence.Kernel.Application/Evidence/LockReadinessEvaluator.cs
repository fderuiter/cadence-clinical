using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Evaluates whether a target lock scope is ready for a <see cref="LockKind.Locked"/> assertion (#5466, spec §10.5):
/// every <see cref="AssertionKind.Queried"/> assertion in the scope must be retracted (closed), every required
/// <see cref="CollectionItem"/> on opened <see cref="CollectionInstance"/>s in the scope must hold a recorded value
/// (a reasoned <see cref="MissingValue"/> other than <see cref="NullFlavor.NotAsked"/> included), every recorded
/// observation in the scope must be source-verified at its current revision, every populated form in the scope must
/// hold a current electronic signature, every due or closed scheduled visit on active enrollments in the scope must
/// have its forms opened, and every active <see cref="ValidationSeverity.Error"/> validation finding in the scope must
/// be resolved, accepted, or explained. <see cref="LockKind.Frozen"/> commits skip this gate so staff can close
/// queries, verify values, and sign forms before hard lock.
/// </summary>
public static class LockReadinessEvaluator
{
    public const string Producer = "kernel.lock_readiness";
    public const string QueriesClosedCondition = "lock.queries_closed";
    public const string RequiredItemsCompleteCondition = "lock.required_items_complete";
    public const string ObservationsVerifiedCondition = "lock.observations_verified";
    public const string FormsSignedCondition = "lock.forms_signed";
    public const string ScheduledVisitsOpenedCondition = "lock.scheduled_visits_opened";
    public const string ErrorFindingsResolvedCondition = "lock.error_findings_resolved";
    public const string ScreeningEnrollmentsResolvedCondition = "lock.screening_enrollments_resolved";
    public const string ObservationsReviewedCondition = "lock.observations_reviewed";

    public const string OpenQueryBlockerCondition = "lock.open_query";
    public const string MissingRequiredItemBlockerCondition = "lock.missing_required_item";
    public const string UnverifiedValueBlockerCondition = "lock.unverified_value";
    public const string UnsignedFormBlockerCondition = "lock.unsigned_form";
    public const string UnopenedVisitBlockerCondition = "lock.unopened_visit";
    public const string ErrorFindingBlockerCondition = "lock.error_finding";
    public const string ScreeningEnrollmentBlockerCondition = "lock.screening_enrollment";
    public const string UnreviewedValueBlockerCondition = "lock.unreviewed_value";

    public const string SiteOwner = "Site coordinator / investigator";
    public const string MonitorOwner = "Clinical research associate / data manager";
    public const string InvestigatorOwner = "Principal investigator";

    /// <summary>
    /// Evaluates lock readiness of <paramref name="scope"/> inside <paramref name="study"/> over <paramref name="work"/>
    /// and returns <see cref="AssertionErrors.ScopeNotReady"/> with its structured <see cref="Explanation"/> when any
    /// readiness blocker prevents <see cref="LockKind.Locked"/>; returns null when ready.
    /// </summary>
    internal static Task<KernelError?> CheckAsync(
        IKernelUnitOfWork work,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null,
        LockMilestone milestone = LockMilestone.InterimCut) =>
        CheckAsync(TenantId.Empty, work, study, scope, findings: null, cancellationToken, evaluatedAt, milestone);

    internal static async Task<KernelError?> CheckAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null,
        LockMilestone milestone = LockMilestone.InterimCut)
    {
        var snapshot = await EvaluateAsync(
            tenant,
            work,
            study,
            studyCode: string.Empty,
            scope,
            findings,
            cancellationToken,
            evaluatedAt,
            milestone).ConfigureAwait(false);
        return snapshot.ReadyForLock
            ? null
            : AssertionErrors.ScopeNotReady.WithMessage(snapshot.Explanation.Claim) with { Explanation = snapshot.Explanation };
    }

    internal static Task<LockReadinessSnapshot> EvaluateAsync(
        IKernelUnitOfWork work,
        StudyId study,
        string studyCode,
        NodeRef scope,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null,
        LockMilestone milestone = LockMilestone.InterimCut) =>
        EvaluateAsync(TenantId.Empty, work, study, studyCode, scope, findings: null, cancellationToken, evaluatedAt, milestone);

    internal static async Task<LockReadinessSnapshot> EvaluateAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        string studyCode,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null,
        LockMilestone milestone = LockMilestone.InterimCut)
    {
        IReadOnlyList<CollectionInstance> instances;
        IReadOnlyList<Observation> coveredObservations;
        IReadOnlyList<Enrollment>? scopedEnrollments = null;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                scopedEnrollments = await work.ReadAllAsync<Enrollment>(e => e.StudyId == study, cancellationToken).ConfigureAwait(false);
                instances = await work.ReadAllAsync<CollectionInstance>(c => c.StudyId == study, cancellationToken).ConfigureAwait(false);
                coveredObservations = await work.ReadAllAsync<Observation>(o => o.StudyId == study, cancellationToken).ConfigureAwait(false);
                break;

            case NodeKinds.StudySite:
                var siteId = StudySiteId.FromGuid(scope.Id);
                scopedEnrollments = await work.ReadAllAsync<Enrollment>(
                    e => e.StudyId == study && e.StudySiteId == siteId,
                    cancellationToken).ConfigureAwait(false);
                var siteEnrollmentIds = scopedEnrollments.Select(e => e.Id).ToList();
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
                scopedEnrollments = await work.ReadAllAsync<Enrollment>(
                    e => e.StudyId == study && e.Id == enrollmentId,
                    cancellationToken).ConfigureAwait(false);
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

        Dictionary<EnrollmentId, Enrollment> enrollmentsById;
        if (scopedEnrollments is not null)
        {
            enrollmentsById = scopedEnrollments.ToDictionary(e => e.Id);
            var extraEnrollmentIds = instances.Select(i => i.EnrollmentId)
                .Concat(coveredObservations.Select(o => o.EnrollmentId))
                .Where(id => !enrollmentsById.ContainsKey(id))
                .Distinct()
                .ToList();
            if (extraEnrollmentIds.Count > 0)
            {
                var extraEnrollments = await work.ReadAllAsync<Enrollment>(
                    e => extraEnrollmentIds.Contains(e.Id),
                    cancellationToken).ConfigureAwait(false);
                foreach (var extra in extraEnrollments)
                {
                    enrollmentsById[extra.Id] = extra;
                }
            }
        }
        else
        {
            var enrollmentIds = instances.Select(i => i.EnrollmentId)
                .Concat(coveredObservations.Select(o => o.EnrollmentId))
                .Distinct()
                .ToList();
            enrollmentsById = enrollmentIds.Count == 0
                ? new Dictionary<EnrollmentId, Enrollment>()
                : (await work.ReadAllAsync<Enrollment>(e => enrollmentIds.Contains(e.Id), cancellationToken).ConfigureAwait(false))
                    .ToDictionary(e => e.Id);
        }

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
        var verifiedRevisions = await ReadVerifiedRevisionsAsync(work, study, coveredObservations, AssertionKind.Verified, cancellationToken).ConfigureAwait(false);
        var reviewedRevisions = milestone == LockMilestone.FinalDatabaseLock
            ? await ReadVerifiedRevisionsAsync(work, study, coveredObservations, AssertionKind.Reviewed, cancellationToken).ConfigureAwait(false)
            : null;
        var formSignatures = await ReadFormSignaturesAsync(work, instances, cancellationToken).ConfigureAwait(false);
        var activeErrorFindings = await ReadActiveErrorFindingsAsync(tenant, study, findings, cancellationToken).ConfigureAwait(false);

        Dictionary<StudySiteId, StudySite>? studySitesById = null;
        Dictionary<SiteId, Site>? sitesById = null;
        IReadOnlyList<Effectivity>? studyEffectivities = null;
        if (evaluatedAt is not null
            && scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
            && enrollmentsById.Values.Any(NeedsScheduleEvaluation))
        {
            var scheduleStudySiteIds = enrollmentsById.Values
                .Where(NeedsScheduleEvaluation)
                .Select(e => e.StudySiteId)
                .Distinct()
                .ToList();
            studySitesById = (await work.ReadAllAsync<StudySite>(
                    s => scheduleStudySiteIds.Contains(s.Id),
                    cancellationToken).ConfigureAwait(false))
                .ToDictionary(s => s.Id);
            var rawSiteIds = studySitesById.Values.Select(s => s.SiteId).Distinct().ToList();
            sitesById = rawSiteIds.Count == 0
                ? new Dictionary<SiteId, Site>()
                : (await work.ReadAllAsync<Site>(s => rawSiteIds.Contains(s.Id), cancellationToken).ConfigureAwait(false))
                    .ToDictionary(s => s.Id);
            studyEffectivities = await work.ReadAllAsync<Effectivity>(e => e.StudyId == study, cancellationToken).ConfigureAwait(false);
            var extraScheduleVersions = studyEffectivities
                .Select(e => e.ProtocolVersionId)
                .Where(id => !versionsById.ContainsKey(id))
                .Distinct()
                .ToList();
            if (extraScheduleVersions.Count > 0)
            {
                var loadedVersions = await work.ReadAllAsync<ProtocolVersion>(
                    v => extraScheduleVersions.Contains(v.Id),
                    cancellationToken).ConfigureAwait(false);
                foreach (var v in loadedVersions)
                {
                    versionsById[v.Id] = v;
                }
            }
        }

        IReadOnlyList<CollectionInstance>? membershipInstances = null;
        if (scope.Kind == NodeKinds.Observation && coveredObservations.Count > 0)
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
            activeErrorFindings,
            verifiedRevisions,
            formSignatures,
            evaluatedAt,
            studySitesById,
            sitesById,
            studyEffectivities,
            milestone,
            reviewedRevisions);
    }

    public static bool NeedsScheduleEvaluation(Enrollment enrollment) =>
        enrollment is not null
        && enrollment.Status != EnrollmentStatus.ScreenFailed
        && enrollment.Periods.Any(p => p.Status == EnrollmentStatus.Enrolled);

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
        IReadOnlyList<ValidationFindingView>? activeErrorFindings = null,
        IReadOnlySet<(ObservationId ObservationId, int Revision)>? verifiedRevisions = null,
        IReadOnlyDictionary<CollectionInstanceId, IReadOnlyList<Attestation>>? formSignaturesByInstance = null,
        DateTimeOffset? evaluatedAt = null,
        IReadOnlyDictionary<StudySiteId, StudySite>? studySitesById = null,
        IReadOnlyDictionary<SiteId, Site>? sitesById = null,
        IReadOnlyList<Effectivity>? studyEffectivities = null,
        LockMilestone milestone = LockMilestone.InterimCut,
        IReadOnlySet<(ObservationId ObservationId, int Revision)>? reviewedRevisions = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(observationsById);
        ArgumentNullException.ThrowIfNull(enrollmentsById);
        ArgumentNullException.ThrowIfNull(versionsById);
        ArgumentNullException.ThrowIfNull(openQueries);
        ArgumentNullException.ThrowIfNull(answeredQueryIds);
        ArgumentNullException.ThrowIfNull(blindedObservationIds);

        var designsByVersion = versionsById.ToDictionary(
            kvp => kvp.Key,
            kvp => ProtocolDesignDocument.Read(kvp.Value.Content).Design);
        var designs = designsByVersion.Values.Where(d => d is not null).Select(d => d!).ToList();
        var specsByVersion = designsByVersion.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.CollectionSpecs ?? []);

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

        // 1. Open queries
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

        // 2. Missing required items on opened forms
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

        // 3. Unverified current revisions of recorded observations in scope
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
            var nextAction = $"{MonitorOwner}: complete source data verification for revision {obs.Revision} of '{itemLabel}' ({targetRef}).";

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.UnverifiedValue,
                MonitorOwner,
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

        // 3b. Final database lock: recorded observations lacking an active source data review (AssertionKind.Reviewed) at their current revision
        if (milestone == LockMilestone.FinalDatabaseLock)
        {
            var reviewedSet = reviewedRevisions ?? new HashSet<(ObservationId, int)>();
            foreach (var obs in observationsById.Values
                .Where(o => inScopeObservationIds.Contains(o.Id) && o.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                .OrderBy(o => o.ObservedAt)
                .ThenBy(o => o.Id))
            {
                if (reviewedSet.Contains((obs.Id, obs.Revision)))
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
                var nextAction = $"{MonitorOwner}: complete source data review for revision {obs.Revision} of '{itemLabel}' ({targetRef}) before final database lock.";

                blockers.Add(new LockReadinessBlocker(
                    LockBlockerKind.UnreviewedValue,
                    MonitorOwner,
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
                    NextCapability: "observation.review",
                    NextTarget: targetRef,
                    CurrentRevision: obs.Revision));
            }
        }

        // 4. Forms lacking a current investigator signature (or holding only superseded signatures).
        //    InterimCut ignores opened forms that hold only NotAsked placeholders; FinalDatabaseLock requires all opened forms to be signed.
        var signaturesMap = formSignaturesByInstance ?? new Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>>();
        foreach (var instance in instances.OrderBy(i => i.OpenedAt).ThenBy(i => i.Id))
        {
            var memberObservations = instance.Members
                .Select(m => observationsById.GetValueOrDefault(m.ObservationId))
                .Where(o => o is not null)
                .Select(o => o!)
                .ToList();
            var hasPopulatedObservation = memberObservations.Any(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked });
            if (!hasPopulatedObservation && milestone != LockMilestone.FinalDatabaseLock)
            {
                continue;
            }

            var currentHash = memberObservations.Count > 0
                ? CollectionInstance.ContentHashOf(memberObservations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)))
                : null;
            var signatures = signaturesMap.GetValueOrDefault(instance.Id) ?? [];
            var hasCurrentSignature = currentHash is not null && signatures.Any(a =>
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
            string nextAction;
            string itemLabel;
            if (superseded)
            {
                nextAction = $"{InvestigatorOwner}: re-sign form '{collectionTitle}' ({targetRef}) because a subsequent data change superseded its prior signature.";
                itemLabel = "Signature superseded";
            }
            else if (hasPopulatedObservation)
            {
                nextAction = $"{InvestigatorOwner}: sign populated form '{collectionTitle}' ({targetRef}) before locking.";
                itemLabel = "Awaiting investigator signature";
            }
            else
            {
                nextAction = $"{InvestigatorOwner}: complete or sign opened form '{collectionTitle}' ({targetRef}) before final database lock.";
                itemLabel = "Opened unpopulated form awaiting disposition or signature";
            }

            blockers.Add(new LockReadinessBlocker(
                LockBlockerKind.UnsignedForm,
                InvestigatorOwner,
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
                ItemLabel: itemLabel,
                QueryText: null,
                Answered: false,
                Blinded: false,
                nextAction,
                NextCapability: "collection.sign",
                NextTarget: targetRef,
                SignatureSuperseded: superseded));
        }

        // 4b. Final database lock: enrollments still in Screening disposition must resolve (Enrolled or ScreenFailed)
        if (milestone == LockMilestone.FinalDatabaseLock
            && scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment)
        {
            foreach (var enrollment in enrollmentsById.Values
                .Where(e => e.Status == EnrollmentStatus.Screening)
                .OrderBy(e => e.SubjectCode.Value, StringComparer.Ordinal)
                .ThenBy(e => e.Id))
            {
                var targetRef = NodeRef.For(enrollment.Id);
                var nextAction = $"{SiteOwner}: complete enrollment or record screen failure for screening subject '{enrollment.SubjectCode.Value}' ({targetRef}) before final database lock.";

                blockers.Add(new LockReadinessBlocker(
                    LockBlockerKind.ScreeningEnrollment,
                    SiteOwner,
                    targetRef,
                    enrollment.StudySiteId,
                    enrollment.Id,
                    CollectionInstanceId: null,
                    ObservationId: null,
                    QueryAssertionId: null,
                    enrollment.SubjectCode.Value,
                    CollectionSpecId: null,
                    CollectionTitle: null,
                    ItemId: null,
                    ItemLabel: "Screening disposition pending",
                    QueryText: null,
                    Answered: false,
                    Blinded: false,
                    nextAction,
                    NextCapability: "enrollment.change_status",
                    NextTarget: targetRef));
            }
        }

        // 5. Unopened scheduled visits (due/Open or Closed, plus NotYetOpen on Enrolled subjects for FinalDatabaseLock)
        //    on active enrollments in scope, excluding screen-failed enrollments and visits scheduled after participation ended on withdrawn enrollments.
        int pendingScheduledVisitCount = 0;
        if (evaluatedAt is { } evalInstant
            && scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
            && studySitesById is not null
            && sitesById is not null
            && studyEffectivities is not null)
        {
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
                    var isPendingFutureVisit = milestone == LockMilestone.FinalDatabaseLock
                        && enrollment.Status == EnrollmentStatus.Enrolled
                        && expected.Status == VisitWindowStatus.NotYetOpen;
                    if (expected.Status is not (VisitWindowStatus.Open or VisitWindowStatus.Closed) && !isPendingFutureVisit)
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

                        if (isPendingFutureVisit)
                        {
                            pendingScheduledVisitCount++;
                        }

                        var specTitle = specsById.TryGetValue(specId, out var spec) && !string.IsNullOrWhiteSpace(spec.Name)
                            ? spec.Name
                            : specId;
                        var windowStateLabel = expected.Status switch
                        {
                            VisitWindowStatus.Closed => "overdue",
                            VisitWindowStatus.NotYetOpen => "pending scheduled",
                            _ => "due",
                        };
                        var nextAction = $"{SiteOwner}: open scheduled form '{specTitle}' ({specId}) for {windowStateLabel} visit '{activity.Name}' (target {expected.Target:yyyy-MM-dd}) on {targetRef}.";

                        blockers.Add(new LockReadinessBlocker(
                            LockBlockerKind.UnopenedVisit,
                            SiteOwner,
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

        // 6. Active Error validation findings
        if (activeErrorFindings is { Count: > 0 })
        {
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
                var nextAction = $"{MonitorOwner}: resolve, accept, or explain Error validation finding {findingRef} ({ruleLabel}: {finding.Message}) on {targetRef}.";
                var isBlindedFinding = (obsId is { } matchedObsId && blindedObservationIds.Contains(matchedObsId))
                    || BlindedTreatmentClassifier.Instance.IsBlindedFinding(finding.Target, finding.Observed, designs)
                    || ReferencesBlindedObservation(finding, collectionInstanceId, enrollmentId, instancesById, instancesByEnrollment, blindedObservationIds);

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
                    Blinded: isBlindedFinding,
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
        var unverifiedValueCount = blockers.Count(b => b.Kind == LockBlockerKind.UnverifiedValue);
        var unsignedFormCount = blockers.Count(b => b.Kind == LockBlockerKind.UnsignedForm);
        var unopenedVisitCount = blockers.Count(b => b.Kind == LockBlockerKind.UnopenedVisit);
        var openErrorFindingCount = blockers.Count(b => b.Kind == LockBlockerKind.ErrorFinding);
        var screeningEnrollmentCount = blockers.Count(b => b.Kind == LockBlockerKind.ScreeningEnrollment);
        var unreviewedValueCount = blockers.Count(b => b.Kind == LockBlockerKind.UnreviewedValue);
        var ready = blockers.Count == 0;
        var explanation = BuildExplanation(
            scope,
            openQueryCount,
            missingRequiredCount,
            openErrorFindingCount,
            blockers,
            unverifiedValueCount,
            unsignedFormCount,
            unopenedVisitCount,
            milestone,
            screeningEnrollmentCount,
            unreviewedValueCount);

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
            openErrorFindingCount,
            unverifiedValueCount,
            unsignedFormCount,
            unopenedVisitCount,
            milestone,
            screeningEnrollmentCount,
            pendingScheduledVisitCount,
            unreviewedValueCount);
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
            blockers!,
            blockers?.Count(b => b.Kind == LockBlockerKind.UnverifiedValue) ?? 0,
            blockers?.Count(b => b.Kind == LockBlockerKind.UnsignedForm) ?? 0,
            blockers?.Count(b => b.Kind == LockBlockerKind.UnopenedVisit) ?? 0,
            LockMilestone.InterimCut,
            blockers?.Count(b => b.Kind == LockBlockerKind.ScreeningEnrollment) ?? 0,
            blockers?.Count(b => b.Kind == LockBlockerKind.UnreviewedValue) ?? 0);

    public static Explanation BuildExplanation(
        NodeRef scope,
        int openQueryCount,
        int missingRequiredCount,
        int openErrorFindingCount,
        IReadOnlyList<LockReadinessBlocker> blockers,
        int unverifiedValueCount = 0,
        int unsignedFormCount = 0,
        int unopenedVisitCount = 0,
        LockMilestone milestone = LockMilestone.InterimCut,
        int screeningEnrollmentCount = 0,
        int unreviewedValueCount = 0)
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
        var unverifiedEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnverifiedValue)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unsignedFormEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnsignedForm)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unopenedVisitEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnopenedVisit)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var errorFindingEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.ErrorFinding)
            .Select(b => b.FindingId is { } fid ? NodeRef.For(fid) : b.Target)
            .Distinct()
            .ToArray();
        var screeningEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.ScreeningEnrollment)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unreviewedEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnreviewedValue)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();

        var queryNoun = openQueryCount == 1 ? "query" : "queries";
        var itemNoun = missingRequiredCount == 1 ? "item" : "items";
        var unverifiedNoun = unverifiedValueCount == 1 ? "revision" : "revisions";
        var formNoun = unsignedFormCount == 1 ? "form" : "forms";
        var visitNoun = unopenedVisitCount == 1 ? "visit form" : "visit forms";
        var findingNoun = openErrorFindingCount == 1 ? "finding" : "findings";
        var screeningNoun = screeningEnrollmentCount == 1 ? "screening enrollment" : "screening enrollments";
        var unreviewedNoun = unreviewedValueCount == 1 ? "revision" : "revisions";

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

        var verifiedCondition = unverifiedValueCount == 0
            ? EvaluatedCondition.Satisfied(ObservationsVerifiedCondition, "All recorded observation revisions in the lock scope are source-verified.")
            : EvaluatedCondition.Unsatisfied(
                ObservationsVerifiedCondition,
                $"Verify {unverifiedValueCount} unverified observation {unverifiedNoun} in {scope} before applying a Locked assertion.",
                unverifiedEvidence);

        var formsSignedSatisfiedText = milestone == LockMilestone.FinalDatabaseLock
            ? "All opened forms in the lock scope hold a current investigator signature."
            : "All populated forms in the lock scope hold a current investigator signature.";
        var formsSignedCondition = unsignedFormCount == 0
            ? EvaluatedCondition.Satisfied(FormsSignedCondition, formsSignedSatisfiedText)
            : EvaluatedCondition.Unsatisfied(
                FormsSignedCondition,
                $"Sign {unsignedFormCount} unsigned or superseded {formNoun} in {scope} before applying a Locked assertion.",
                unsignedFormEvidence);

        var visitsOpenedSatisfiedText = milestone == LockMilestone.FinalDatabaseLock
            ? "All scheduled visit forms (including pending future visits) on active enrollments in the lock scope are opened."
            : "All due or closed scheduled visit forms on active enrollments in the lock scope are opened.";
        var visitsOpenedCondition = unopenedVisitCount == 0
            ? EvaluatedCondition.Satisfied(ScheduledVisitsOpenedCondition, visitsOpenedSatisfiedText)
            : EvaluatedCondition.Unsatisfied(
                ScheduledVisitsOpenedCondition,
                $"Open {unopenedVisitCount} scheduled {visitNoun} in {scope} before applying a Locked assertion.",
                unopenedVisitEvidence);

        var findingsCondition = openErrorFindingCount == 0
            ? EvaluatedCondition.Satisfied(ErrorFindingsResolvedCondition, "All Error validation findings in the lock scope are resolved, accepted, or explained.")
            : EvaluatedCondition.Unsatisfied(
                ErrorFindingsResolvedCondition,
                $"Resolve, accept, or explain {openErrorFindingCount} Error validation {findingNoun} in {scope} before applying a Locked assertion.",
                errorFindingEvidence);

        var conditions = new List<EvaluatedCondition>
        {
            queriesCondition,
            itemsCondition,
            verifiedCondition,
            formsSignedCondition,
            visitsOpenedCondition,
            findingsCondition,
        };

        if (milestone == LockMilestone.FinalDatabaseLock)
        {
            var screeningCondition = screeningEnrollmentCount == 0
                ? EvaluatedCondition.Satisfied(ScreeningEnrollmentsResolvedCondition, "All enrollments in the lock scope have resolved screening disposition.")
                : EvaluatedCondition.Unsatisfied(
                    ScreeningEnrollmentsResolvedCondition,
                    $"Resolve {screeningEnrollmentCount} {screeningNoun} in {scope} before applying a final database lock.",
                    screeningEvidence);
            var reviewedCondition = unreviewedValueCount == 0
                ? EvaluatedCondition.Satisfied(ObservationsReviewedCondition, "All recorded observation revisions in the lock scope are source-reviewed.")
                : EvaluatedCondition.Unsatisfied(
                    ObservationsReviewedCondition,
                    $"Review {unreviewedValueCount} unreviewed observation {unreviewedNoun} in {scope} before applying a final database lock.",
                    unreviewedEvidence);
            conditions.Add(screeningCondition);
            conditions.Add(reviewedCondition);
        }

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
                LockBlockerKind.UnverifiedValue => UnverifiedValueBlockerCondition,
                LockBlockerKind.UnreviewedValue => UnreviewedValueBlockerCondition,
                LockBlockerKind.UnsignedForm => UnsignedFormBlockerCondition,
                LockBlockerKind.UnopenedVisit => UnopenedVisitBlockerCondition,
                LockBlockerKind.ScreeningEnrollment => ScreeningEnrollmentBlockerCondition,
                LockBlockerKind.ErrorFinding => ErrorFindingBlockerCondition,
                _ => MissingRequiredItemBlockerCondition,
            };
            var queryLabel = blocker.QueryAssertionId is { } q ? NodeRef.For(q).ToString() : "query";
            var findingLabel = blocker.FindingId is { } f ? NodeRef.For(f).ToString() : "finding";
            var claim = blocker.Kind switch
            {
                LockBlockerKind.OpenQuery =>
                    $"{blocker.Owner} owns open query {queryLabel} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.UnverifiedValue =>
                    $"{blocker.Owner} owns unverified revision {blocker.CurrentRevision ?? 1} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.UnreviewedValue =>
                    $"{blocker.Owner} owns unreviewed revision {blocker.CurrentRevision ?? 1} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.UnsignedForm =>
                    $"{blocker.Owner} owns {(blocker.SignatureSuperseded ? "superseded signature" : "unsigned form")} on {blocker.Target} ({blocker.CollectionTitle ?? blocker.CollectionSpecId}).",
                LockBlockerKind.UnopenedVisit =>
                    $"{blocker.Owner} owns unopened scheduled visit '{blocker.PlannedActivityName ?? blocker.PlannedActivityId}' ({blocker.CollectionTitle ?? blocker.CollectionSpecId}) on {blocker.Target}.",
                LockBlockerKind.ScreeningEnrollment =>
                    $"{blocker.Owner} owns unresolved screening enrollment {blocker.Target} ({blocker.SubjectCode ?? "subject"}).",
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
            steps.Add(new ExplanationStep(
                $"Checked queries, required form items, source verification, form signatures, scheduled visits, and validation findings under {scope}; no blockers remain.",
                [scope]));
        }

        string headline;
        if (blockers.Count == 0)
        {
            headline = milestone == LockMilestone.FinalDatabaseLock
                ? $"Scope {scope} is ready for a FinalDatabaseLock assertion: 0 screening enrollments, 0 open queries, 0 missing required items, 0 unverified revisions, 0 unreviewed revisions, 0 unsigned forms, 0 unopened visits, and 0 open Error validation findings."
                : $"Scope {scope} is ready for a Locked assertion: 0 open queries, 0 missing required items, 0 unverified revisions, 0 unsigned forms, 0 unopened visits, and 0 open Error validation findings.";
        }
        else
        {
            var summaryParts = new List<string>();
            if (screeningEnrollmentCount > 0)
            {
                summaryParts.Add($"{screeningEnrollmentCount} {screeningNoun}");
            }

            if (openQueryCount > 0)
            {
                summaryParts.Add($"{openQueryCount} open {queryNoun}");
            }

            if (missingRequiredCount > 0)
            {
                summaryParts.Add($"{missingRequiredCount} missing required {itemNoun}");
            }

            if (unverifiedValueCount > 0)
            {
                summaryParts.Add($"{unverifiedValueCount} unverified {unverifiedNoun}");
            }

            if (unreviewedValueCount > 0)
            {
                summaryParts.Add($"{unreviewedValueCount} unreviewed {unreviewedNoun}");
            }

            if (unsignedFormCount > 0)
            {
                summaryParts.Add($"{unsignedFormCount} unsigned {formNoun}");
            }

            if (unopenedVisitCount > 0)
            {
                summaryParts.Add($"{unopenedVisitCount} unopened {visitNoun}");
            }

            if (openErrorFindingCount > 0)
            {
                summaryParts.Add($"{openErrorFindingCount} open Error validation {findingNoun}");
            }

            headline = $"Locking {scope} is blocked by {FormatList(summaryParts)}; Frozen remains available while blockers are resolved.";
        }

        return new Explanation(scope, headline, Producer, steps, conditions, nextActions);
    }

    private static string FormatList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => "unresolved readiness blockers",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };

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

    private static async Task<IReadOnlySet<(ObservationId ObservationId, int Revision)>> ReadVerifiedRevisionsAsync(
        IKernelUnitOfWork work,
        StudyId study,
        IReadOnlyList<Observation> coveredObservations,
        AssertionKind assertionKind,
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
                && a.Kind == assertionKind
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

    private static async Task<IReadOnlyDictionary<CollectionInstanceId, IReadOnlyList<Attestation>>> ReadFormSignaturesAsync(
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
}

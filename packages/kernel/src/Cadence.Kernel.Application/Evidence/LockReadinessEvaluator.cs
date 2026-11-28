using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    public const string OpenQueryBlockerCondition = "lock.open_query";
    public const string MissingRequiredItemBlockerCondition = "lock.missing_required_item";
    public const string UnverifiedValueBlockerCondition = "lock.unverified_value";
    public const string UnsignedFormBlockerCondition = "lock.unsigned_form";
    public const string UnopenedVisitBlockerCondition = "lock.unopened_visit";
    public const string ErrorFindingBlockerCondition = "lock.error_finding";

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
        DateTimeOffset? evaluatedAt = null) =>
        CheckAsync(TenantId.Empty, work, study, scope, findings: null, cancellationToken, evaluatedAt);

    internal static async Task<KernelError?> CheckAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null)
    {
        var snapshot = await EvaluateAsync(
            tenant,
            work,
            study,
            studyCode: string.Empty,
            scope,
            findings,
            cancellationToken,
            evaluatedAt).ConfigureAwait(false);
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
        DateTimeOffset? evaluatedAt = null) =>
        EvaluateAsync(TenantId.Empty, work, study, studyCode, scope, findings: null, cancellationToken, evaluatedAt);

    internal static async Task<LockReadinessSnapshot> EvaluateAsync(
        TenantId tenant,
        IKernelUnitOfWork work,
        StudyId study,
        string studyCode,
        NodeRef scope,
        IValidationFindings? findings,
        CancellationToken cancellationToken,
        DateTimeOffset? evaluatedAt = null)
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

        var openQueries = await LockReadinessFindingAndUnitReaders.ReadOpenQueriesAsync(work, study, coveredObservations, cancellationToken).ConfigureAwait(false);
        var verifiedRevisions = await LockReadinessFindingAndUnitReaders.ReadVerifiedRevisionsAsync(work, study, coveredObservations, cancellationToken).ConfigureAwait(false);
        var formSignatures = await LockReadinessFindingAndUnitReaders.ReadFormSignaturesAsync(work, instances, cancellationToken).ConfigureAwait(false);
        var activeErrorFindings = await LockReadinessFindingAndUnitReaders.ReadActiveErrorFindingsAsync(tenant, study, findings, cancellationToken).ConfigureAwait(false);

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
            studyEffectivities);
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
        IReadOnlyList<Effectivity>? studyEffectivities = null)
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
        LockReadinessFormAndScheduleBlockers.CollectOpenQueryBlockers(
            blockers, openQueries, answeredQueryIds, observationsById, enrollmentsById, membershipByObservation, blindedObservationIds);
        LockReadinessFormAndScheduleBlockers.CollectMissingRequiredItemBlockers(
            blockers, instances, specsByVersion, observationsById, enrollmentsById);
        LockReadinessFormAndScheduleBlockers.CollectUnverifiedValueBlockers(
            blockers, scope, instances, observationsById, enrollmentsById, membershipByObservation, blindedObservationIds, verifiedRevisions);
        LockReadinessFormAndScheduleBlockers.CollectUnsignedFormBlockers(
            blockers, instances, specsByVersion, observationsById, enrollmentsById, formSignaturesByInstance);
        LockReadinessFormAndScheduleBlockers.CollectUnopenedVisitBlockers(
            blockers, scope, instances, enrollmentsById, versionsById, designsByVersion, evaluatedAt, studySitesById, sitesById, studyEffectivities);
        LockReadinessFindingAndUnitReaders.CollectErrorFindingBlockers(
            blockers, scope, instances, membershipInstances, observationsById, enrollmentsById, membershipByObservation, specsByVersion, designs, blindedObservationIds, activeErrorFindings);

        var openQueryCount = blockers.Count(b => b.Kind == LockBlockerKind.OpenQuery);
        var missingRequiredCount = blockers.Count(b => b.Kind == LockBlockerKind.MissingRequiredItem);
        var unverifiedValueCount = blockers.Count(b => b.Kind == LockBlockerKind.UnverifiedValue);
        var unsignedFormCount = blockers.Count(b => b.Kind == LockBlockerKind.UnsignedForm);
        var unopenedVisitCount = blockers.Count(b => b.Kind == LockBlockerKind.UnopenedVisit);
        var openErrorFindingCount = blockers.Count(b => b.Kind == LockBlockerKind.ErrorFinding);
        var ready = blockers.Count == 0;
        var explanation = LockReadinessExplanationBuilder.BuildExplanation(
            scope,
            openQueryCount,
            missingRequiredCount,
            openErrorFindingCount,
            blockers,
            unverifiedValueCount,
            unsignedFormCount,
            unopenedVisitCount);

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
            unopenedVisitCount);
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
            blockers?.Count(b => b.Kind == LockBlockerKind.UnopenedVisit) ?? 0);

    public static Explanation BuildExplanation(
        NodeRef scope,
        int openQueryCount,
        int missingRequiredCount,
        int openErrorFindingCount,
        IReadOnlyList<LockReadinessBlocker> blockers,
        int unverifiedValueCount = 0,
        int unsignedFormCount = 0,
        int unopenedVisitCount = 0) =>
        LockReadinessExplanationBuilder.BuildExplanation(
            scope,
            openQueryCount,
            missingRequiredCount,
            openErrorFindingCount,
            blockers,
            unverifiedValueCount,
            unsignedFormCount,
            unopenedVisitCount);
}

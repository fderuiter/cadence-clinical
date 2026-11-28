using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class LockReadinessPersistenceReader
{
    internal static async Task<bool> ScopeExistsInStudyAsync(
        KernelDbContext db,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        scope.Kind switch
        {
            NodeKinds.Study => StudyId.FromGuid(scope.Id) == study,
            NodeKinds.StudySite => await db.StudySites.AsNoTracking()
                .AnyAsync(s => s.Id == StudySiteId.FromGuid(scope.Id) && s.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.Enrollment => await db.Enrollments.AsNoTracking()
                .AnyAsync(e => e.Id == EnrollmentId.FromGuid(scope.Id) && e.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.CollectionInstance => await db.CollectionInstances.AsNoTracking()
                .AnyAsync(c => c.Id == CollectionInstanceId.FromGuid(scope.Id) && c.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            NodeKinds.Observation => await db.Observations.AsNoTracking()
                .AnyAsync(o => o.Id == ObservationId.FromGuid(scope.Id) && o.StudyId == study, cancellationToken)
                .ConfigureAwait(false),
            _ => false,
        };

    internal static async Task<LockReadinessSnapshot> EvaluateReadinessAsync(
        KernelDbContext db,
        StudyId study,
        string studyCode,
        NodeRef scope,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        List<CollectionInstance> instances;
        List<Observation> coveredObservations;
        List<Enrollment>? scopedEnrollments = null;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;

            case NodeKinds.StudySite:
                var siteId = StudySiteId.FromGuid(scope.Id);
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study && e.StudySiteId == siteId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var siteEnrollmentIds = scopedEnrollments.Select(e => e.Id).ToList();
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.StudySiteId == siteId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = siteEnrollmentIds.Count == 0
                    ? []
                    : await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && siteEnrollmentIds.Contains(o.EnrollmentId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                break;

            case NodeKinds.Enrollment:
                var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                scopedEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => e.StudyId == study && e.Id == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.EnrollmentId == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study && o.EnrollmentId == enrollmentId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;

            case NodeKinds.CollectionInstance:
                var instanceId = CollectionInstanceId.FromGuid(scope.Id);
                instances = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.StudyId == study && c.Id == instanceId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var memberIds = instances.SelectMany(i => i.Members.Select(m => m.ObservationId)).Distinct().ToList();
                coveredObservations = memberIds.Count == 0
                    ? []
                    : await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && memberIds.Contains(o.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                break;

            default:
                instances = [];
                var observationId = ObservationId.FromGuid(scope.Id);
                coveredObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.StudyId == study && o.Id == observationId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
        }

        var obsById = coveredObservations.ToDictionary(o => o.Id);
        var missingMemberIds = instances
            .SelectMany(i => i.Members.Select(m => m.ObservationId))
            .Where(id => !obsById.ContainsKey(id))
            .Distinct()
            .ToList();
        if (missingMemberIds.Count > 0)
        {
            var extraObs = await db.Observations.AsNoTracking()
                .Where(o => missingMemberIds.Contains(o.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var extra in extraObs)
            {
                obsById[extra.Id] = extra;
            }
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
                var extraEnrollments = await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => extraEnrollmentIds.Contains(e.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
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
                : await db.Enrollments.AsNoTracking()
                    .Include(e => e.Periods)
                    .Where(e => enrollmentIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, cancellationToken)
                    .ConfigureAwait(false);
        }

        var versionIds = instances.Select(i => i.ProtocolVersionId).Distinct().ToList();
        var versionsById = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, ProtocolVersion>()
            : await db.ProtocolVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, cancellationToken)
                .ConfigureAwait(false);

        List<Assertion> openQueries = [];
        HashSet<AssertionId> answeredQueryIds = [];
        HashSet<(ObservationId ObservationId, int Revision)> verifiedRevisions = [];
        if (coveredObservations.Count > 0)
        {
            var targetGuids = coveredObservations.Select(o => o.Id.Value).ToList();
            var candidateAssertions = await db.Assertions.AsNoTracking()
                .Where(a => a.StudyId == study
                    && (a.Kind == AssertionKind.Queried || a.Kind == AssertionKind.Verified)
                    && a.RetractsId == null
                    && a.TargetKind == NodeKinds.Observation
                    && targetGuids.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (candidateAssertions.Count > 0)
            {
                var assertionIds = candidateAssertions.Select(a => a.Id).ToList();
                var retractedAssertions = await db.Assertions.AsNoTracking()
                    .Where(a => a.RetractsId != null && assertionIds.Contains(a.RetractsId.Value))
                    .Select(a => a.RetractsId!.Value)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var retractedSet = retractedAssertions.ToHashSet();

                openQueries = [.. candidateAssertions.Where(a => a.Kind == AssertionKind.Queried && !retractedSet.Contains(a.Id))];
                verifiedRevisions = candidateAssertions
                    .Where(a => a.Kind == AssertionKind.Verified && !retractedSet.Contains(a.Id) && a.TargetRevision is not null)
                    .Select(a => (ObservationId.FromGuid(a.TargetId), a.TargetRevision!.Value))
                    .ToHashSet();

                if (openQueries.Count > 0)
                {
                    var openGuids = openQueries.Select(q => q.Id.Value).ToList();
                    var candidateAnswers = await db.Assertions.AsNoTracking()
                        .Where(a => a.StudyId == study
                            && a.Kind == AssertionKind.QueryAnswered
                            && a.RetractsId == null
                            && a.TargetKind == NodeKinds.Assertion
                            && openGuids.Contains(a.TargetId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (candidateAnswers.Count > 0)
                    {
                        var answerIds = candidateAnswers.Select(a => a.Id).ToList();
                        var retractedAnswers = await db.Assertions.AsNoTracking()
                            .Where(a => a.RetractsId != null && answerIds.Contains(a.RetractsId.Value))
                            .Select(a => a.RetractsId!.Value)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var retractedAnswerSet = retractedAnswers.ToHashSet();
                        answeredQueryIds = candidateAnswers
                            .Where(a => !retractedAnswerSet.Contains(a.Id))
                            .Select(a => AssertionId.FromGuid(a.TargetId))
                            .ToHashSet();
                    }
                }
            }
        }

        Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>> formSignaturesByInstance = [];
        if (instances.Count > 0)
        {
            var instanceGuids = instances.Select(i => i.Id.Value).ToList();
            var formAttestations = await db.Attestations.AsNoTracking()
                .Where(a => a.TargetKind == NodeKinds.CollectionInstance
                    && instanceGuids.Contains(a.TargetId)
                    && (a.Meaning == AttestationMeaning.Approved || a.Meaning == AttestationMeaning.Responsibility))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            formSignaturesByInstance = formAttestations
                .GroupBy(a => CollectionInstanceId.FromGuid(a.TargetId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Attestation>)g.ToList());
        }

        Dictionary<StudySiteId, StudySite>? studySitesById = null;
        Dictionary<SiteId, Site>? sitesById = null;
        IReadOnlyList<Effectivity>? studyEffectivities = null;
        if (scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
            && enrollmentsById.Values.Any(LockReadinessEvaluator.NeedsScheduleEvaluation))
        {
            var scheduleStudySiteIds = enrollmentsById.Values
                .Where(LockReadinessEvaluator.NeedsScheduleEvaluation)
                .Select(e => e.StudySiteId)
                .Distinct()
                .ToList();
            studySitesById = await db.StudySites.AsNoTracking()
                .Where(s => scheduleStudySiteIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken)
                .ConfigureAwait(false);
            var rawSiteIds = studySitesById.Values.Select(s => s.SiteId).Distinct().ToList();
            sitesById = rawSiteIds.Count == 0
                ? new Dictionary<SiteId, Site>()
                : await db.Sites.AsNoTracking()
                    .Where(s => rawSiteIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken)
                    .ConfigureAwait(false);
            studyEffectivities = await db.Effectivities.AsNoTracking()
                .Include(e => e.Periods)
                .Where(e => e.StudyId == study)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var extraScheduleVersions = studyEffectivities
                .Select(e => e.ProtocolVersionId)
                .Where(id => !versionsById.ContainsKey(id))
                .Distinct()
                .ToList();
            if (extraScheduleVersions.Count > 0)
            {
                var loadedVersions = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => extraScheduleVersions.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var v in loadedVersions)
                {
                    versionsById[v.Id] = v;
                }
            }
        }

        var activeErrorFindings = await KernelValidationStore.LoadActiveErrorFindingsAsync(db, study, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<CollectionInstance>? containingInstances = null;
        if (scope.Kind == NodeKinds.Observation && coveredObservations.Count > 0)
        {
            var singleObsId = ObservationId.FromGuid(scope.Id);
            var containing = await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == study && c.Members.Any(m => m.ObservationId == singleObsId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (containing.Count > 0)
            {
                containingInstances = containing;
                var missingVersions = containing.Select(c => c.ProtocolVersionId).Where(id => !versionsById.ContainsKey(id)).Distinct().ToList();
                if (missingVersions.Count > 0)
                {
                    var extraVersions = await db.ProtocolVersions.AsNoTracking()
                        .Where(v => missingVersions.Contains(v.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var v in extraVersions)
                    {
                        versionsById[v.Id] = v;
                    }
                }
            }
        }
        else if (scope.Kind == NodeKinds.CollectionInstance && activeErrorFindings.Count > 0 && enrollmentsById.Count > 0)
        {
            var siblingEnrollmentIds = enrollmentsById.Keys.ToList();
            var siblingInstances = await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == study && siblingEnrollmentIds.Contains(c.EnrollmentId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (siblingInstances.Count > instances.Count)
            {
                containingInstances = siblingInstances;
                var extraSiblingObsIds = siblingInstances
                    .SelectMany(c => c.Members.Select(m => m.ObservationId))
                    .Where(id => !obsById.ContainsKey(id))
                    .Distinct()
                    .ToList();
                if (extraSiblingObsIds.Count > 0)
                {
                    var extraSiblingObs = await db.Observations.AsNoTracking()
                        .Where(o => extraSiblingObsIds.Contains(o.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var extra in extraSiblingObs)
                    {
                        obsById[extra.Id] = extra;
                    }
                }

                var missingVersions = siblingInstances.Select(c => c.ProtocolVersionId).Where(id => !versionsById.ContainsKey(id)).Distinct().ToList();
                if (missingVersions.Count > 0)
                {
                    var extraVersions = await db.ProtocolVersions.AsNoTracking()
                        .Where(v => missingVersions.Contains(v.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var v in extraVersions)
                    {
                        versionsById[v.Id] = v;
                    }
                }
            }
        }

        var unverifiedObservationIds = coveredObservations
            .Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked } && !verifiedRevisions.Contains((o.Id, o.Revision)))
            .Select(o => o.Id);

        var candidateBlindedObservations = openQueries
            .Select(q => ObservationId.FromGuid(q.TargetId))
            .Concat(unverifiedObservationIds)
            .Concat(activeErrorFindings
                .Select(f => f.Target.Node is { Kind: NodeKinds.Observation } obsNode ? ObservationId.FromGuid(obsNode.Id) : ObservationId.Empty)
                .Where(id => !id.IsEmpty))
            .Concat(activeErrorFindings.Count > 0
                ? coveredObservations.Select(o => o.Id)
                    .Concat((containingInstances ?? instances).SelectMany(i => i.Members.Select(m => m.ObservationId)))
                : [])
            .Where(obsById.ContainsKey)
            .Select(id => obsById[id])
            .DistinctBy(o => o.Id)
            .ToList();
        var blindedObservations = candidateBlindedObservations.Count == 0
            ? new HashSet<ObservationId>()
            : await BlindedObservationClassifier.ResolveBlindedObservationsAsync(db, study, candidateBlindedObservations, cancellationToken).ConfigureAwait(false);

        return LockReadinessEvaluator.BuildSnapshot(
            study,
            studyCode,
            scope,
            instances,
            obsById,
            enrollmentsById,
            versionsById,
            openQueries,
            answeredQueryIds,
            blindedObservations,
            containingInstances,
            activeErrorFindings,
            verifiedRevisions,
            formSignaturesByInstance,
            evaluatedAt,
            studySitesById,
            sitesById,
            studyEffectivities);
    }
}

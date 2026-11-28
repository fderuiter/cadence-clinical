using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Validation;

internal static class ValidationFindingBlindingResolver
{
    internal static async Task<(
        Dictionary<ValidationFindingId, EnrollmentId> Enrollments,
        Dictionary<ValidationFindingId, IReadOnlySet<string>> BlindedVariables,
        Dictionary<ValidationFindingId, ObservationId> BlindedObservations)> ResolveBlindingContextAsync(
        KernelDbContext db,
        IReadOnlyList<FindingState> states,
        IReadOnlyDictionary<(ValidationRunId RunId, ValidationFindingId FindingId), ValidationDetectionRecord> detections,
        CancellationToken cancellationToken)
    {
        var formTargetIds = new HashSet<CollectionInstanceId>();
        var obsTargetIds = new HashSet<ObservationId>();

        foreach (var state in states)
        {
            var target = detections[(state.LastDetectedRun, state.Finding)].Target;
            if (target.Kind == FindingTargetKind.DatasetRecord && Guid.TryParse(target.Key, out var fg))
            {
                formTargetIds.Add(CollectionInstanceId.FromGuid(fg));
            }
            else if (target.Kind == FindingTargetKind.Observation && Guid.TryParse(target.Key, out var og))
            {
                obsTargetIds.Add(ObservationId.FromGuid(og));
            }
        }

        if (formTargetIds.Count == 0 && obsTargetIds.Count == 0)
        {
            return ([], [], []);
        }

        var formTargetList = formTargetIds.ToList();
        var targetForms = formTargetList.Count == 0
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Where(c => formTargetList.Contains(c.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var obsTargetList = obsTargetIds.ToList();
        var targetObservations = obsTargetList.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : await db.Observations.AsNoTracking()
                .Where(o => obsTargetList.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var enrollmentIds = targetForms.Select(c => c.EnrollmentId)
            .Concat(targetObservations.Values.Select(o => o.EnrollmentId))
            .Distinct()
            .ToList();
        if (enrollmentIds.Count == 0)
        {
            return ([], [], []);
        }

        var enrollmentForms = await db.CollectionInstances.AsNoTracking()
            .Where(c => enrollmentIds.Contains(c.EnrollmentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var formsById = enrollmentForms.ToDictionary(c => c.Id);

        var studyIds = detections.Values.Select(d => d.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var allObsIds = enrollmentForms.SelectMany(c => c.Members.Select(m => m.ObservationId))
            .Concat(obsTargetList)
            .Distinct()
            .ToList();
        var allObservations = allObsIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : await db.Observations.AsNoTracking()
                .Where(o => allObsIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var conceptIds = rawVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(allObservations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var blindedTokensByEnrollment = new Dictionary<EnrollmentId, HashSet<string>>();
        var blindedObsByEnrollment = new Dictionary<EnrollmentId, ObservationId>();
        var blindedObsSet = new HashSet<ObservationId>();
        foreach (var instance in enrollmentForms)
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(instance.StudyId) ?? [];
            var spec = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId)?.CollectionSpecs
                .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            var items = spec?.Items ?? [];

            if (!blindedTokensByEnrollment.TryGetValue(instance.EnrollmentId, out var tokens))
            {
                tokens = new HashSet<string>(StringComparer.Ordinal);
                blindedTokensByEnrollment[instance.EnrollmentId] = tokens;
            }

            foreach (var item in items)
            {
                concepts.TryGetValue(item.Concept.ConceptId, out var concept);
                if (BlindedObservationClassifier.IsBlindedTreatment(
                        item.Concept.ConceptId,
                        new TextValue(string.Empty),
                        concept,
                        instance.CollectionSpecId,
                        item.Id,
                        studyDesigns))
                {
                    tokens.Add(item.Id);
                    tokens.Add($"{instance.CollectionSpecId}.{item.Id}");
                }
            }

            foreach (var member in instance.Members)
            {
                if (!allObservations.TryGetValue(member.ObservationId, out var obs))
                {
                    continue;
                }

                concepts.TryGetValue(obs.ConceptId, out var concept);
                if (tokens.Contains(member.ItemId)
                    || BlindedObservationClassifier.IsBlindedTreatment(
                        obs.ConceptId,
                        obs.Value,
                        concept,
                        instance.CollectionSpecId,
                        member.ItemId,
                        studyDesigns))
                {
                    tokens.Add(member.ItemId);
                    tokens.Add($"{instance.CollectionSpecId}.{member.ItemId}");
                    blindedObsSet.Add(obs.Id);
                    blindedObsByEnrollment.TryAdd(instance.EnrollmentId, obs.Id);
                }
            }
        }

        foreach (var obs in targetObservations.Values.Where(o => !blindedObsSet.Contains(o.Id)))
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(obs.StudyId) ?? [];
            concepts.TryGetValue(obs.ConceptId, out var concept);
            if (BlindedObservationClassifier.IsBlindedTreatment(
                    obs.ConceptId,
                    obs.Value,
                    concept,
                    collectionSpecId: null,
                    itemId: null,
                    studyDesigns))
            {
                blindedObsSet.Add(obs.Id);
                blindedObsByEnrollment.TryAdd(obs.EnrollmentId, obs.Id);
            }
        }

        var enrollmentMap = new Dictionary<ValidationFindingId, EnrollmentId>();
        var blindedVarsMap = new Dictionary<ValidationFindingId, IReadOnlySet<string>>();
        var blindedObsMap = new Dictionary<ValidationFindingId, ObservationId>();
        foreach (var state in states)
        {
            var detection = detections[(state.LastDetectedRun, state.Finding)];
            var target = detection.Target;
            EnrollmentId? enrollmentId = null;
            ObservationId? directObsId = null;
            var isDirectBlindedObs = false;

            if (target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(target.Key, out var fg)
                && formsById.TryGetValue(CollectionInstanceId.FromGuid(fg), out var targetForm))
            {
                enrollmentId = targetForm.EnrollmentId;
            }
            else if (target.Kind == FindingTargetKind.Observation
                && Guid.TryParse(target.Key, out var og)
                && allObservations.TryGetValue(ObservationId.FromGuid(og), out var targetObs))
            {
                enrollmentId = targetObs.EnrollmentId;
                directObsId = targetObs.Id;
                isDirectBlindedObs = blindedObsSet.Contains(targetObs.Id);
            }

            if (enrollmentId is not { } eid)
            {
                continue;
            }

            enrollmentMap[state.Finding] = eid;
            var matched = new HashSet<string>(StringComparer.Ordinal);
            if (blindedTokensByEnrollment.TryGetValue(eid, out var tokens) && tokens.Count > 0)
            {
                foreach (var variable in detection.ObservedValues.Select(o => o.Variable).Where(tokens.Contains))
                {
                    matched.Add(variable);
                }

                if (!string.IsNullOrWhiteSpace(target.Variable) && tokens.Contains(target.Variable))
                {
                    matched.Add(target.Variable);
                }
            }

            if (isDirectBlindedObs)
            {
                foreach (var variable in detection.ObservedValues.Select(o => o.Variable))
                {
                    matched.Add(variable);
                }

                matched.Add(string.IsNullOrWhiteSpace(target.Variable) ? "value" : target.Variable);
            }

            if (matched.Count > 0)
            {
                blindedVarsMap[state.Finding] = matched;
                if (isDirectBlindedObs && directObsId is { } dObs)
                {
                    blindedObsMap[state.Finding] = dObs;
                }
                else if (blindedObsByEnrollment.TryGetValue(eid, out var repObs))
                {
                    blindedObsMap[state.Finding] = repObs;
                }
            }
        }

        return (enrollmentMap, blindedVarsMap, blindedObsMap);
    }
}
